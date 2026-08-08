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
    [switch]$ForceCleanInstall,         # wipe + reinstall on launch (התקן behaves like תיקון)
    [switch]$AnyCpuOnly                 # build only the AnyCPU variant (skip x64 and x86)
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
Write-Host "Platforms    : $(if ($AnyCpuOnly) { 'AnyCPU only' } else { 'x64, x86, AnyCPU' })" -ForegroundColor Gray
Write-Host ""

# ── 0. Delete Vue build stamp (forces fresh Vue rebuild every release build) ──
$vueStamp = Join-Path $ProjectRoot "KitveiHakodesh\vue-frontend\dist\.build-stamp"
if (Test-Path $vueStamp) {
    Remove-Item $vueStamp -Force
    Write-Host "Deleted Vue build stamp" -ForegroundColor Gray
}

# Delete .tsbuildinfo cache so vue-tsc --build does a clean type-check (prevents
# stale cache from replaying old errors after source fixes)
Get-ChildItem -Path (Join-Path $ProjectRoot "KitveiHakodesh\vue-frontend") -Filter "*.tsbuildinfo" -Recurse -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Force; Write-Host "Deleted $($_.Name)" -ForegroundColor Gray }

# ── 1. Wipe VSTO Release folders (ensures clean VSTO output for all variants) ─
foreach ($folder in @("bin\Release", "bin\Release-x64", "bin\Release-x86")) {
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
        -p:DeleteFtsIndex=$(if ($DeleteFtsIndex) { 'true' } else { 'false' }) `
        -p:ForceCleanInstall=$(if ($ForceCleanInstall) { 'true' } else { 'false' }) `
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

if (-not $AnyCpuOnly) {
    Build-Variant -Platform "x64"    -Suffix "-x64"
    $installerX64 = $script:LastBuiltInstaller

    Build-Variant -Platform "x86"    -Suffix "-x86"
    $installerX86 = $script:LastBuiltInstaller
} else {
    $installerX64 = $null
    $installerX86 = $null
}
Build-Variant -Platform "AnyCPU" -Suffix ""
$installerAny = $script:LastBuiltInstaller

Write-Host ""
Write-Host "All variants built successfully." -ForegroundColor Green

# ── 4b. Build KitveiHakodeshPortable (DemoApp, Release|AnyCPU) + zip ─────────
Write-Host ""
Write-Host "Building KitveiHakodeshPortable (Release|AnyCPU)..." -ForegroundColor Yellow

$msbuild = Find-MSBuild
if (-not $msbuild) {
    Write-Host "ERROR: MSBuild not found — cannot build KitveiHakodeshDemoApp." -ForegroundColor Red
    exit 1
}

& $msbuild $DemoAppProjectPath /p:Configuration=Release /p:Platform=AnyCPU /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: KitveiHakodeshDemoApp build failed." -ForegroundColor Red
    exit 1
}

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

foreach ($asset in @($installerX64, $installerX86, $installerAny, $portableZip) | Where-Object { $_ }) {
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

# ── 6. Point the website at this release's AnyCPU installer ──────────────────
#
# The website's download button holds a full, versioned URL to the AnyCPU installer.
# It cannot be a fixed "/releases/latest/download/..." link: GitHub resolves "latest"
# server-side but matches the *filename* literally, so any URL naming a versioned
# asset dies the moment the next version ships. Instead the URL is rewritten here,
# once per release, and committed to the website repo.
#
# Order matters. The asset is verified live BEFORE the site is touched, so the button
# is never pointed at a URL that 404s. If anything below fails the release itself is
# already published and intact — only the website lags, and re-running this section
# (or editing the href by hand) is enough to catch it up.
Write-Host ""
Write-Host "Updating website download link..." -ForegroundColor Yellow

$downloadUrl = "https://github.com/KleiKodesh/KleiKodeshProject/releases/download/$version/KleiKodeshSetup-$version.exe"

# 6a. Confirm the asset really is downloadable before linking to it.
$linkOk = $false
try {
    # Follow the 302 to the CDN but read headers only — never pull the installer body.
    $resp = Invoke-WebRequest -Uri $downloadUrl -Method Head -UseBasicParsing -TimeoutSec 30
    $linkOk = ($resp.StatusCode -eq 200)
} catch {
    Write-Host "  $($_.Exception.Message)" -ForegroundColor DarkGray
}

if (-not $linkOk) {
    Write-Host "ERROR: Release asset is not downloadable — website NOT updated." -ForegroundColor Red
    Write-Host "  $downloadUrl" -ForegroundColor Red
    Write-Host "  Assets currently in the release:" -ForegroundColor Yellow
    gh release view $version --repo KleiKodesh/KleiKodeshProject --json assets --jq '.assets[].name' |
        ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    exit 1
}
Write-Host "  asset verified: $downloadUrl -> 200" -ForegroundColor Gray

# 6b. Rewrite the href in index.html.
if (-not (Test-Path $WebsiteRepo)) {
    Write-Host "WARNING: Website repo not found at $WebsiteRepo — skipping link update." -ForegroundColor Yellow
    Write-Host "         Set the download button href manually to:" -ForegroundColor Yellow
    Write-Host "         $downloadUrl" -ForegroundColor Yellow
} else {
    $indexPath = Join-Path $WebsiteRepo "index.html"
    $html = [System.IO.File]::ReadAllText($indexPath)

    # Anchor on the data-download-button attribute rather than the URL itself, so this
    # keeps working no matter what the previous release left in the href.
    $pattern     = '(?s)(href=")[^"]*("[^>]*?data-download-button="main")'
    $replacement = '${1}' + $downloadUrl + '${2}'
    $updated     = [regex]::Replace($html, $pattern, $replacement)

    if ($updated -eq $html -and $html -notmatch [regex]::Escape($downloadUrl)) {
        Write-Host "ERROR: Could not find the download button in index.html." -ForegroundColor Red
        Write-Host "       Expected an <a href=\"...\" data-download-button=\"main\">." -ForegroundColor Yellow
        Write-Host "       Set the href manually to: $downloadUrl" -ForegroundColor Yellow
        exit 1
    }

    if ($updated -ne $html) {
        # UTF8 without BOM — the file is UTF-8 and a BOM would show up as stray
        # characters at the top of the rendered page.
        [System.IO.File]::WriteAllText($indexPath, $updated, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  index.html updated" -ForegroundColor Gray
    } else {
        Write-Host "  index.html already current" -ForegroundColor Gray
    }

    # 6c. Commit and push, but only if something actually changed.
    Push-Location $WebsiteRepo
    try {
        $dirty = git status --porcelain -- index.html
        if (-not $dirty) {
            Write-Host "OK: website already up to date (nothing to push)." -ForegroundColor Green
        } else {
            git add index.html
            git commit -m "download button: point at $version" | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Host "ERROR: Website commit failed — link updated locally but not pushed." -ForegroundColor Red
                exit 1
            }

            git push origin HEAD
            if ($LASTEXITCODE -ne 0) {
                Write-Host "ERROR: Website push failed. The commit is local; push it manually:" -ForegroundColor Red
                Write-Host "       cd $WebsiteRepo; git push origin HEAD" -ForegroundColor Yellow
                exit 1
            }
            Write-Host "OK: website updated -> $downloadUrl" -ForegroundColor Green
        }
    } finally {
        Pop-Location
    }
}

Write-Host ""
Write-Host "SUCCESS: https://github.com/KleiKodesh/KleiKodeshProject/releases/tag/$version" -ForegroundColor Green
