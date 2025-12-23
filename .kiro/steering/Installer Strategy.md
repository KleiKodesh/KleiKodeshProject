## NSIS Wrapper Installer Strategy
- **Two-tier installer approach**: NSIS wrapper + WPF installer
- **NSIS handles**: .NET dependency checking, system compatibility, uninstaller registry cleanup, system-level operations
- **WPF installer handles**: Application-specific installation logic, UI, settings, file operations
- **Purpose**: Use familiar NSIS tools for system-level operations while maintaining custom WPF installer for application logic

### NSIS Flow:
1. **Check .NET Framework** - Verify required version exists
2. **If .NET missing** - Notify user and abort
3. **If .NET exists** - Extract WPF installer to temp directory
4. **Run WPF installer** - Execute the actual installation
5. **Check WPF exit code**:
   - **Exit code 1** (aborted/failed) - Clean temp and exit NSIS
   - **Exit code 0** (success) - Install uninstaller, then clean temp
6. **Clean up temp** - Always remove temporary files

### Build Pipeline:
1. **WPF Installer Prebuild** - Builds VSTO in Release mode, packs it in zip, adds to resource stream
2. **Build Script** - Builds WPF installer in Release mode
3. **NSIS Compilation** - Packs the Release WPF installer into final executable

### Responsibilities:
- **Uninstaller responsibility**: NSIS uninstaller must clean up all files and registry keys created by WPF installer
- **Dependency checking**: NSIS checks for compatible .NET version before launching WPF installer
- **Error handling**: Clear user messaging when .NET dependencies are missing
- **Build process**: NSIS script packages the WPF installer release into a single executable
- **Registry cleanup**: NSIS uninstaller removes all registry entries created during installation process
- **File cleanup**: NSIS uninstaller removes entire installation directory and all created files