---
inclusion: fileMatch
fileMatchPattern: '**/KleiKodeshVstoInstallerWpf/**|**/Build/**'
---

# Installer System

## Two-Tier Architecture
- **NSIS Wrapper**: .NET dependency checking, system compatibility, uninstaller registry
- **WPF Installer**: Application-specific installation logic, UI, settings, file operations

## Build Pipeline
1. **Version Increment** - `UpdateVersion.ps1` fetches from GitHub API, increments patch
2. **WPF Installer Build** - `dotnet build` with architecture support
3. **VSTO Build** - Automatic via WPF installer prebuild event using MSBuild
4. **NSIS Compilation** - Creates final installer with dynamic version
5. **GitHub Release** - Optional auto-create release with installer asset

## Version Management
- **Single Source**: `KleiKodeshVstoInstallerWpf/InstallProgressWindow.xaml.cs`
- **Auto-Increment**: Fetches latest from `https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest`
- **Format**: GitHub tag format (e.g., "v1.0.31")
- **Registry Storage**: `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh\Version`

## Installation Paths
- **New Location**: `%LOCALAPPDATA%\KleiKodesh` (per-user)
- **Old Location**: `%ProgramFiles(x86)%\KleiKodesh` (deprecated)
- **Registry**: `HKEY_CURRENT_USER\Software\Microsoft\Office\Word\Addins\KleiKodesh`

## Old Installation Cleanup
```csharp
// Automatic cleanup of old Program Files installations
await OldInstallationCleaner.CheckAndRemoveOldInstallations();
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