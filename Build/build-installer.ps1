param(
    [switch]$NoWait,
    [switch]$NoRelease,
    [ValidateSet("AnyCPU", "x64")]
    [string]$Platform
)

Write-Host "Building KleiKodesh Installer..." -ForegroundColor Green

# Check if platform was provided as parameter
if ($Platform) {
    $Configuration = "Release"
    Write-Host "Using command line parameter: Release|$Platform" -ForegroundColor Cyan
} else {
    # Show platform selection menu
    Write-Host ""
    Write-Host "Select VSTO build configuration:" -ForegroundColor Yellow
    Write-Host "1. Release|AnyCPU (Recommended)" -ForegroundColor White
    Write-Host "2. Release|x64" -ForegroundColor White
    Write-Host ""

    do {
        $choice = Read-Host "Enter your choice (1-2)"
        switch ($choice) {
            "1" { 
                $Configuration = "Release"
                $Platform = "AnyCPU"
                $valid = $true
            }
            "2" { 
                $Configuration = "Release"
                $Platform = "x64"
                $valid = $true
            }
            default { 
                Write-Host "Invalid choice. Please enter 1 or 2." -ForegroundColor Red
                $valid = $false
            }
        }
    } while (-not $valid)
}

Write-Host "Selected: $Configuration|$Platform" -ForegroundColor Cyan

# Increment version first, before building
Write-Host "Incrementing version..." -ForegroundColor Yellow

# Get absolute paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$progressWindowPath = Join-Path $projectRoot "KleiKodeshVstoInstallerWpf\InstallProgressWindow.xaml.cs"
$updateVersionScript = Join-Path $projectRoot "KleiKodeshVstoInstallerWpf\UpdateVersion.ps1"

Write-Host "Project root: $projectRoot" -ForegroundColor Gray
Write-Host "Progress window path: $progressWindowPath" -ForegroundColor Gray

# Run UpdateVersion.ps1 to increment the version
& powershell -ExecutionPolicy Bypass -File $updateVersionScript -FilePath $progressWindowPath

