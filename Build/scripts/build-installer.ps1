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
    [switch]$NoClean,                   # skip solution clean step
    [switch]$DeleteFtsIndex,            # delete FTS index on install (forces reindex on user machines)
    [switch]$ForceCleanInstall          # wipe + reinstall on launch (התקן behaves like תיקון)
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
Write-Host "FTS index    : $(if ($DeleteFtsIndex) { 'DELETE on install (force reindex)' } else { 'preserve' })" -ForegroundColor Gray
Write-Host "Clean install: $(if ($ForceCleanInstall) { 'yes (wipe + reinstall)' } else { 'no' })" -ForegroundColor Gray
Write-Host "Platform     : AnyCPU (the only installer)" -ForegroundColor Gray
Write-Host ""

# ── 0. Delete Vue build stamp (forces fresh Vue rebuild every release build) ──
Start-BuildStep "Prepare: drop Vue build stamp, wipe Release folders"
$vueStamp = Join-Path $ProjectRoot "KitveiHakodesh\vue-frontend\dist\.build-stamp"
if (Test-Path $vueStamp) {
    Remove-Item $vueStamp -Force
    Write-Host "Deleted Vue build stamp" -ForegroundColor Gray
}

# Delete .tsbuildinfo cache so vue-tsc --build does a clean type-check (prevents
# stale cache from replaying old errors after source fixes)
Get-ChildItem -Path (Join-Path $ProjectRoot "KitveiHakodesh\vue-frontend") -Filter "*.tsbuildinfo" -Recurse -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Force; Write-Host "Deleted $($_.Name)" -ForegroundColor Gray }

# ── 1. Wipe the VSTO Release folder (ensures clean VSTO output) ─────────────
foreach ($folder in @("bin\Release")) {
    $path = Join-Path $ProjectRoot "KleiKodeshVsto\$folder"
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
        Write-Host "Deleted KleiKodeshVsto\$folder" -ForegroundColor Gray
    }
}

# Wipe DemoApp Release folder so the rebuild is clean
if (Test-Path $DemoAppReleaseDir) {
    Remove-Item $DemoAppReleaseDir -Recurse -Force
    Write-Host "Deleted KitveiHakodeshDemoApp\bin\Release" -ForegroundColor Gray
}

# ── 2. Update version ─────────────────────────────────────────────────────────
Start-BuildStep "Stamp version"
$versionBefore = Get-CurrentVersion
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

# Re-stamp if the version did not actually land. UpdateVersion.ps1 reports success even
# when its regex fails to match, and on the auto-increment path it derives the new
# version from the GitHub tag rather than from this file -- so a hand-edited constant
# can survive the stamp untouched and ship under the wrong tag. That is what happened
# to v9.1.0: it was built and released with the constant still reading v10.0.0 (commit
# 7842bb14), so every machine that installed it wrote v10.0.0 to
# HKCU\SOFTWARE\KleiKodesh\Version and can never see a v9.x release as newer again.
#
# The tag is canon: whatever version this build is released under is the version that
# must be baked in, so force it rather than trusting the first pass.
if ($ManualVersion -and $version -ne $ManualVersion) {
    Write-Host "  Version stamp did not apply ($version); forcing $ManualVersion." -ForegroundColor Yellow
    Set-Version $ManualVersion
    $version = Get-CurrentVersion
    if ($version -ne $ManualVersion) {
        Write-Host "ERROR: cannot stamp $ManualVersion into AddinInstaller.cs (still $version)." -ForegroundColor Red
        exit 1
    }
    Write-Host "  Version: $version (corrected)" -ForegroundColor Cyan
}
elseif (-not $ManualVersion -and $version -eq $versionBefore -and
        (git ls-remote --tags origin "refs/tags/$version")) {
    # Unchanged is fine when a previous build failed after stamping: the constant already
    # holds the next version and that tag is not released yet. Only an already-released
    # tag means a stale stamp.
    # Auto-increment path: there is no independently-known expected version to compare
    # against, so the guard above cannot apply. But an UNCHANGED constant is
    # a failed stamp when its tag is already released -- and this is the
    # path that produced the v9.1.0 mis-stamp. Fail rather than tag a stale version.
    Write-Host "ERROR: version stamp did not change the constant (still $version)." -ForegroundColor Red
    Write-Host "  UpdateVersion.ps1 reported success but AddinInstaller.cs was not updated." -ForegroundColor Red
    Write-Host "  Re-run with -ManualVersion <vX.Y.Z> to set it explicitly." -ForegroundColor Red
    exit 1
}

