param(
    [switch]$NoWait,
    [switch]$NoVersionIncrement,
    [ValidateSet("AnyCPU", "x64")]
    [string]$Platform = "AnyCPU"
)

Write-Host "Building KleiKodesh ClickOnce Deployment..." -ForegroundColor Green

# Get absolute paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Split-Path -Parent $scriptDir
$projectRoot = Split-Path -Parent $buildDir
$vstoProjectPath = Join-Path $projectRoot "KleiKodeshVsto\KleiKodeshVsto.csproj"
$clickOnceOutputDir = Join-Path $scriptDir "ClickOnceOutput"
$tempExtractionDir = Join-Path $scriptDir "TempExtraction"
$progressWindowPath = Join-Path $projectRoot "KleiKodeshVstoInstallerWpf\InstallProgressWindow.xaml.cs"
$updateVersionScript = Join-Path $projectRoot "KleiKodeshVstoInstallerWpf\UpdateVersion.ps1"

Write-Host "Project root: $projectRoot" -ForegroundColor Gray
Write-Host "VSTO project: $vstoProjectPath" -ForegroundColor Gray
Write-Host "ClickOnce output: $clickOnceOutputDir" -ForegroundColor Gray

# Clean previous builds
if (Test-Path $clickOnceOutputDir) {
    Write-Host "Cleaning previous ClickOnce build..." -ForegroundColor Yellow
    Remove-Item $clickOnceOutputDir -Recurse -Force
}

if (Test-Path $tempExtractionDir) {
    Remove-Item $tempExtractionDir -Recurse -Force
}

# Create output directories
New-Item -ItemType Directory -Path $clickOnceOutputDir -Force | Out-Null
New-Item -ItemType Directory -Path $tempExtractionDir -Force | Out-Null

# Increment version first, before building (unless skipped)
if (-not $NoVersionIncrement) {
    Write-Host "Incrementing version..." -ForegroundColor Yellow

    Write-Host "Progress window path: $progressWindowPath" -ForegroundColor Gray

    # Run UpdateVersion.ps1 to increment the version (same as main build system)
    & powershell -ExecutionPolicy Bypass -File $updateVersionScript -FilePath $progressWindowPath
} else {
    Write-Host "Skipping version increment (-NoVersionIncrement specified)" -ForegroundColor Yellow
}