# Now get the updated version from the file
$versionMatch = Select-String -Path $progressWindowPath -Pattern 'const string Version = "([^"]+)"'
if ($versionMatch) {
    $version = $versionMatch.Matches[0].Groups[1].Value
    Write-Host "Using version: $version" -ForegroundColor Cyan
} else {
    Write-Host "ERROR: Could not detect version from InstallProgressWindow.xaml.cs" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

# Build WPF installer with VSTO configuration parameters
# The WPF installer prebuild event will automatically build the VSTO project
Write-Host "Building WPF installer in Release mode..." -ForegroundColor Yellow
Write-Host "Note: VSTO project will be built automatically by WPF installer prebuild event" -ForegroundColor Cyan

# Determine dotnet build architecture parameter
$dotnetArch = ""
if ($Platform -eq "x64") {
    $dotnetArch = "--arch x64"
    Write-Host "Using dotnet build with x64 architecture" -ForegroundColor Cyan
} else {
    Write-Host "Using dotnet build with default architecture (AnyCPU)" -ForegroundColor Cyan
}

# Use dotnet build for modern .NET project
$wpfProjectPath = Join-Path $projectRoot "KleiKodeshVstoInstallerWpf\KleiKodeshVstoInstallerWpf.csproj"
$buildCommand = "dotnet build `"$wpfProjectPath`" -c Release $dotnetArch -p:VstoConfiguration=$Configuration -p:VstoPlatform=$Platform --verbosity normal"
Write-Host "Build command: $buildCommand" -ForegroundColor Gray
Write-Host "Starting WPF build..." -ForegroundColor Yellow
Invoke-Expression $buildCommand

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to build WPF installer (VSTO build is handled by prebuild event)" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

# Check for NSIS
$nsisPath = "C:\Program Files (x86)\NSIS\makensis.exe"
if (-not (Test-Path $nsisPath)) {
    $nsisPath = "C:\Program Files\NSIS\makensis.exe"
    if (-not (Test-Path $nsisPath)) {
        Write-Host "ERROR: NSIS not found" -ForegroundColor Red
        Write-Host "Install from: https://nsis.sourceforge.io/" -ForegroundColor Yellow
        if (-not $NoWait) { Read-Host "Press Enter to continue" }
        exit 1
    }
}

# Build NSIS wrapper with version parameter
Write-Host "Building NSIS wrapper with version $version..." -ForegroundColor Yellow
$nsisScriptPath = Join-Path $scriptDir "KleiKodeshWrapper.nsi"
& $nsisPath "/DPRODUCT_VERSION=$version" $nsisScriptPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: NSIS build failed" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

$installerPath = Join-Path $scriptDir "KleiKodeshSetup-$version.exe"
if (-not (Test-Path $installerPath)) {
    Write-Host "ERROR: Installer not created at $installerPath" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

Write-Host ""
Write-Host "SUCCESS: KleiKodeshSetup-$version.exe created!" -ForegroundColor Green
Write-Host "This wrapper checks .NET and runs your WPF installer." -ForegroundColor Cyan
Write-Host "VSTO Configuration: $Configuration|$Platform" -ForegroundColor Cyan
Write-Host "Using version: $version" -ForegroundColor Cyan

# Create GitHub release if authenticated with gh CLI and not skipped
if (-not $NoRelease) {
    Write-Host ""
    Write-Host "Creating GitHub release..." -ForegroundColor Yellow

    try {
        # Check if gh CLI is available and authenticated
        $ghAuth = gh auth status 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "GitHub CLI not authenticated. Skipping release creation." -ForegroundColor Yellow
            Write-Host "To enable releases, run: gh auth login" -ForegroundColor Cyan
        } else {
            Write-Host "Using GitHub CLI authentication..." -ForegroundColor Cyan
            
            # Delete existing release if it exists
            $existingRelease = gh release view $version 2>$null
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Release $version already exists. Deleting..." -ForegroundColor Yellow
                gh release delete $version --yes
            }
            
            # Create new release with installer
            Write-Host "Creating release $version..." -ForegroundColor Yellow
            
            # Get git commit history since last release
            Write-Host "Gathering git commit history..." -ForegroundColor Cyan
            
            # Get the previous release tag
            $previousTag = gh release list --limit 1 --json tagName --jq '.[0].tagName' 2>$null
            
            $commitHistory = ""
            if ($previousTag -and $LASTEXITCODE -eq 0) {
                Write-Host "Previous release: $previousTag" -ForegroundColor Gray
                # Get commits since the previous tag
                $commits = git log "$previousTag..HEAD" --pretty=format:"- %s (%h)" 2>$null
                if ($commits) {
                    $commitHistory = "`n`n**Commits since ${previousTag}:**`n$commits"
                }
            } else {
                Write-Host "No previous release found, including recent commits" -ForegroundColor Gray
                # Get last 10 commits if no previous release
                $commits = git log -10 --pretty=format:"- %s (%h)" 2>$null
                if ($commits) {
                    $commitHistory = "`n`n**Recent Commits:**`n$commits"
                }
            }
            
            # Create release notes with compilation details and commit history
            $platformInfo = if ($Platform -eq "x64") { "x64" } else { "AnyCPU" }
            $releaseNotes = "Automated release for KleiKodesh $version`n`n"
            $releaseNotes += "**Build Configuration:**`n"
            $releaseNotes += "- VSTO Project: $Configuration|$Platform (MSBuild)`n"
            $releaseNotes += "- WPF Installer: Release|$platformInfo (dotnet build)"
            $releaseNotes += $commitHistory
            
            gh release create $version $installerPath `
                --title "KleiKodesh $version" `
                --notes $releaseNotes
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "SUCCESS: GitHub release $version created!" -ForegroundColor Green
                $repoUrl = gh repo view --json url --jq .url
                Write-Host "Release URL: $repoUrl/releases/tag/$version" -ForegroundColor Cyan
            } else {
                Write-Host "ERROR: Failed to create GitHub release" -ForegroundColor Red
            }
        }
    } catch {
        Write-Host "ERROR: Failed to create GitHub release: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Build completed successfully, but release creation failed." -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "Skipping GitHub release creation (-NoRelease specified)" -ForegroundColor Yellow
}

if (-not $NoWait) {
    Read-Host "Press Enter to continue"
}