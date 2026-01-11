---
inclusion: fileMatch
fileMatchPattern: '**/KleiKodeshVstoInstallerWpf/**'
---

# Installer Project Details

## Build Script Arguments
- **Default behavior**: Waits for user input on completion or errors
- **-NoWait**: Skips all pause prompts for automated builds
- **GitHub Integration**: Uses GITHUB_TOKEN environment variable
- **Usage**: `.\build-installer.ps1 -NoWait` for CI/CD scenarios

## NSIS Flow
1. **Check .NET Framework** - Verify required version exists
2. **If .NET missing** - Notify user and abort
3. **If .NET exists** - Extract WPF installer to temp directory
4. **Run WPF installer** - Execute installation (with optional --silent argument)
5. **Check WPF exit code**:
   - **Exit code 1** (aborted/failed) - Clean temp and exit NSIS
   - **Exit code 0** (success) - Install uninstaller, then clean temp
6. **Clean up temp** - Always remove temporary files

## Build Automation Details
- **Always Fresh**: Prebuild event runs on every Release build
- **Latest Code**: Always builds and packages most recent VSTO code changes
- **Version Sync**: Automatically fetches and applies latest GitHub release version
- **Clean Build**: Deletes existing zip before creating new one
- **Auto Release**: Creates GitHub release with installer for each build

## Installation Path Changes
- **New Installation Location**: `%LOCALAPPDATA%\KleiKodesh` (App Data folder)
- **Previous Location**: `%ProgramFiles(x86)%\KleiKodesh` (Program Files - deprecated)
- **Benefits**: Per-user installs, cleaner system, no conflicts between users
- **WPF Implementation**: Uses `Environment.SpecialFolder.LocalApplicationData`
- **NSIS Implementation**: Uses `$LOCALAPPDATA\KleiKodesh`

## Registry Strategy - User-Specific Installation
- **New Registry Location**: `HKEY_CURRENT_USER\Software\Microsoft\Office\Word\Addins\KleiKodesh`
- **Previous Location**: `HKEY_LOCAL_MACHINE\Software\Microsoft\Office\Word\Addins\KleiKodesh` (deprecated)
- **Benefits**: Per-user add-in registration, no system-wide pollution, cleaner uninstalls
- **Implementation**: Single registry write to HKCU (no separate 32-bit/64-bit handling needed)
- **Backward Compatibility**: Old HKLM entries are cleaned up during installation

## Old Installation Cleanup
- **OldInstallationCleaner Class**: Handles detection and removal of old Program Files installations
- **Cleanup Locations**: Both `%ProgramFiles%\KleiKodesh` and `%ProgramFiles(x86)%\KleiKodesh`
- **Registry Cleanup**: Removes old HKLM Office add-in entries from both 32-bit and 64-bit registry views
- **Error Handling**: Won't fail new installation if old cleanup fails
- **Permission Handling**: Falls back to individual file deletion if directory removal fails
- **Integration Point**: Call `await OldInstallationCleaner.CheckAndRemoveOldInstallations()` before new installation
- **Methods Available**:
  - `CheckAndRemoveOldInstallations()` - Main cleanup method
  - `HasOldInstallations()` - Detection only
  - `GetOldInstallationInfo()` - Descriptive information about found installations

## Installation Parameter Flow
- **MainWindow Responsibility**: Collects user settings from UI controls and passes to InstallProgressWindow
- **Parameter Collection**:
  - `defaultButton` - Retrieved from radio button selection via SettingsManager
  - `kezayit`, `hebrewbooks`, `websites`, `kleiKodesh` - Retrieved from checkbox states via SettingsManager
- **Parameter Passing**: MainWindow.Install() method passes all parameters to InstallProgressWindow constructor
- **InstallProgressWindow**: Receives parameters and uses them in installation process
- **Settings Storage**: Uses SettingsManager for persistent storage of user preferences

## Admin Mode Requirement
- **CRITICAL**: Both WPF installer and NSIS wrapper MUST always run in administrator mode
- **Reason**: Required for Office add-in registry modifications in HKEY_LOCAL_MACHINE
- **Registry Access**: Office add-in registration requires HKLM write permissions
- **Old Installation Cleanup**: Removing old Program Files installations requires admin rights
- **NSIS Setting**: `RequestExecutionLevel admin` is mandatory
- **WPF Manifest**: Must include `requireAdministrator` execution level
- **No Fallback**: Installation will fail without admin rights - this is by design

## Responsibilities
- **Uninstaller responsibility**: NSIS uninstaller must clean up all files and registry keys created by WPF installer
- **New Path Cleanup**: NSIS uninstaller removes `$LOCALAPPDATA\KleiKodesh` (updated from old Program Files path)
- **Dependency checking**: NSIS checks for compatible .NET version before launching WPF installer
- **Registry cleanup**: NSIS uninstaller removes all registry entries created during installation process
- **Old Installation Migration**: WPF installer automatically detects and removes old Program Files installations before installing to App Data