---
inclusion: always
---

# Build System Requirements

## VSTO vs Modern .NET
- **VSTO Projects**: Require Visual Studio MSBuild (Office Tools dependencies)
- **Modern .NET**: Use `dotnet build` for better performance
- **Vue Projects**: Use `npm run build` for production builds

## Version Management
- **Single Source**: `KleiKodeshVstoInstallerWpf/InstallProgressWindow.xaml.cs`
- **Auto-Increment**: `UpdateVersion.ps1` fetches from GitHub API
- **Format**: GitHub tag format (e.g., "v1.0.31")
- **Sync**: All installers use same version

## Build Pipeline
1. **Version Increment** - Auto-fetch and increment from GitHub
2. **Vue Builds** - HTML/JS projects build via MSBuild targets
3. **VSTO Build** - MSBuild with Office Tools
4. **WPF Installer** - dotnet build with architecture support
5. **NSIS Wrapper** - Creates final installer executable

## Platform Support
- **AnyCPU**: Recommended for maximum compatibility
- **x64**: Specific 64-bit builds when needed
- **Architecture Detection**: Build scripts auto-detect and apply correct parameters