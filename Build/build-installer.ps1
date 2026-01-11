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
$progressWindowPath = "..\KleiKodeshVstoInstallerWpf\InstallProgressWindow.xaml.cs"

# Run UpdateVersion.ps1 to increment the version
powershell -ExecutionPolicy Bypass -File "..\KleiKodeshVstoInstallerWpf\UpdateVersion.ps1" -FilePath $progressWindowPath

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

# Build VSTO project first
Write-Host "Building VSTO project in $Configuration|$Platform mode..." -ForegroundColor Yellow

# Find MSBuild
$msbuildPaths = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
)

$msbuildPath = $null
foreach ($path in $msbuildPaths) {
    if (Test-Path $path) {
        $msbuildPath = $path
        break
    }
}

if (-not $msbuildPath) {
    Write-Host "ERROR: MSBuild not found. Install Visual Studio with VSTO tools." -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

Write-Host "Using MSBuild: $msbuildPath" -ForegroundColor Cyan
$vstoBuildResult = & $msbuildPath "..\KleiKodeshVsto\KleiKodeshVsto.csproj" -p:Configuration=$Configuration -p:Platform=$Platform

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to build VSTO project" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

# Build WPF installer with VSTO configuration parameters
Write-Host "Building WPF installer in Release mode..." -ForegroundColor Yellow
$buildResult = dotnet build "..\KleiKodeshVstoInstallerWpf\KleiKodeshVstoInstallerWpf.csproj" -c Release -p:VstoConfiguration=$Configuration -p:VstoPlatform=$Platform

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to build WPF installer" -ForegroundColor Red
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
& $nsisPath "/DPRODUCT_VERSION=$version" "KleiKodeshWrapper.nsi"

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: NSIS build failed" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

$installerPath = "KleiKodeshSetup-$version.exe"
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
            gh release create $version $installerPath `
                --title "KleiKodesh $version" `
                --notes "Automated release for KleiKodesh $version`n`nChanges:`n- Updated installer with latest features`n- Bug fixes and improvements"
            
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