# ── 2b. Restore packages (same as the GitHub Actions release) ─────────────────
Start-BuildStep "Restore packages (msbuild, nuget, npm)"
& (Find-MSBuild) (Join-Path $ProjectRoot "KleiKodeshProject.slnx") -t:restore -v:minimal -nologo
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: msbuild restore failed." -ForegroundColor Red; exit 1 }
# packages.config projects each use their own ..\packages folder.
Push-Location $ProjectRoot
try {
    foreach ($cfg in git ls-files '*packages.config') {
        nuget restore $cfg -PackagesDirectory (Join-Path (Split-Path $cfg) '..\packages') -NonInteractive
        if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: nuget restore failed for $cfg." -ForegroundColor Red; exit 1 }
    }
} finally { Pop-Location }
Push-Location (Join-Path $ProjectRoot "KitveiHakodesh\vue-frontend")
# npm ci deletes node_modules first, which fails half-way (leaving it gutted) while a
# local dev server holds esbuild.exe. So npm ci only in CI; locally the same lock file
# is installed in place, without rewriting package.json / package-lock.json.
$npmArgs = @(if ($env:CI) { "ci" } else { "install", "--no-save", "--no-audit", "--no-fund" })
try { & "C:\Program Files\nodejs\npm.cmd" @npmArgs } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: npm ci failed." -ForegroundColor Red; exit 1 }

# ── 3. Clean ──────────────────────────────────────────────────────────────────
if (-not $NoClean) {
    Start-BuildStep "Clean solution"
    Invoke-SolutionClean
}

# ── 4. Build the installer (AnyCPU - the ONLY installer since 2026-10-11) ────
#
# Builds the VSTO AnyCPU, packs it (pre-build target), wraps it with NSIS:
#   KleiKodeshSetup-{version}.exe
# The x64 / x86 variants are gone: the add-in is AnyCPU and runs in either Office
# bitness, and the KitveiHakodesh service is a separate x64 process.

$nsisExe = @(
    "C:\Program Files (x86)\NSIS\makensis.exe",
    "C:\Program Files\NSIS\makensis.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $nsisExe) {
    Write-Host "ERROR: NSIS not found. Install from https://nsis.sourceforge.io/" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $ReleasesDir)) { New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null }

