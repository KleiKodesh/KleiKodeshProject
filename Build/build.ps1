<#
.SYNOPSIS
    Builds KleiKodeshSetup-v<version>.exe and KitveiHakodeshPortable-v<version>.zip from a
    clean tree. Used by the release workflow and by build.bat (debug builds).

.DESCRIPTION
    1. Empties the folders that are packed whole (the add-in's and the portable app's
       bin\Release, the service payload), so nothing left from earlier builds or from
       running the app there (caches, indexes, WebView2 profiles, old DLLs) can reach the
       setup or the zip. Other bin\ and obj\ folders are left alone.
    2. Restores packages, stamps the version, and builds: Vue frontend, typing tutor,
       Kitvei Hakodesh service (native AOT), the Word add-in (Release; includes the
       Kitvei Hakodesh app) and the portable app.
    3. Builds the setup exe (Rust), with the release notes built in for its
       "What's new" screen, and packs the product files into it.

    The version stamp in tracked files is put back afterwards, so a build leaves the
    worktree as it found it.

.PARAMETER Version
    X.Y.Z. Debug builds use 0.0.0.

.PARAMETER Notes
    release-notes.json written by .github/scripts/ai-release-plan.ps1 (optional).
#>
param(
    [string] $Version = "0.0.0",
    [string] $Notes
)

$ErrorActionPreference = "Stop"
$Version = $Version.TrimStart('v')
# Resolved now, relative to where the script was started: later steps change folder.
if ($Notes) { $Notes = (Resolve-Path $Notes).Path }

$BuildDir     = $PSScriptRoot
$ProjectRoot  = Split-Path -Parent $BuildDir
$InstallerDir = Join-Path $BuildDir "installer-rs"
$ReleasesDir  = Join-Path $BuildDir "releases"
$VstoProject  = Join-Path $ProjectRoot "KleiKodeshVsto\KleiKodeshVsto.csproj"
$LibProject   = Join-Path $ProjectRoot "KitveiHakodesh\CSharpBackend\KitveiHakodeshLib\KitveiHakodeshLib.csproj"
$PortableProject = Join-Path $ProjectRoot "KitveiHakodesh\CSharpBackend\KitveiHakodeshDemoApp\KitveiHakodeshDemoApp.csproj"
$PortableOutput  = Join-Path $ProjectRoot "KitveiHakodesh\CSharpBackend\KitveiHakodeshDemoApp\bin\Release"
$PortableInfo    = Join-Path $ProjectRoot "KitveiHakodesh\CSharpBackend\KitveiHakodeshDemoApp\Properties\AssemblyInfo.cs"
$VueDir       = Join-Path $ProjectRoot "KitveiHakodesh\vue-frontend"
$TutorDir     = Join-Path $ProjectRoot "hebrew-typing-tutor"
$CoreOutput   = Join-Path $ProjectRoot "KleiKodeshVsto\bin\Release"
$SetupFile    = Join-Path $ReleasesDir "KleiKodeshSetup-v$Version.exe"
$PortableZip  = Join-Path $ReleasesDir "KitveiHakodeshPortable-v$Version.zip"

$watch = [Diagnostics.Stopwatch]::StartNew()
function Step([string] $Name) {
    Write-Host ""
    Write-Host ("[{0:mm\:ss}] {1}" -f $watch.Elapsed, $Name) -ForegroundColor Cyan
}

function Assert-Success([string] $What) {
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." }
}

function Find-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($found) { return $found }
    }
    $onPath = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw "MSBuild not found."
}

# --- 1. Clean ----------------------------------------------------------------------
# Only the folders whose whole content is packed: whatever sits in them ships. Running the
# app from a Release folder leaves caches, indexes and WebView2 profiles there. Other bin\
# and obj\ folders stay: Release builds keep them correct, and dev servers or other builds
# may be using them.
$PackedOutputs = @(
    $CoreOutput,
    $PortableOutput,
    (Join-Path $ProjectRoot "KitveiHakodesh\CSharpBackend\KitveiHakodeshService\bin\LibPayload")
)

function Clear-PackedOutputs {
    foreach ($dir in $PackedOutputs) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        Remove-Item -LiteralPath $dir -Recurse -Force
        Write-Host "  removed $($dir.Substring($ProjectRoot.Length + 1))" -ForegroundColor DarkGray
    }
}

# --- Version stamp (tracked files; restored at the end) ----------------------------
# The portable app's AssemblyInfo.cs is UTF-8 WITH BOM: without it the compiler reads
# the Hebrew title literals as ANSI. Bytes are saved and written back exactly.
$Stamped = @($PortableInfo, $VstoProject)
$Snapshot = @{}
foreach ($file in $Stamped) { $Snapshot[$file] = [IO.File]::ReadAllBytes($file) }

