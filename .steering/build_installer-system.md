---
inclusion: fileMatch
fileMatchPattern: '**/KleiKodeshVstoInstallerWpf/**'
---

# Installer Build System

## NSIS Wrapper Strategy
- **Two-tier approach**: NSIS wrapper + WPF installer
- **NSIS handles**: .NET dependency checking, system compatibility, uninstaller registry cleanup
- **WPF installer handles**: Application-specific installation logic, UI, settings, file operations

## Build Pipeline
1. **Version Extraction** - Extract version from `InstallProgressWindow.xaml.cs`
2. **WPF Installer Build** - Build in Release mode with prebuild events using `dotnet build`
3. **Automatic VSTO Build** - Handled by WPF installer prebuild event using MSBuild
4. **NSIS Compilation** - Compile with dynamic version parameter
5. **GitHub Release Creation** - Auto-create release with installer asset

## Build Architecture
- **WPF Installer**: Uses `dotnet build` with architecture support (`--arch x64` for x64 builds)
- **VSTO Project**: Built automatically by WPF installer prebuild event using MSBuild
- **Platform Support**: Both AnyCPU and x64 configurations supported
- **Path Resolution**: Uses absolute paths to avoid directory context issues

## Version Management
- **Auto-Increment System**: `build-installer.ps1` calls `UpdateVersion.ps1` to increment patch version before building
- **GitHub Integration**: Fetches latest release version from GitHub API, increments patch number
- **File Update**: Updates `const string Version` in `InstallProgressWindow.xaml.cs` before build starts
- **Storage**: Registry `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh\Version`
- **Format**: GitHub tag format (e.g., "v1.0.31")
- **Sync**: Both WPF and NSIS use same version number
- **Dynamic Passing**: Build script passes version to NSIS via `/DPRODUCT_VERSION=$version`
- **Single Source**: Version increment only happens in build script, NOT in MSBuild prebuild targets

## Build Script Features
- **Verbose Output**: Uses `--verbosity normal` for detailed build information
- **Architecture Detection**: Automatically detects and applies correct architecture parameters
- **Error Handling**: Comprehensive error checking with detailed messages
- **Path Safety**: Uses absolute paths with `$scriptDir` and `$projectRoot` variables
- **Platform Parameters**: Passes VSTO configuration to WPF installer via MSBuild properties

## Release Notes Enhancement
- **Build Configuration Details**: Includes VSTO and WPF build configurations
- **Architecture Information**: Shows specific architecture used for compilation
- **Build Tool Information**: Indicates which build tools were used (MSBuild vs dotnet build)

## Silent Installation
- **Arguments**: `--silent` or `/silent`
- **Flow**: NSIS passes to WPF installer → skips main window → goes to InstallProgressWindow
- **Use case**: Future updater silent installation