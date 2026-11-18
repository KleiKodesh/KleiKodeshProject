<#
.SYNOPSIS
    Builds KleiKodeshSetup-<version>.exe from a clean tree.

.DESCRIPTION
    1. Deletes every bin\ and obj\ folder in the repository, so nothing left over from
       earlier builds or from running an app (caches, indexes, WebView2 profiles, old
       DLLs) can end up in the setup.
    2. Restores packages and builds the parts: Vue frontend, Kitvei Hakodesh service
       (native AOT), the Word add-in (Release, includes the Kitvei Hakodesh app), and
       the typing tutor.
    3. Builds the setup exe and the packer (Rust), then packs:
         core  = KleiKodeshVsto\bin\Release
         tutor = hebrew-typing-tutor\dist

    Meant for CI. Locally it removes every bin/obj in the shared worktree, including
    other sessions' builds.

.EXAMPLE
    .\build.ps1 -Version 11.0.0
#>
param(
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\..\scripts\build-helpers.ps1"   # $ProjectRoot, Find-MSBuild, Update-SigningCertificate, steps

$InstallerDir = $PSScriptRoot
$VstoProject  = Join-Path $ProjectRoot "KleiKodeshVsto\KleiKodeshVsto.csproj"
$LibProject   = Join-Path $ProjectRoot "KitveiHakodesh\CSharpBackend\KitveiHakodeshLib\KitveiHakodeshLib.csproj"
$VueDir       = Join-Path $ProjectRoot "KitveiHakodesh\vue-frontend"
$TutorDir     = Join-Path $ProjectRoot "hebrew-typing-tutor"
$CoreOutput   = Join-Path $ProjectRoot "KleiKodeshVsto\bin\Release"
$Output       = Join-Path $ReleasesDir "KleiKodeshSetup-v$Version.exe"

function Assert-Success([string] $What) {
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." }
}

# Folders never searched: dependencies, git, the Rust build (cargo cleans itself).
$Skip = @("node_modules", ".git", "packages", "target", "KleiKodeshProject-work")

# A bin\ or obj\ folder is build output unless it holds source: anything git tracks, or a
# Rust crate's src\bin (installer-rs\src\bin is the packer's code). Rust builds go to
# target\, never bin\ or obj\.
function Test-BuildOutput([System.IO.DirectoryInfo] $Folder) {
    if ($Folder.Name -notin "bin", "obj") { return $false }
    if (git -C $ProjectRoot ls-files -- $Folder.FullName) { return $false }
    $crate = $Folder.Parent.Name -eq "src" -and (Test-Path (Join-Path $Folder.Parent.Parent.FullName "Cargo.toml"))
    return -not $crate
}

function Remove-BuildFolders([string] $Dir) {
    foreach ($child in Get-ChildItem -LiteralPath $Dir -Directory -Force) {
        if ($Skip -contains $child.Name) { continue }
        if (Test-BuildOutput $child) {
            Remove-Item -LiteralPath $child.FullName -Recurse -Force
            Write-Host "  removed $($child.FullName.Substring($ProjectRoot.Length + 1))" -ForegroundColor DarkGray
        } else {
            Remove-BuildFolders $child.FullName
        }
    }
}

Start-BuildStep "Delete every bin\ and obj\ folder"
Remove-BuildFolders $ProjectRoot

Start-BuildStep "Restore packages"
$msbuild = Find-MSBuild
if (-not $msbuild) { throw "MSBuild not found." }
& $msbuild (Join-Path $ProjectRoot "KleiKodeshProject.slnx") -t:restore -v:minimal -nologo
Assert-Success "msbuild restore"
Push-Location $ProjectRoot
try {
    foreach ($config in git ls-files '*packages.config') {
        nuget restore $config -PackagesDirectory (Join-Path (Split-Path $config) '..\packages') -NonInteractive
        Assert-Success "nuget restore $config"
    }
} finally { Pop-Location }
$npmInstall = @(if ($env:CI) { "ci" } else { "install", "--no-save", "--no-audit", "--no-fund" })
foreach ($dir in $VueDir, $TutorDir) {
    Push-Location $dir
    try { npm @npmInstall; Assert-Success "npm install in $dir" } finally { Pop-Location }
}

Start-BuildStep "Vue frontend"
Push-Location $VueDir
try { npm run build; Assert-Success "Vue build" } finally { Pop-Location }

Start-BuildStep "Typing tutor"
Push-Location $TutorDir
try { npm run build; Assert-Success "Typing tutor build" } finally { Pop-Location }

Start-BuildStep "Kitvei Hakodesh service (native AOT)"
& $msbuild $LibProject /t:PublishKitveiHakodeshService /p:Configuration=Release /nologo /v:minimal
Assert-Success "Service publish"

Start-BuildStep "Word add-in (Release)"
Update-SigningCertificate
& $msbuild $VstoProject /p:Configuration=Release /nologo /v:minimal
Assert-Success "Add-in build"
if (-not (Test-Path (Join-Path $CoreOutput "KleiKodesh.vsto"))) { throw "Add-in output has no KleiKodesh.vsto: $CoreOutput" }

Start-BuildStep "Setup exe (Rust)"
Push-Location $InstallerDir
try {
    $env:KK_VERSION = $Version
    cargo build --release
    Assert-Success "cargo build"
} finally { Pop-Location; Remove-Item Env:KK_VERSION -ErrorAction SilentlyContinue }

Start-BuildStep "Pack"
New-Item -ItemType Directory -Force $ReleasesDir | Out-Null
& (Join-Path $InstallerDir "target\release\pack-payload.exe") `
    --exe (Join-Path $InstallerDir "target\release\kleikodesh-setup.exe") `
    --core $CoreOutput --tutor (Join-Path $TutorDir "dist") --out $Output
Assert-Success "Pack"

Complete-BuildSteps
Write-Host "Built $Output" -ForegroundColor Green