function Set-PortableVersion {
    $four = "$Version.0"
    $utf8Bom = New-Object Text.UTF8Encoding($true)
    $text = [IO.File]::ReadAllText($PortableInfo, $utf8Bom)
    $text = $text `
        -replace '(\[assembly:\s*AssemblyVersion\(")[^"]*("\)\])', "`${1}$four`${2}" `
        -replace '(\[assembly:\s*AssemblyFileVersion\(")[^"]*("\)\])', "`${1}$four`${2}"
    [IO.File]::WriteAllText($PortableInfo, $text, $utf8Bom)
}

# The add-in's manifests must be signed, but any key will do: the installer trusts the
# manifest's own public key on every install. A fresh 5-year self-signed key per build.
function Set-SigningCertificate {
    $pfx = Join-Path $ProjectRoot "KleiKodeshVsto\KleiKodesh_TemporaryKey.pfx"
    $cert = New-SelfSignedCertificate -Subject "CN=KleiKodesh" -Type CodeSigningCert `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5)
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (New-Object Security.SecureString) | Out-Null
    $utf8 = New-Object Text.UTF8Encoding($false)
    $text = [IO.File]::ReadAllText($VstoProject, $utf8)
    $text = $text -replace '<ManifestCertificateThumbprint>[^<]*<', "<ManifestCertificateThumbprint>$($cert.Thumbprint)<"
    [IO.File]::WriteAllText($VstoProject, $text, $utf8)
}

try {
    Step "Empty the packed output folders"
    Clear-PackedOutputs

    Step "Restore packages"
    $msbuild = Find-MSBuild
    & $msbuild (Join-Path $ProjectRoot "KleiKodeshProject.slnx") -t:restore -v:minimal -nologo
    Assert-Success "msbuild restore"
    Push-Location $ProjectRoot
    try {
        foreach ($config in git ls-files '*packages.config') {
            nuget restore $config -PackagesDirectory (Join-Path (Split-Path $config) '..\packages') -NonInteractive
            Assert-Success "nuget restore $config"
        }
    } finally { Pop-Location }
    # npm ci empties node_modules first, which fails half-way while a local dev server
    # holds esbuild.exe - so locally the lock file is installed in place.
    $npmInstall = @(if ($env:CI) { "ci" } else { "install", "--no-save", "--no-audit", "--no-fund" })
    foreach ($dir in $VueDir, $TutorDir) {
        Push-Location $dir
        try { npm @npmInstall; Assert-Success "npm install ($dir)" } finally { Pop-Location }
    }

    Step "Version $Version and signing certificate"
    Set-PortableVersion
    Set-SigningCertificate

    Step "Vue frontend"
    Push-Location $VueDir
    try { npm run build; Assert-Success "Vue build" } finally { Pop-Location }

    Step "Typing tutor"
    Push-Location $TutorDir
    try { npm run build; Assert-Success "Typing tutor build" } finally { Pop-Location }

    Step "Kitvei Hakodesh service (native AOT)"
    & $msbuild $LibProject /t:PublishKitveiHakodeshService /p:Configuration=Release /nologo /v:minimal
    Assert-Success "Service publish"

    Step "Word add-in (Release)"
    & $msbuild $VstoProject /p:Configuration=Release /nologo /v:minimal
    Assert-Success "Add-in build"
    if (-not (Test-Path (Join-Path $CoreOutput "KleiKodesh.vsto"))) { throw "Add-in output has no KleiKodesh.vsto: $CoreOutput" }

    Step "Portable app (Release)"
    & $msbuild $PortableProject /p:Configuration=Release /p:Platform=AnyCPU /nologo /v:minimal
    Assert-Success "Portable app build"

    Step "Setup exe (Rust)"
    Push-Location $InstallerDir
    try {
        $env:KK_VERSION = $Version
        $env:KK_NOTES = if ($Notes) { $Notes } else { "" }
        cargo build --release
        Assert-Success "cargo build"
    } finally {
        Pop-Location
        Remove-Item Env:KK_VERSION, Env:KK_NOTES -ErrorAction SilentlyContinue
    }

    Step "Pack"
    New-Item -ItemType Directory -Force $ReleasesDir | Out-Null
    & (Join-Path $InstallerDir "target\release\pack-payload.exe") `
        --exe (Join-Path $InstallerDir "target\release\kleikodesh-setup.exe") `
        --core $CoreOutput --tutor (Join-Path $TutorDir "dist") --out $SetupFile
    Assert-Success "Pack"
    if (Test-Path $PortableZip) { Remove-Item $PortableZip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($PortableOutput, $PortableZip)
} finally {
    foreach ($file in $Snapshot.Keys) { [IO.File]::WriteAllBytes($file, $Snapshot[$file]) }
}

Write-Host ""
Write-Host ("Done in {0:mm\:ss}:" -f $watch.Elapsed) -ForegroundColor Green
Write-Host "  $SetupFile"
Write-Host "  $PortableZip"
