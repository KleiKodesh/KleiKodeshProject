param(
    [switch]$NoWait,
    [switch]$NoRelease,
    [switch]$NoClean,
    [ValidateSet("major", "minor", "patch")]
    [string]$VersionIncrement,
    [ValidateSet("commits", "file", "both")]
    [string]$ReleaseNotesSource
)

Write-Host "Building KleiKodesh Installer..." -ForegroundColor Green

# Check if version increment type was provided as parameter
if ($VersionIncrement) {
    Write-Host "Using command line parameter for version increment: $VersionIncrement" -ForegroundColor Cyan
} else {
    # Show version increment selection menu
    Write-Host ""
    Write-Host "Select version increment type (Semantic Versioning):" -ForegroundColor Yellow
    Write-Host "1. Patch (Bug fixes) - e.g., 1.2.3 -> 1.2.4" -ForegroundColor White
    Write-Host "2. Minor (New features) - e.g., 1.2.3 -> 1.3.0" -ForegroundColor White
    Write-Host "3. Major (Breaking changes) - e.g., 1.2.3 -> 2.0.0" -ForegroundColor White
    Write-Host ""

    do {
        $versionChoice = Read-Host "Enter your choice (1-3)"
        switch ($versionChoice) {
            "1" { 
                $VersionIncrement = "patch"
                $valid = $true
            }
            "2" { 
                $VersionIncrement = "minor"
                $valid = $true
            }
            "3" { 
                $VersionIncrement = "major"
                $valid = $true
            }
            default { 
                Write-Host "Invalid choice. Please enter 1, 2, or 3." -ForegroundColor Red
                $valid = $false
            }
        }
    } while (-not $valid)
}

Write-Host "Version increment type: $VersionIncrement" -ForegroundColor Cyan
Write-Host ""

# Check if release notes source was provided as parameter
if ($ReleaseNotesSource) {
    Write-Host "Using command line parameter for release notes: $ReleaseNotesSource" -ForegroundColor Cyan
} else {
    # Show release notes source selection menu
    Write-Host "Select release notes source:" -ForegroundColor Yellow
    Write-Host "1. Git commits only" -ForegroundColor White
    Write-Host "2. RELEASE_NOTES.txt file only" -ForegroundColor White
    Write-Host "3. Both (file + commits)" -ForegroundColor White
    Write-Host ""

    do {
        $notesChoice = Read-Host "Enter your choice (1-3)"
        switch ($notesChoice) {
            "1" { 
                $ReleaseNotesSource = "commits"
                $valid = $true
            }
            "2" { 
                $ReleaseNotesSource = "file"
                $valid = $true
            }
            "3" { 
                $ReleaseNotesSource = "both"
                $valid = $true
            }
            default { 
                Write-Host "Invalid choice. Please enter 1, 2, or 3." -ForegroundColor Red
                $valid = $false
            }
        }
    } while (-not $valid)
}

Write-Host "Release notes source: $ReleaseNotesSource" -ForegroundColor Cyan
Write-Host ""

# Platform is always AnyCPU
$Configuration = "Release"
$Platform = "AnyCPU"
Write-Host "Platform: Release|AnyCPU" -ForegroundColor Cyan

# Increment version first, before building
Write-Host "Incrementing version..." -ForegroundColor Yellow

# Get absolute paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$progressWindowPath = Join-Path $projectRoot "KleiKodeshVstoInstallerWpf\InstallProgressWindow.xaml.cs"
$updateVersionScript = Join-Path $projectRoot "KleiKodeshVstoInstallerWpf\UpdateVersion.ps1"

Write-Host "Project root: $projectRoot" -ForegroundColor Gray
Write-Host "Progress window path: $progressWindowPath" -ForegroundColor Gray

# Delete VSTO Release folder first to ensure clean build
Write-Host "Deleting VSTO Release folder..." -ForegroundColor Yellow
$vstoReleasePath = Join-Path $projectRoot "KleiKodeshVsto\bin\Release"
if (Test-Path $vstoReleasePath) {
    Remove-Item -Path $vstoReleasePath -Recurse -Force
    Write-Host "VSTO Release folder deleted: $vstoReleasePath" -ForegroundColor Cyan
} else {
    Write-Host "VSTO Release folder not found (already clean)" -ForegroundColor Gray
}

# Run UpdateVersion.ps1 to increment the version
& powershell -ExecutionPolicy Bypass -File $updateVersionScript -FilePath $progressWindowPath -IncrementType $VersionIncrement

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