# Now get the current version from the file
$versionMatch = Select-String -Path $progressWindowPath -Pattern 'const string Version = "([^"]+)"'
if ($versionMatch) {
    $version = $versionMatch.Matches[0].Groups[1].Value
    Write-Host "Using version: $version" -ForegroundColor Cyan
} else {
    Write-Host "ERROR: Could not detect version from InstallProgressWindow.xaml.cs" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

# Increment VSTO project version based on WPF installer version + auto-increment build number
Write-Host "Incrementing VSTO project version based on WPF installer version..." -ForegroundColor Yellow

# Get current VSTO ApplicationVersion from project file
$vstoProjectContent = Get-Content $vstoProjectPath -Raw
$vstoVersionMatch = [regex]::Match($vstoProjectContent, '<ApplicationVersion>([^<]+)</ApplicationVersion>')

# Parse WPF installer version (e.g., "v1.0.88" -> "1.0.88")
$wpfVersionBase = $version.TrimStart('v')
Write-Host "WPF installer version base: $wpfVersionBase" -ForegroundColor Gray

if ($vstoVersionMatch.Success) {
    $currentVstoVersion = $vstoVersionMatch.Groups[1].Value
    Write-Host "Current VSTO ApplicationVersion: $currentVstoVersion" -ForegroundColor Gray
    
    # Check if current VSTO version matches WPF base version
    if ($currentVstoVersion -match "^$([regex]::Escape($wpfVersionBase))\.(\d+)$") {
        # Same WPF version, increment build number
        $buildNumber = [int]$matches[1] + 1
        $newVstoVersion = "$wpfVersionBase.$buildNumber"
        Write-Host "Incrementing build number: $currentVstoVersion -> $newVstoVersion" -ForegroundColor Cyan
    } else {
        # Different WPF version or format, start with build number 1
        $newVstoVersion = "$wpfVersionBase.1"
        Write-Host "New WPF version base, starting with build 1: $currentVstoVersion -> $newVstoVersion" -ForegroundColor Cyan
    }
    
    # Update ApplicationVersion in project file
    $updatedContent = $vstoProjectContent -replace '<ApplicationVersion>[^<]+</ApplicationVersion>', "<ApplicationVersion>$newVstoVersion</ApplicationVersion>"
    Set-Content -Path $vstoProjectPath -Value $updatedContent -Encoding UTF8
    
    # Also update AssemblyInfo.cs versions to match
    $assemblyInfoPath = Join-Path $projectRoot "KleiKodeshVsto\Properties\AssemblyInfo.cs"
    if (Test-Path $assemblyInfoPath) {
        $assemblyInfoContent = Get-Content $assemblyInfoPath -Raw
        $updatedAssemblyInfo = $assemblyInfoContent -replace '\[assembly: AssemblyVersion\("[^"]+"\)\]', "[assembly: AssemblyVersion(`"$newVstoVersion`")]"
        $updatedAssemblyInfo = $updatedAssemblyInfo -replace '\[assembly: AssemblyFileVersion\("[^"]+"\)\]', "[assembly: AssemblyFileVersion(`"$newVstoVersion`")]"
        Set-Content -Path $assemblyInfoPath -Value $updatedAssemblyInfo -Encoding UTF8
        Write-Host "Updated AssemblyInfo.cs versions to: $newVstoVersion" -ForegroundColor Gray
    }
    
    $vstoVersion = $newVstoVersion
} else {
    Write-Host "ERROR: Could not find ApplicationVersion in VSTO project file" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

# Now get the current version from the file (for WPF installer compatibility)
$versionMatch = Select-String -Path $progressWindowPath -Pattern 'const string Version = "([^"]+)"'
if ($versionMatch) {
    $version = $versionMatch.Matches[0].Groups[1].Value
    Write-Host "Using WPF installer version: $version" -ForegroundColor Cyan
    Write-Host "Using VSTO project version: $vstoVersion" -ForegroundColor Cyan
} else {
    Write-Host "ERROR: Could not detect version from InstallProgressWindow.xaml.cs" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

Write-Host "Selected platform: $Platform" -ForegroundColor Cyan

# Build VSTO project for ClickOnce
Write-Host "Building VSTO project for ClickOnce deployment..." -ForegroundColor Yellow

# Diagnostic information
Write-Host "Build environment check:" -ForegroundColor Cyan
Write-Host "  - Project file exists: $(Test-Path $vstoProjectPath)" -ForegroundColor Gray
Write-Host "  - Output directory: $clickOnceOutputDir" -ForegroundColor Gray
Write-Host "  - Platform: $Platform" -ForegroundColor Gray
Write-Host "  - Version: $version" -ForegroundColor Gray

# Find MSBuild for VSTO ClickOnce (required for Office Tools)
$msbuildPaths = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2018\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2018\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2018\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2018\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)

$msbuildPath = $null
foreach ($path in $msbuildPaths) {
    if (Test-Path $path) {
        $msbuildPath = $path
        break
    }
}

if (-not $msbuildPath) {
    # Try dotnet msbuild as fallback
    try {
        $dotnetVersion = dotnet --version 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Visual Studio MSBuild not found, trying dotnet msbuild..." -ForegroundColor Yellow
            Write-Host "Detected .NET SDK version: $dotnetVersion" -ForegroundColor Cyan
            Write-Host ""
            Write-Host "WARNING: .NET SDK MSBuild has limitations with VSTO projects!" -ForegroundColor Red
            Write-Host "  - Missing Office Tools targets" -ForegroundColor Yellow
            Write-Host "  - ClickOnce deployment may fail" -ForegroundColor Yellow
            Write-Host "  - Recommended: Install Visual Studio with Office workload" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Continuing with .NET SDK MSBuild (this will likely fail)..." -ForegroundColor Yellow
            
            $msbuildPath = "dotnet"
            $useDotnetMSBuild = $true
        } else {
            throw "dotnet not found"
        }
    } catch {
        Write-Host "ERROR: Neither Visual Studio MSBuild nor .NET SDK found." -ForegroundColor Red
        Write-Host ""
        Write-Host "For VSTO ClickOnce deployment, you need either:" -ForegroundColor Yellow
        Write-Host "1. Visual Studio with Office/SharePoint development workload (RECOMMENDED)" -ForegroundColor Cyan
        Write-Host "2. .NET SDK with MSBuild (LIMITED - will likely fail for VSTO)" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Download Visual Studio: https://visualstudio.microsoft.com/downloads/" -ForegroundColor Cyan
        Write-Host "Download .NET SDK: https://dotnet.microsoft.com/download" -ForegroundColor Cyan
        if (-not $NoWait) { Read-Host "Press Enter to continue" }
        exit 1
    }
} else {
    $useDotnetMSBuild = $false
}

