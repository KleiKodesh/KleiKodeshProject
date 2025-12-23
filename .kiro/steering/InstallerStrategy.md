## NSIS Wrapper Installer Strategy
- **Two-tier installer approach**: NSIS wrapper + WPF installer
- **NSIS handles**: .NET dependency checking, system compatibility, uninstaller registry cleanup, system-level operations
- **WPF installer handles**: Application-specific installation logic, UI, settings, file operations
- **Purpose**: Use familiar NSIS tools for system-level operations while maintaining custom WPF installer for application logic

### NSIS Flow:
1. **Check .NET Framework** - Verify required version exists
2. **If .NET missing** - Notify user and abort
3. **If .NET exists** - Extract WPF installer to temp directory
4. **Run WPF installer** - Execute the actual installation (with optional --silent argument)
5. **Check WPF exit code**:
   - **Exit code 1** (aborted/failed) - Clean temp and exit NSIS
   - **Exit code 0** (success) - Install uninstaller, then clean temp
6. **Clean up temp** - Always remove temporary files

### Silent Installation Support:
- **Command line arguments**: `--silent` or `/silent`
- **NSIS wrapper**: Passes silent argument to WPF installer
- **WPF installer**: Skips main window, goes directly to InstallProgressWindow
- **Use case**: Future updater can install silently without user interaction

### Version Management:
- **Version Storage**: WPF installer saves current version to registry `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh\Version`
- **Version Format**: Uses GitHub tag format (e.g., "v1.0.31")
- **Windows Programs List**: NSIS uninstaller registry entries include DisplayVersion for proper version display in "Add or Remove Programs"
- **Update Checking**: GitHubUpdateChecker reads registry version and compares with latest GitHub release
- **Build Automation**: WPF installer prebuild event automatically updates version from latest GitHub release
- **Version Sync**: Both WPF installer and NSIS wrapper use the same version number

### Coding Style:
- **Concise Code**: Prefer compact, readable code over verbose implementations
- **Modern C# Features**: Use target-typed new expressions, using declarations, expression-bodied members
- **Minimal Classes**: Only include necessary properties and methods, remove unused code
- **Single Responsibility**: Each class should have one clear purpose

### Build Pipeline:
1. **WPF Installer Prebuild** - Runs on EVERY Release build: deletes old zip, gets latest version from GitHub, updates InstallProgressWindow.xaml.cs, builds VSTO with latest code, creates fresh zip, adds to resource stream
2. **PowerShell Build Script** - Builds WPF installer in Release mode, compiles NSIS wrapper
3. **GitHub Release Creation** - Automatically creates new GitHub release with installer as asset
4. **NSIS Compilation** - Packs the Release WPF installer into final executable

### Build Automation Details:
- **Always Fresh**: Prebuild event runs on every Release build (not conditional on zip existence)
- **Latest Code**: Always builds and packages the most recent VSTO code changes
- **Version Sync**: Automatically fetches and applies latest GitHub release version
- **Clean Build**: Deletes existing zip before creating new one to ensure freshness
- **Auto Release**: Creates GitHub release with installer for each build

### Build Script Arguments:
- **Default behavior**: Waits for user input on completion or errors
- **-NoWait**: Skips all pause prompts for automated builds
- **GitHub Integration**: Uses GITHUB_TOKEN environment variable for release creation
- **Usage**: `.\build-installer.ps1 -NoWait` for CI/CD or automated scenarios
- **Setup**: Set GITHUB_TOKEN environment variable and update RepoOwner/RepoName in script

### Responsibilities:
- **Uninstaller responsibility**: NSIS uninstaller must clean up all files and registry keys created by WPF installer
- **Dependency checking**: NSIS checks for compatible .NET version before launching WPF installer
- **Error handling**: Clear user messaging when .NET dependencies are missing
- **Build process**: NSIS script packages the WPF installer release into a single executable
- **Registry cleanup**: NSIS uninstaller removes all registry entries created during installation process
- **File cleanup**: NSIS uninstaller removes entire installation directory and all created files