# Helper: build the installer and wrap it with NSIS. Called once, for AnyCPU.
function Build-Variant {
    param(
        [string]$Platform,      # AnyCPU
        [string]$Suffix         # ""
    )

    # The installer build used to be one opaque step: its BuildVstoAndPack target builds
    # the VSTO (which builds Vue and AOT-publishes the service on the way) and packs the
    # payload. Each piece runs here as its own timed step instead. The nested builds then
    # find Vue (stamp) and the service (Inputs/Outputs) up to date and skip them.
    # -tl:off: dotnet's live terminal logger collapses nested output into one line.

    # SDK-style projects ignore PropertyGroup conditions — must pass OutputPath explicitly
    $outputPath = "bin\Release\net48\"
    $installerProps = @(
        "-p:Configuration=Release",
        "-p:VstoConfiguration=Release", "-p:VstoPlatform=$Platform",
        "-p:InstallerVariant=$Platform",
        "-p:DeleteFtsIndex=$(if ($DeleteFtsIndex) { 'true' } else { 'false' })",
        "-p:ForceCleanInstall=$(if ($ForceCleanInstall) { 'true' } else { 'false' })",
        "-p:OutputPath=$outputPath"
    )
    $msbuild = Find-MSBuild

    Start-BuildStep "Vue frontend (npm run build)"
    $vueDir = Join-Path $ProjectRoot "KitveiHakodesh\vue-frontend"
    Push-Location $vueDir
    try { & "C:\Program Files\nodejs\npm.cmd" run build } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: Vue build failed." -ForegroundColor Red; exit 1 }
    # Same stamp KitveiHakodesh.targets touches, so the VSTO and DemoApp builds skip Vue.
    New-Item -ItemType File -Path $vueStamp -Force | Out-Null

    Start-BuildStep "KitveiHakodeshService native AOT publish"
    $libProject = Join-Path $ProjectRoot "KitveiHakodesh\CSharpBackend\KitveiHakodeshLib\KitveiHakodeshLib.csproj"
    & $msbuild $libProject /t:PublishKitveiHakodeshService /p:Configuration=Release /nologo /verbosity:normal /clp:ShowTimestamp
    if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: service publish failed." -ForegroundColor Red; exit 1 }

    Start-BuildStep "KleiKodeshVsto build (Release|$Platform)"
    dotnet msbuild $WpfProjectPath -t:BuildVsto @installerProps -tl:off -clp:ShowTimestamp -v:normal -nologo
    if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: VSTO build failed." -ForegroundColor Red; exit 1 }

    Start-BuildStep "Pack VSTO payload (chunked LZMA, parallel)"
    dotnet msbuild $WpfProjectPath -t:PackVstoPayload @installerProps -tl:off -clp:ShowTimestamp -v:normal -nologo
    if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: payload pack failed." -ForegroundColor Red; exit 1 }

    Start-BuildStep "Compile WPF installer"
    dotnet build $WpfProjectPath @installerProps -p:SkipVstoPack=true -tl:off -clp:ShowTimestamp -v:minimal -nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: WPF build failed for $Platform." -ForegroundColor Red
        exit 1
    }

    $wpfExeDir = Join-Path (Split-Path $WpfProjectPath) $outputPath
    $wpfExePath = Join-Path $wpfExeDir "KleiKodeshVstoInstallerWpf.exe"
    $outFile = Join-Path $ReleasesDir "KleiKodeshSetup-${version}${Suffix}.exe"

    Start-BuildStep "NSIS wrapper ($version$Suffix)"
    $versionNumeric = $version.TrimStart('v')   # "v8.6.0" → "8.6.0" for VIProductVersion
    & $nsisExe `
        "/DPRODUCT_VERSION=$version" `
        "/DPRODUCT_VERSION_NUMERIC=$versionNumeric" `
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

Start-BuildStep "Add-in signing certificate"
Update-SigningCertificate

Build-Variant -Platform "AnyCPU" -Suffix ""
$installerAny = $script:LastBuiltInstaller

Write-Host ""
Write-Host "Installer built successfully." -ForegroundColor Green

# ── 4b. Build KitveiHakodeshPortable (DemoApp, Release|AnyCPU) + zip ─────────
Start-BuildStep "Build KitveiHakodeshPortable (DemoApp Release|AnyCPU)"

$msbuild = Find-MSBuild
if (-not $msbuild) {
    Write-Host "ERROR: MSBuild not found — cannot build KitveiHakodeshDemoApp." -ForegroundColor Red
    exit 1
}

& $msbuild $DemoAppProjectPath /p:Configuration=Release /p:Platform=AnyCPU /nologo /verbosity:minimal /clp:ShowTimestamp
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: KitveiHakodeshDemoApp build failed." -ForegroundColor Red
    exit 1
}

Start-BuildStep "Zip KitveiHakodeshPortable"

$portableZip = Join-Path $ReleasesDir "KitveiHakodeshPortable-${version}.zip"
if (Test-Path $portableZip) { Remove-Item $portableZip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($DemoAppReleaseDir, $portableZip)

if (-not (Test-Path $portableZip)) {
    Write-Host "ERROR: KitveiHakodeshPortable zip not created: $portableZip" -ForegroundColor Red
    exit 1
}
Write-Host "OK: $(Split-Path -Leaf $portableZip)" -ForegroundColor Green

# ── 5. GitHub release ─────────────────────────────────────────────────────────
if ($NoRelease) { Complete-BuildSteps; Write-Host "GitHub release skipped." -ForegroundColor Yellow; exit 0 }

Start-BuildStep "GitHub release"

$ghCmd = Get-Command gh -ErrorAction SilentlyContinue
if (-not $ghCmd) {
    Write-Host "GitHub CLI (gh) not found — skipping release. Install from: https://cli.github.com/" -ForegroundColor Yellow
    Complete-BuildSteps
    exit 0
}

gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "GitHub CLI not authenticated — skipping release. Run: gh auth login" -ForegroundColor Yellow
    Complete-BuildSteps
    exit 0
}

# Delete existing release/tag if present
gh release view $version --repo KleiKodesh/KleiKodeshProject 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Existing release $version found — deleting..." -ForegroundColor Yellow
    gh release delete $version --repo KleiKodesh/KleiKodeshProject --yes
}

$notes      = New-ReleaseNotes -Version $version -Source $ReleaseNotesSource
$branch     = git rev-parse --abbrev-ref HEAD
$notesFile  = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($notesFile, $notes, (New-Object System.Text.UTF8Encoding($false)))

# Upload all three installers to the same release.
# Files are uploaded one at a time to avoid Windows command-line length limits.
# --notes-file avoids shell-splitting bugs when $notes contains newlines or special chars.
gh release create $version `
    --repo KleiKodesh/KleiKodeshProject `
    --title "KleiKodesh $version" `
    --notes-file $notesFile `
    --target $branch
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: GitHub release creation failed." -ForegroundColor Red
    exit 1
}

foreach ($asset in @($installerAny, $portableZip) | Where-Object { $_ }) {
    Write-Host "Uploading $(Split-Path -Leaf $asset)..." -ForegroundColor Yellow
    gh release upload $version $asset --repo KleiKodesh/KleiKodeshProject --clobber
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Upload failed for $(Split-Path -Leaf $asset)" -ForegroundColor Red
        exit 1
    }
}

Remove-Item $notesFile -ErrorAction SilentlyContinue

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: GitHub release creation failed." -ForegroundColor Red
    exit 1
}

Complete-BuildSteps
Write-Host ""
Write-Host "SUCCESS: https://github.com/KleiKodesh/KleiKodeshProject/releases/tag/$version" -ForegroundColor Green

# gh release create does not fire notify-website.yml / release-issue-check.yml / drive-upload.yml
# (not with the Actions token), so start them here - for local and CI releases alike.
# The website repo needs its own token: WEBSITE_DISPATCH_TOKEN when set (CI), else gh's login.
$ghToken = $env:GH_TOKEN
if ($env:WEBSITE_DISPATCH_TOKEN) { $env:GH_TOKEN = $env:WEBSITE_DISPATCH_TOKEN }
gh api repos/KleiKodesh/kleikodesh.github.io/dispatches -f event_type=release-changed
if ($LASTEXITCODE -ne 0) { Write-Host "WARNING: website update dispatch failed." -ForegroundColor Yellow }
$env:GH_TOKEN = $ghToken
gh workflow run release-issue-check.yml --repo KleiKodesh/KleiKodeshProject -f tag=$version
if ($LASTEXITCODE -ne 0) { Write-Host "WARNING: release-issue-check dispatch failed." -ForegroundColor Yellow }
gh workflow run drive-upload.yml --repo KleiKodesh/KleiKodeshProject
if ($LASTEXITCODE -ne 0) { Write-Host "WARNING: Drive sync dispatch failed." -ForegroundColor Yellow }
