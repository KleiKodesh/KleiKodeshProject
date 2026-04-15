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

# ── 1. Wipe VSTO Release folder (ensures clean VSTO output) ──────────────────
$vstoRelease = Join-Path $ProjectRoot "KleiKodeshVsto\bin\Release"
if (Test-Path $vstoRelease) {
    Remove-Item $vstoRelease -Recurse -Force
    Write-Host "Deleted VSTO Release folder." -ForegroundColor Gray
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

# ── 4. Build WPF installer (prebuild event builds VSTO via MSBuild) ───────────
Write-Host ""
Write-Host "Building WPF installer (Release|AnyCPU)..." -ForegroundColor Yellow
dotnet build $WpfProjectPath -c Release `
    -p:VstoConfiguration=Release -p:VstoPlatform=AnyCPU `
    --verbosity normal
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: WPF build failed." -ForegroundColor Red; exit 1 }

# ── 5. NSIS wrapper ───────────────────────────────────────────────────────────
$nsisExe = @(
    "C:\Program Files (x86)\NSIS\makensis.exe",
    "C:\Program Files\NSIS\makensis.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $nsisExe) {
    Write-Host "ERROR: NSIS not found. Install from https://nsis.sourceforge.io/" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $ReleasesDir)) { New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null }

Write-Host "Building NSIS wrapper ($version)..." -ForegroundColor Yellow
& $nsisExe "/DPRODUCT_VERSION=$version" "/DOUTPUT_DIR=$ReleasesDir" $NsisScriptPath
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: NSIS build failed." -ForegroundColor Red; exit 1 }

$installerPath = Join-Path $ReleasesDir "KleiKodeshSetup-$version.exe"
if (-not (Test-Path $installerPath)) {
    Write-Host "ERROR: Installer not found at $installerPath" -ForegroundColor Red; exit 1
}

Write-Host ""
Write-Host "SUCCESS: KleiKodeshSetup-$version.exe" -ForegroundColor Green

# ── 6. GitHub release ─────────────────────────────────────────────────────────
if ($NoRelease) { Write-Host "GitHub release skipped." -ForegroundColor Yellow; exit 0 }

Write-Host ""
Write-Host "Creating GitHub release..." -ForegroundColor Yellow

gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "GitHub CLI not authenticated — skipping release. Run: gh auth login" -ForegroundColor Yellow
    exit 0
}

# Delete existing release/tag if present
gh release view $version 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Existing release $version found — deleting..." -ForegroundColor Yellow
    gh release delete $version --yes
}

$notes  = New-ReleaseNotes -Version $version -Source $ReleaseNotesSource
$branch = git rev-parse --abbrev-ref HEAD

gh release create $version $installerPath `
    --title "KleiKodesh $version" `
    --notes $notes `
    --target $branch

if ($LASTEXITCODE -eq 0) {
    $repoUrl = gh repo view --json url --jq .url
    Write-Host "SUCCESS: $repoUrl/releases/tag/$version" -ForegroundColor Green
} else {
    Write-Host "ERROR: GitHub release creation failed." -ForegroundColor Red
    exit 1
}