# Clean the solution first (unless skipped)
if (-not $NoClean) {
    Write-Host "Cleaning solution..." -ForegroundColor Yellow
    $solutionPath = Join-Path $projectRoot "KleiKodeshProject.slnx"
    $wpfProjectPath = Join-Path $projectRoot "KleiKodeshVstoInstallerWpf\KleiKodeshVstoInstallerWpf.csproj"

    # Try to find MSBuild - first check PATH, then check common install locations
    $msbuildPath = $null
    
    # Check if MSBuild is in PATH
    $msbuildInPath = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($msbuildInPath) {
        $msbuildPath = $msbuildInPath.Source
        Write-Host "Found MSBuild in PATH: $msbuildPath" -ForegroundColor Cyan
    } else {
        # Check common Visual Studio install locations
        $commonPaths = @(
            "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
            "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
            "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
            "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
            "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
            "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
        )
        
        foreach ($path in $commonPaths) {
            if (Test-Path $path) {
                $msbuildPath = $path
                Write-Host "Found MSBuild at: $msbuildPath" -ForegroundColor Cyan
                break
            }
        }
    }

    if ($msbuildPath) {
        # MSBuild found - clean entire solution including VSTO
        Write-Host "Clean command (MSBuild): $msbuildPath" -ForegroundColor Gray
        & $msbuildPath $solutionPath /t:Clean /p:Configuration=Release /p:Platform="Any CPU" /verbosity:minimal
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "WARNING: Clean operation had issues, continuing with build..." -ForegroundColor Yellow
        }
    } else {
        # MSBuild not found - only clean WPF project with dotnet (VSTO requires MSBuild)
        Write-Host "MSBuild not found in PATH or common locations, cleaning WPF project only (VSTO requires Visual Studio MSBuild)..." -ForegroundColor Yellow
        Write-Host "Clean command (dotnet): dotnet clean" -ForegroundColor Gray
        dotnet clean $wpfProjectPath -c Release --verbosity minimal
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "WARNING: Clean operation had issues, continuing with build..." -ForegroundColor Yellow
        } else {
            Write-Host "WPF project cleaned. VSTO project will be cleaned by prebuild event if needed." -ForegroundColor Cyan
        }
    }
} else {
    Write-Host "Skipping clean step (-NoClean specified)" -ForegroundColor Yellow
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
Write-Host "Build command: dotnet build" -ForegroundColor Gray
Write-Host "Starting WPF build..." -ForegroundColor Yellow

dotnet build $wpfProjectPath -c Release -p:VstoConfiguration=$Configuration -p:VstoPlatform=$Platform --verbosity normal

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

# Ensure releases output folder exists (gitignored)
$releasesDir = Join-Path $scriptDir "releases"
if (!(Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
}

# Build NSIS wrapper with version parameter, output to releases subfolder
Write-Host "Building NSIS wrapper with version $version..." -ForegroundColor Yellow
$nsisScriptPath = Join-Path $scriptDir "KleiKodeshWrapper.nsi"
& $nsisPath "/DPRODUCT_VERSION=$version" "/DOUTPUT_DIR=$releasesDir" $nsisScriptPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: NSIS build failed" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

$installerPath = Join-Path $releasesDir "KleiKodeshSetup-$version.exe"
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
            
            # Build release notes based on user selection
            $platformInfo = if ($Platform -eq "x64") { "x64" } else { "AnyCPU" }
            $releaseNotes = ""
            
            # Check for RELEASE_NOTES.txt file
            $releaseNotesFile = Join-Path $projectRoot "RELEASE_NOTES.txt"
            $fileContent = ""
            if (Test-Path $releaseNotesFile) {
                $fileContent = Get-Content $releaseNotesFile -Raw
                Write-Host "Found RELEASE_NOTES.txt file" -ForegroundColor Cyan
            }
            
            # Get git commit history
            Write-Host "Gathering git commit history..." -ForegroundColor Cyan
            $previousTag = gh release list --limit 1 --json tagName --jq '.[0].tagName' 2>$null
            
            $commitHistory = ""
            if ($previousTag -and $LASTEXITCODE -eq 0) {
                Write-Host "Previous release: $previousTag" -ForegroundColor Gray
                $commits = git log "$previousTag..HEAD" --pretty=format:"- %s (%h)" 2>$null
                if ($commits) {
                    $commitHistory = "**Commits since ${previousTag}:**`n$commits"
                    Write-Host "Found $($commits.Count) commits since $previousTag" -ForegroundColor Cyan
                } else {
                    Write-Host "WARNING: No commits found since $previousTag" -ForegroundColor Yellow
                }
            } else {
                Write-Host "No previous release found, including recent commits" -ForegroundColor Gray
                $commits = git log -10 --pretty=format:"- %s (%h)" 2>$null
                if ($commits) {
                    $commitHistory = "**Recent Commits:**`n$commits"
                    Write-Host "Found recent commits" -ForegroundColor Cyan
                } else {
                    Write-Host "WARNING: No git commits found" -ForegroundColor Yellow
                }
            }
            
            # Build release notes based on source selection
            switch ($ReleaseNotesSource) {
                "commits" {
                    Write-Host "Using git commits for release notes" -ForegroundColor Cyan
                    $releaseNotes = "Release $version`n`n"
                    $releaseNotes += "**Build Configuration:**`n"
                    $releaseNotes += "- VSTO Project: $Configuration|$Platform (MSBuild)`n"
                    $releaseNotes += "- WPF Installer: Release|$platformInfo (dotnet build)`n`n"
                    if ($commitHistory) {
                        $releaseNotes += $commitHistory
                    }
                }
                "file" {
                    Write-Host "Using RELEASE_NOTES.txt for release notes" -ForegroundColor Cyan
                    if ($fileContent) {
                        $releaseNotes = "Release $version`n`n"
                        $releaseNotes += $fileContent
                        $releaseNotes += "`n`n**Build Configuration:**`n"
                        $releaseNotes += "- VSTO Project: $Configuration|$Platform (MSBuild)`n"
                        $releaseNotes += "- WPF Installer: Release|$platformInfo (dotnet build)"
                        Write-Host "Using content from RELEASE_NOTES.txt" -ForegroundColor Cyan
                    } else {
                        Write-Host "WARNING: RELEASE_NOTES.txt not found, using default notes" -ForegroundColor Yellow
                        $releaseNotes = "Release $version`n`n"
                        $releaseNotes += "**Build Configuration:**`n"
                        $releaseNotes += "- VSTO Project: $Configuration|$Platform (MSBuild)`n"
                        $releaseNotes += "- WPF Installer: Release|$platformInfo (dotnet build)"
                    }
                }
                "both" {
                    Write-Host "Using both RELEASE_NOTES.txt and git commits" -ForegroundColor Cyan
                    if ($fileContent) {
                        $releaseNotes = $fileContent
                        $releaseNotes += "`n`n---`n`n"
                    } else {
                        $releaseNotes = "Release $version`n`n"
                    }
                    $releaseNotes += "**Build Configuration:**`n"
                    $releaseNotes += "- VSTO Project: $Configuration|$Platform (MSBuild)`n"
                    $releaseNotes += "- WPF Installer: Release|$platformInfo (dotnet build)`n`n"
                    if ($commitHistory) {
                        $releaseNotes += $commitHistory
                    }
                }
            }
            
            $currentBranch = git rev-parse --abbrev-ref HEAD
            gh release create $version $installerPath `
                --title "KleiKodesh $version" `
                --notes $releaseNotes `
                --target $currentBranch
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "SUCCESS: GitHub release $version created!" -ForegroundColor Green
                $repoUrl = gh repo view --json url --jq .url
                Write-Host "Release URL: $repoUrl/releases/tag/$version" -ForegroundColor Cyan
                
                # Verify the release was created and get the actual version tag
                Write-Host "Verifying release on GitHub..." -ForegroundColor Yellow
                $releaseInfo = gh release view $version --json tagName,assets 2>$null
                
                if ($LASTEXITCODE -eq 0 -and $releaseInfo) {
                    $releaseData = $releaseInfo | ConvertFrom-Json
                    $actualVersion = $releaseData.tagName
                    
                    # Find the setup file in the release assets
                    $setupAsset = $releaseData.assets | Where-Object { $_.name -like "KleiKodeshSetup-*.exe" } | Select-Object -First 1
                    
                    if ($setupAsset) {
                        Write-Host "Found setup file in release: $($setupAsset.name)" -ForegroundColor Cyan
                        
                        # Generate direct download link using the actual version from GitHub
                        $downloadUrl = "$repoUrl/releases/download/$actualVersion/$($setupAsset.name)"
                        Write-Host "Direct download URL: $downloadUrl" -ForegroundColor Cyan
                        Write-Host ""
                        Write-Host "NOTE: Website will automatically fetch the latest release via GitHub API" -ForegroundColor Cyan
                    } else {
                        Write-Host "WARNING: Setup file not found in GitHub release assets" -ForegroundColor Yellow
                    }
                } else {
                    Write-Host "WARNING: Could not verify release on GitHub, skipping website update" -ForegroundColor Yellow
                }
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