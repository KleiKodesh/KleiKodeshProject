# build-installer.ps1 — pure orchestration, no interactive prompts.
# Called by build-menu.ps1 or directly from the CLI.
#
# Examples:
#   .\build-installer.ps1 -VersionIncrement patch -ReleaseNotesSource commits
#   .\build-installer.ps1 -ManualVersion v3.5.0 -NoRelease
#   .\build-installer.ps1 -ManualVersion v3.5.0 -NoRelease -NoClean
param(
    [ValidateSet("major","minor","patch")]
    [string]$VersionIncrement,          # auto-increment type

    [string]$ManualVersion,             # exact version, e.g. "v3.5.0"

    [ValidateSet("commits","file","both")]
    [string]$ReleaseNotesSource = "commits",

    [switch]$NoRelease,                 # skip GitHub release
    [switch]$NoClean                    # skip solution clean step
)

. "$PSScriptRoot\build-helpers.ps1"

# ── Validate inputs ───────────────────────────────────────────────────────────
if ($ManualVersion) {
    if ($ManualVersion -notmatch '^v') { $ManualVersion = "v$ManualVersion" }
    if ($ManualVersion -notmatch '^v\d+\.\d+\.\d+$') {
        Write-Host "ERROR: '$ManualVersion' is not valid semver (expected vX.Y.Z)" -ForegroundColor Red
        exit 1
    }
} elseif (-not $VersionIncrement) {
    Write-Host "ERROR: Provide -VersionIncrement or -ManualVersion" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== KleiKodesh Build ===" -ForegroundColor Green
Write-Host "Project root : $ProjectRoot" -ForegroundColor Gray
if ($ManualVersion) { Write-Host "Version      : $ManualVersion (manual)"          -ForegroundColor Cyan }
else                { Write-Host "Version      : increment $VersionIncrement"      -ForegroundColor Cyan }
Write-Host "Notes source : $ReleaseNotesSource" -ForegroundColor Gray
Write-Host "GitHub rel.  : $(if ($NoRelease) { 'skip' } else { 'yes' })" -ForegroundColor Gray
Write-Host ""

# ── 1. Wipe VSTO Release folders (ensures clean VSTO output for all variants) ─
foreach ($folder in @("bin\Release", "bin\Release-x64", "bin\Release-x86")) {
    $path = Join-Path $ProjectRoot "KleiKodeshVsto\$folder"
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
        Write-Host "Deleted $folder" -ForegroundColor Gray
    }
}

# ── 2. Update version ─────────────────────────────────────────────────────────
Write-Host "Updating version..." -ForegroundColor Yellow
if ($ManualVersion) {
    & powershell -ExecutionPolicy Bypass -File $UpdateVersionScript `
        -FilePath $AddinInstallerPath -ManualVersion $ManualVersion
} else {
    & powershell -ExecutionPolicy Bypass -File $UpdateVersionScript `
        -FilePath $AddinInstallerPath -IncrementType $VersionIncrement
}
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: UpdateVersion.ps1 failed." -ForegroundColor Red; exit 1 }

$version = Get-CurrentVersion
Write-Host "Version: $version" -ForegroundColor Cyan

# ── 3. Clean ──────────────────────────────────────────────────────────────────
if (-not $NoClean) { Invoke-SolutionClean }

# ── 4. Build three VSTO+installer variants ───────────────────────────────────
#
# Each variant builds the VSTO at the given platform, packs it into KleiKodesh.zip
# (via the pre-build target), then wraps it with NSIS.
# The three output files are:
#   KleiKodeshSetup-{version}-x64.exe   — for 64-bit Word (most users)
#   KleiKodeshSetup-{version}-x86.exe   — for 32-bit Word
#   KleiKodeshSetup-{version}.exe       — AnyCPU fallback (both native folders)

$nsisExe = @(
    "C:\Program Files (x86)\NSIS\makensis.exe",
    "C:\Program Files\NSIS\makensis.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $nsisExe) {
    Write-Host "ERROR: NSIS not found. Install from https://nsis.sourceforge.io/" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $ReleasesDir)) { New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null }

