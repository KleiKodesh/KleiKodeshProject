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

## Responsibilities
- **Uninstaller responsibility**: NSIS uninstaller must clean up all files and registry keys created by WPF installer
- **Dependency checking**: NSIS checks for compatible .NET version before launching WPF installer
- **Registry cleanup**: NSIS uninstaller removes all registry entries created during installation process