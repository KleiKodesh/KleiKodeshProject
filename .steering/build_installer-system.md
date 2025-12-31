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
2. **WPF Installer Build** - Build in Release mode with prebuild events
3. **NSIS Compilation** - Compile with dynamic version parameter
4. **GitHub Release Creation** - Auto-create release with installer asset

## Version Management
- **Storage**: Registry `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh\Version`
- **Format**: GitHub tag format (e.g., "v1.0.31")
- **Sync**: Both WPF and NSIS use same version number
- **Dynamic Passing**: Build script passes version to NSIS via `/DPRODUCT_VERSION=$version`

## Silent Installation
- **Arguments**: `--silent` or `/silent`
- **Flow**: NSIS passes to WPF installer → skips main window → goes to InstallProgressWindow
- **Use case**: Future updater silent installation