# Helper: build one variant and produce its NSIS installer
function Build-Variant {
    param(
        [string]$Platform,      # AnyCPU | x64 | x86
        [string]$Suffix         # "" | "-x64" | "-x86"
    )

    Write-Host ""
    Write-Host "Building WPF installer (Release|$Platform)..." -ForegroundColor Yellow

    # SDK-style projects ignore PropertyGroup conditions — must pass OutputPath explicitly
    $outputPath = switch ($Platform) {
        "x64"    { "bin\Release-x64\net48\" }
        "x86"    { "bin\Release-x86\net48\" }
        "AnyCPU" { "bin\Release\net48\" }
    }

    dotnet build $WpfProjectPath -c Release `
        -p:VstoConfiguration=Release -p:VstoPlatform=$Platform `
        -p:InstallerVariant=$Platform `
        -p:OutputPath=$outputPath `
        --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: WPF build failed for $Platform." -ForegroundColor Red
        exit 1
    }

    $wpfExeDir = Join-Path (Split-Path $WpfProjectPath) $outputPath
    $wpfExePath = Join-Path $wpfExeDir "KleiKodeshVstoInstallerWpf.exe"
    $outFile = Join-Path $ReleasesDir "KleiKodeshSetup-${version}${Suffix}.exe"

    Write-Host "Building NSIS wrapper ($version$Suffix)..." -ForegroundColor Yellow
    & $nsisExe `
        "/DPRODUCT_VERSION=$version" `
        "/DOUTPUT_DIR=$ReleasesDir" `
        "/DOUTPUT_SUFFIX=$Suffix" `
        "/DWPF_EXE_PATH=$wpfExePath" `
        $NsisScriptPath
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: NSIS build failed for $Platform." -ForegroundColor Red
        exit 1
    }

    if (-not (Test-Path $outFile)) {
        Write-Host "ERROR: Expected installer not found: $outFile" -ForegroundColor Red
        exit 1
    }

    Write-Host "OK: $(Split-Path -Leaf $outFile)" -ForegroundColor Green
    # Use script scope to return the path — avoids PowerShell function return value pollution
    $script:LastBuiltInstaller = $outFile
}

Build-Variant -Platform "x64"    -Suffix "-x64"
$installerX64 = $script:LastBuiltInstaller

Build-Variant -Platform "x86"    -Suffix "-x86"
$installerX86 = $script:LastBuiltInstaller

Build-Variant -Platform "AnyCPU" -Suffix ""
$installerAny = $script:LastBuiltInstaller

Write-Host ""
Write-Host "All three variants built successfully." -ForegroundColor Green

# ── 5. GitHub release ─────────────────────────────────────────────────────────
if ($NoRelease) { Write-Host "GitHub release skipped." -ForegroundColor Yellow; exit 0 }

Write-Host ""
Write-Host "Creating GitHub release..." -ForegroundColor Yellow

$ghCmd = Get-Command gh -ErrorAction SilentlyContinue
if (-not $ghCmd) {
    Write-Host "GitHub CLI (gh) not found — skipping release. Install from: https://cli.github.com/" -ForegroundColor Yellow
    exit 0
}

gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "GitHub CLI not authenticated — skipping release. Run: gh auth login" -ForegroundColor Yellow
    exit 0
}

# Delete existing release/tag if present
gh release view $version --repo KleiKodesh/KleiKodeshProject 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Existing release $version found — deleting..." -ForegroundColor Yellow
    gh release delete $version --repo KleiKodesh/KleiKodeshProject --yes
}

$notes  = New-ReleaseNotes -Version $version -Source $ReleaseNotesSource
$branch = git rev-parse --abbrev-ref HEAD

# Upload all three installers to the same release.
# Files are uploaded one at a time to avoid Windows command-line length limits.
gh release create $version `
    --repo KleiKodesh/KleiKodeshProject `
    --title "KleiKodesh $version" `
    --notes $notes `
    --target $branch
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: GitHub release creation failed." -ForegroundColor Red
    exit 1
}

foreach ($asset in @($installerX64, $installerX86, $installerAny)) {
    Write-Host "Uploading $(Split-Path -Leaf $asset)..." -ForegroundColor Yellow
    gh release upload $version $asset --repo KleiKodesh/KleiKodeshProject --clobber
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Upload failed for $(Split-Path -Leaf $asset)" -ForegroundColor Red
        exit 1
    }
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "SUCCESS: https://github.com/KleiKodesh/KleiKodeshProject/releases/tag/$version" -ForegroundColor Green
} else {
    Write-Host "ERROR: GitHub release creation failed." -ForegroundColor Red
    exit 1
}