Write-Host "Using MSBuild: $msbuildPath" -ForegroundColor Gray
if ($useDotnetMSBuild) {
    Write-Host "Note: Using .NET SDK MSBuild - some VSTO features may be limited" -ForegroundColor Yellow
}

# Build and publish ClickOnce
if ($useDotnetMSBuild) {
    # Use dotnet msbuild
    $publishArgs = @(
        "msbuild"
        "`"$vstoProjectPath`""
        "/p:Configuration=Release"
        "/p:Platform=$Platform"
        "/p:PublishUrl=$clickOnceOutputDir\"
        "/p:InstallUrl="
        "/p:ApplicationVersion=$vstoVersion"
        "/p:UpdateEnabled=true"
        "/p:UpdateMode=Foreground"
        "/p:UpdateInterval=7"
        "/p:UpdateIntervalUnits=Days"
        "/p:UpdatePeriodically=false"
        "/p:UpdateRequired=false"
        "/p:MapFileExtensions=true"
        "/p:ApplicationRevision=1"
        "/p:UseApplicationTrust=false"
        "/p:BootstrapperEnabled=true"
        "/p:CreateDesktopShortcut=true"
        "/p:PublishWizardShown=false"
        "/t:Publish"
        "/verbosity:normal"
    )
    
    Write-Host "Publishing ClickOnce deployment using dotnet msbuild..." -ForegroundColor Yellow
    & dotnet $publishArgs
} else {
    # Use Visual Studio MSBuild
    $publishArgs = @(
        "`"$vstoProjectPath`""
        "/p:Configuration=Release"
        "/p:Platform=$Platform"
        "/p:PublishUrl=$clickOnceOutputDir\"
        "/p:InstallUrl="
        "/p:ApplicationVersion=$vstoVersion"
        "/p:UpdateEnabled=true"
        "/p:UpdateMode=Foreground"
        "/p:UpdateInterval=7"
        "/p:UpdateIntervalUnits=Days"
        "/p:UpdatePeriodically=false"
        "/p:UpdateRequired=false"
        "/p:MapFileExtensions=true"
        "/p:ApplicationRevision=1"
        "/p:UseApplicationTrust=false"
        "/p:BootstrapperEnabled=true"
        "/p:CreateDesktopShortcut=true"
        "/p:PublishWizardShown=false"
        "/t:Publish"
        "/verbosity:normal"
    )
    
    Write-Host "Publishing ClickOnce deployment using Visual Studio MSBuild..." -ForegroundColor Yellow
    & $msbuildPath $publishArgs
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: ClickOnce publish failed" -ForegroundColor Red
    
    if ($useDotnetMSBuild) {
        Write-Host ""
        Write-Host "VSTO ClickOnce deployment requires Visual Studio MSBuild with Office Tools." -ForegroundColor Yellow
        Write-Host "The .NET SDK MSBuild lacks the required VSTO targets:" -ForegroundColor Yellow
        Write-Host "  - Microsoft.VisualStudio.Tools.Office.targets" -ForegroundColor Gray
        Write-Host "  - Office/SharePoint development tools" -ForegroundColor Gray
        Write-Host ""
        Write-Host "SOLUTION: Install Visual Studio with Office/SharePoint workload" -ForegroundColor Cyan
        Write-Host "Download: https://visualstudio.microsoft.com/downloads/" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Required workloads:" -ForegroundColor Yellow
        Write-Host "  ✓ .NET desktop development" -ForegroundColor Green
        Write-Host "  ✓ Office/SharePoint development" -ForegroundColor Green
        Write-Host ""
        Write-Host "Alternative: Use the main WPF installer build instead" -ForegroundColor Yellow
        Write-Host "Run: Build\\build-menu.bat (options 1-6)" -ForegroundColor Cyan
    } else {
        Write-Host "Check the build output above for specific errors." -ForegroundColor Yellow
        Write-Host "Common issues:" -ForegroundColor Yellow
        Write-Host "  - Missing Office development tools in Visual Studio" -ForegroundColor Gray
        Write-Host "  - Project dependencies not restored" -ForegroundColor Gray
        Write-Host "  - Platform mismatch (AnyCPU vs x64)" -ForegroundColor Gray
    }
    
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

# Verify ClickOnce files were created and copy them to our output directory
$vstoProjectDir = Split-Path -Parent $vstoProjectPath
$appPublishDir = Join-Path $vstoProjectDir "bin\Release\app.publish"
$setupExe = Join-Path $appPublishDir "setup.exe"
$vstoFile = Join-Path $appPublishDir "KleiKodesh.vsto"
$applicationFilesDir = Join-Path $appPublishDir "Application Files"

if (-not (Test-Path $setupExe)) {
    Write-Host "ERROR: ClickOnce setup.exe not found at $setupExe" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

Write-Host "ClickOnce deployment created successfully!" -ForegroundColor Green
Write-Host "Copying ClickOnce files to output directory..." -ForegroundColor Yellow

# Copy all ClickOnce files to our output directory
Copy-Item -Path "$appPublishDir\*" -Destination $clickOnceOutputDir -Recurse -Force

Write-Host "ClickOnce files copied to: $clickOnceOutputDir" -ForegroundColor Cyan

# Version is already available from the increment step above
Write-Host "Using WPF installer version: $version" -ForegroundColor Cyan
Write-Host "Using VSTO project version: $vstoVersion" -ForegroundColor Cyan

# Create NSIS auto-extractor script
$iconPath = Join-Path $projectRoot "KleiKodeshVsto\Resources\KleiKodesh_Main.ico"

Write-Host "Icon path: $iconPath" -ForegroundColor Gray
Write-Host "Icon exists: $(Test-Path $iconPath)" -ForegroundColor Gray
Write-Host "NSIS will use relative path: ..\\..\\KleiKodeshVsto\\Resources\\KleiKodesh_Main.ico" -ForegroundColor Cyan

if (Test-Path $iconPath) {
    $iconSize = (Get-Item $iconPath).Length
    Write-Host "Icon file size: $iconSize bytes" -ForegroundColor Gray
    
    if ($iconSize -lt 1024) {
        Write-Host "WARNING: Icon file is very small ($iconSize bytes) - may be corrupted" -ForegroundColor Yellow
    } elseif ($iconSize -gt 1048576) {
        Write-Host "WARNING: Icon file is very large ($iconSize bytes) - may cause issues" -ForegroundColor Yellow
    } else {
        Write-Host "Icon file size looks good" -ForegroundColor Green
    }
} else {
    Write-Host "ERROR: Icon file not found at $iconPath" -ForegroundColor Red
    Write-Host "NSIS will compile without icon" -ForegroundColor Yellow
}

$nsisScript = @"
!define PRODUCT_NAME "KleiKodesh"
!define PRODUCT_VERSION "$vstoVersion"
!define PRODUCT_PUBLISHER "KleiKodesh"
!define PRODUCT_WEB_SITE "https://github.com/shoshiiran/KleiKodeshProject"

!include "MUI2.nsh"
!include "FileFunc.nsh"

Name "`${PRODUCT_NAME} `${PRODUCT_VERSION}"
OutFile "KleiKodeshClickOnce-`${PRODUCT_VERSION}.exe"
InstallDir "`$APPDATA\`${PRODUCT_NAME}\ClickOnceTemp"
RequestExecutionLevel user
ShowInstDetails show

; Set the installer icon (this is what shows in Windows Explorer)
Icon "..\\..\\KleiKodeshVsto\\Resources\\KleiKodesh_Main.ico"

; Set the MUI icon for installer pages
!define MUI_ABORTWARNING
!define MUI_ICON "..\\..\\KleiKodeshVsto\\Resources\\KleiKodesh_Main.ico"
!define MUI_UNICON "..\\..\\KleiKodeshVsto\\Resources\\KleiKodesh_Main.ico"

; Add version information to the executable
VIProductVersion "$vstoVersion"
VIAddVersionKey "ProductName" "`${PRODUCT_NAME}"
VIAddVersionKey "ProductVersion" "`${PRODUCT_VERSION}"
VIAddVersionKey "CompanyName" "`${PRODUCT_PUBLISHER}"
VIAddVersionKey "FileDescription" "`${PRODUCT_NAME} ClickOnce Installer"
VIAddVersionKey "FileVersion" "$vstoVersion"
VIAddVersionKey "LegalCopyright" "© `${PRODUCT_PUBLISHER}"

!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "MainSection" SEC01
    SetOutPath "`$INSTDIR"
    
    ; Extract all ClickOnce files using directory-based approach
    File /r "`${CLICKONCE_SOURCE}\*"
    
    ; Create extraction complete marker
    FileOpen `$0 "`$INSTDIR\extraction_complete.marker" w
    FileWrite `$0 "ClickOnce files extracted successfully"
    FileClose `$0
    
    ; Run ClickOnce setup
    DetailPrint "Starting ClickOnce installation..."
    
    ; Wait a moment to ensure all files are fully extracted
    Sleep 2000
    
    ; Verify setup.exe exists
    IfFileExists "`$INSTDIR\setup.exe" SetupOK SetupError
    
    SetupError:
        DetailPrint "ERROR: setup.exe not found after extraction"
        MessageBox MB_ICONSTOP "ClickOnce setup.exe was not extracted properly. Please try again."
        Abort
    
    SetupOK:
        DetailPrint "setup.exe verified, checking application files..."
        
        ; Verify Application Files directory exists
        IfFileExists "`$INSTDIR\Application Files\*.*" AppFilesOK AppFilesError
        
        AppFilesError:
            DetailPrint "ERROR: Application Files directory not found"
            MessageBox MB_ICONSTOP "ClickOnce application files were not extracted properly. Please try again."
            Abort
        
        AppFilesOK:
            DetailPrint "All critical files verified, proceeding with ClickOnce installation..."
            
            ; Run ClickOnce setup with proper error handling
            ExecWait '"`$INSTDIR\setup.exe"' `$1
    
    ; Check if installation was successful
    `${If} `$1 == 0
        DetailPrint "ClickOnce installation completed successfully"
        
        ; Simple delayed cleanup - just wait and clean up
        DetailPrint "ClickOnce installation completed successfully"
        DetailPrint "Temporary files will remain for manual cleanup if needed"
        DetailPrint "Location: `$INSTDIR"
        
        DetailPrint "Installation location: `$INSTDIR"
    `${Else}
        DetailPrint "ClickOnce installation returned code: `$1"
        
        `${If} `$1 == -2147009290
            ; User cancelled installation
            DetailPrint "Installation was cancelled by user"
            MessageBox MB_ICONINFORMATION "Installation was cancelled."
        `${ElseIf} `$1 == -2147009294
            ; Trust not granted
            DetailPrint "Installation failed: Trust not granted"
            MessageBox MB_ICONEXCLAMATION "Installation failed: Application trust was not granted.$\n$\nThis may be due to security settings or network restrictions."
        `${Else}
            DetailPrint "Installation failed with error code: `$1"
            MessageBox MB_ICONEXCLAMATION "Installation may not have completed successfully.$\n$\nError code: `$1$\n$\nPlease check Windows Event Log for more details."
        `${EndIf}
        
        ; Clean up immediately on failure, but with a small delay
        DetailPrint "Cleaning up temporary files..."
        Sleep 3000
        RMDir /r "`$INSTDIR"
    `${EndIf}
SectionEnd

Function .onInit
    ; Check if .NET Framework 4.8 is installed
    ReadRegStr `$0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
    `${If} `$0 == ""
        MessageBox MB_YESNO|MB_ICONQUESTION ".NET Framework 4.8 is required but not detected. Continue anyway?" IDYES continue
        Abort
    continue:
    `${EndIf}
    
    ; Check if VSTO Runtime is available
    ReadRegStr `$1 HKLM "SOFTWARE\Microsoft\VSTO Runtime Setup\v4R" "Version"
    `${If} `$1 == ""
        MessageBox MB_YESNO|MB_ICONQUESTION "Visual Studio Tools for Office Runtime may not be installed. Continue anyway?" IDYES continue2
        Abort
    continue2:
    `${EndIf}
FunctionEnd

Function .onInstSuccess
    MessageBox MB_ICONINFORMATION "KleiKodesh has been installed successfully!$\n$\nThe add-in will be available in Microsoft Word."
FunctionEnd
"@

$nsisScriptPath = Join-Path $scriptDir "KleiKodeshClickOnce.nsi"
$nsisScript = $nsisScript.Replace('${CLICKONCE_SOURCE}', $clickOnceOutputDir)
Set-Content -Path $nsisScriptPath -Value $nsisScript -Encoding UTF8

Write-Host "NSIS script created: $nsisScriptPath" -ForegroundColor Cyan

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

# Build NSIS auto-extractor
Write-Host "Building NSIS auto-extractor..." -ForegroundColor Yellow
Write-Host "NSIS path: $nsisPath" -ForegroundColor Gray
Write-Host "NSIS script: $nsisScriptPath" -ForegroundColor Gray

# Add verbose output for NSIS compilation
& $nsisPath "/V4" $nsisScriptPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: NSIS build failed" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

$finalInstaller = Join-Path $scriptDir "KleiKodeshClickOnce-$vstoVersion.exe"
if (-not (Test-Path $finalInstaller)) {
    Write-Host "ERROR: Final installer not created at $finalInstaller" -ForegroundColor Red
    if (-not $NoWait) { Read-Host "Press Enter to continue" }
    exit 1
}

Write-Host ""
Write-Host "SUCCESS: KleiKodeshClickOnce-$vstoVersion.exe created!" -ForegroundColor Green
Write-Host "This installer will:" -ForegroundColor Cyan
Write-Host "  1. Extract ClickOnce files to %APPDATA%\KleiKodesh\ClickOnceTemp" -ForegroundColor Cyan
Write-Host "  2. Run the ClickOnce setup.exe automatically" -ForegroundColor Cyan
Write-Host "  3. Clean up temporary files after installation" -ForegroundColor Cyan
Write-Host "  4. Check for .NET Framework 4.8 and VSTO Runtime" -ForegroundColor Cyan
Write-Host ""
Write-Host "Platform: $Platform" -ForegroundColor Cyan
Write-Host "VSTO Version: $vstoVersion" -ForegroundColor Cyan
Write-Host "Installer location: $finalInstaller" -ForegroundColor Cyan

if (-not $NoWait) {
    Read-Host "Press Enter to continue"
}