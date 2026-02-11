---
inclusion: fileMatch
fileMatchPattern: '**/KleiKodeshVstoInstallerWpf/**|**/Build/**'
---

# Installer System

## Two-Tier Architecture
- **NSIS Wrapper**: .NET Framework 4.8 dependency checking, system compatibility, per-user uninstaller registry
- **WPF Installer**: Application-specific installation logic, UI, settings, file operations (.NET Framework 4.8)

## Per-User Installation (No Admin Required)
- **Execution Level**: `user` (no admin rights needed)
- **Install Location**: `%LOCALAPPDATA%\KleiKodesh` (per-user)
- **Registry**: All entries in `HKCU` (current user)
- **Uninstaller Registry**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh`

## Build Pipeline
1. **Version Increment** - `UpdateVersion.ps1` fetches from GitHub API, increments patch
2. **WPF Installer Build** - `dotnet build` with architecture support (outputs to `net48` subdirectory)
3. **VSTO Build** - Automatic via WPF installer prebuild event using MSBuild
4. **NSIS Compilation** - Creates final installer with dynamic version (uses `net48` path)
5. **GitHub Release** - Optional auto-create release with installer asset

## Version Management
- **Single Source**: `KleiKodeshVstoInstallerWpf/InstallProgressWindow.xaml.cs`
- **Auto-Increment**: Fetches latest from `https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest`
- **Format**: GitHub tag format (e.g., "v1.0.31")
- **Registry Storage**: `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh\Version`

## Installation Paths
- **Current Location**: `%LOCALAPPDATA%\KleiKodesh` (per-user, no admin required)
- **Old Locations**: Cleaned by uninstaller only (not during install)
  - `%ProgramFiles(x86)%\KleiKodesh` (deprecated)
  - `%ProgramFiles%\KleiKodesh` (deprecated x64)

## Registry Entries Created by WPF Installer
- **Add-in Registration**: `HKCU\Software\Microsoft\Office\Word\Addins\KleiKodesh`
- **Add-in Metadata**: `HKCU\Software\Microsoft\Office\Word\AddinsData\KleiKodesh`
- **Version Info**: `HKCU\SOFTWARE\KleiKodesh\Version`
- **VSTO Inclusion List**: `HKCU\SOFTWARE\Microsoft\VSTO\Security\Inclusion\[base64-key]`
- **VSTO Trusted Paths**: `HKCU\SOFTWARE\Microsoft\VSTO\Security\TrustedPaths\[base64-key]`

## Old Installation Cleanup
**Removed from installer** - Old installations are now cleaned only by the uninstaller to avoid requiring admin rights during installation.

```csharp
// OLD CODE (removed to eliminate admin requirement):
// await OldInstallationCleaner.CheckAndRemoveOldInstallations();
```

## Uninstaller Cleanup (NSIS)
The NSIS uninstaller removes ALL files, folders, and registry entries:

**Files & Folders:**
- `$LOCALAPPDATA\KleiKodesh` (current installation)
- `$PROGRAMFILES\KleiKodesh` (old x64 installations)
- `$PROGRAMFILES32\KleiKodesh` (old x86 installations)

**Registry Entries:**
- `HKCU\Software\Microsoft\Office\Word\Addins\KleiKodesh`
- `HKCU\Software\Microsoft\Office\Word\AddinsData\KleiKodesh`
- `HKCU\SOFTWARE\KleiKodesh`
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh` (uninstaller entry)
- `HKLM\Software\Microsoft\Office\Word\Addins\KleiKodesh` (old machine-wide)
- `HKLM\Software\WOW6432Node\Microsoft\Office\Word\Addins\KleiKodesh` (old 32-bit)
- All VSTO security entries containing "KleiKodesh" (enumerated dynamically)

**VSTO Security Cleanup:**
```nsis
; Enumerates and removes base64-encoded VSTO security entries
Call un.CleanupVSTOSecurityEntries
```

## Silent Installation
- **Arguments**: `--silent` or `/silent`
- **Flow**: NSIS passes to WPF → skips MainWindow → InstallProgressWindow
- **Use Case**: Deferred updates during app shutdown

## Build Script Features
- **Platform Support**: AnyCPU (recommended) and x64
- **Verbose Output**: `--verbosity normal` for detailed build info
- **Error Handling**: Comprehensive error checking
- **Path Safety**: Uses absolute paths with `$scriptDir` variables

## ClickOnce Alternative
- **Different Deployment**: Uses ClickOnce technology instead of traditional installer
- **Auto-Extractor**: NSIS wrapper extracts to `%APPDATA%\KleiKodesh\ClickOnceTemp`
- **Built-in Updates**: ClickOnce handles updates automatically
- **User-Specific**: Per-user installation without admin rights

## .NET Runtime Requirements
- **WPF Installer**: Requires .NET Framework 4.8 (same as VSTO)
- **VSTO Add-in**: Requires .NET Framework 4.8 + VSTO Runtime 2010
- **NSIS Wrapper**: Checks for .NET Framework 4.8 and VSTO Runtime only
- **Detection Methods**: 
  1. Registry check for .NET Framework 4.8 release number (528040)
  2. WOW6432Node registry check for 32-bit on 64-bit systems
  3. VSTO Runtime detection via multiple registry locations and GAC
  4. Fallback error message with download links
- **Error Messages**: Provide correct download links for prerequisites in Hebrew
- **Recovery Options**: Offers to open download pages for missing prerequisites
- **Office Installation**: Not checked - assumes Office is available when VSTO add-in runs