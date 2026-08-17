# Build Scripts — PowerShell Build Orchestration

PowerShell scripts that orchestrate the build, packaging, and deployment pipeline for KleiKodesh.

## Files

**`build-menu.bat`** (located at `Build/build-menu.bat`, not in this folder) — Entry point batch file that launches the interactive build menu.

**`build-menu.ps1`** — Interactive build menu (prompts user for version, notes source, confirmation). Launched by `build-menu.bat`. Options:
- Build (full three-variant build)
- Clean (remove build artifacts)
- Test (run validation)
- GitHub release (create + upload)
- Quick build (VSTO only, no installer)

**`build-installer.ps1`** — Main orchestrator (headless, no interactivity). Called by `build-menu.ps1`. Flow:
1. Calls `UpdateVersion.ps1` to bump version in `AddinInstaller.cs` + `.csproj` + the KitveiHakodesh DemoApp's `AssemblyInfo.cs`
2. Wipes VSTO release folders + `KitveiHakodeshDemoApp\bin\Release\` for a clean build
3. Builds VSTO add-in (AnyCPU) → creates embedded zip
4. Builds WPF installer via `dotnet build` → wraps in NSIS → `KleiKodeshSetup-vX.Y.Z.exe`
5. Builds `KitveiHakodeshDemoApp` (Release|AnyCPU) via MSBuild → zips output → `KitveiHakodeshPortable-vX.Y.Z.zip`
6. Optionally creates GitHub release and uploads installer EXE + portable ZIP

**`build-helpers.ps1`** — Shared path constants and utility functions used by other build scripts:
- `$DemoAppProjectPath` — Path to `KitveiHakodeshDemoApp.csproj`
- `$DemoAppReleaseDir` — Path to `KitveiHakodeshDemoApp\bin\Release\`
- `Get-CurrentVersion` — Reads version from `AddinInstaller.cs`
- `Find-MSBuild` — Locates MSBuild from Visual Studio installation
- `Invoke-SolutionClean` — Cleans the solution via MSBuild or dotnet
- `New-ReleaseNotes` — Builds release notes string from git commits and/or `RELEASE_NOTES.txt`

## SvgToPng Subfolder

Contains a small C# console project (`SvgToPng.csproj`) used by build scripts to convert SVG ribbon icons to PNG for use in the NSIS installer. Run via `build-helpers.ps1` if icon assets have changed.

## Usage

```powershell
# Interactive menu (recommended)
Build\build-menu.bat

# Headless build (for CI/automation)
.\Build\scripts\build-installer.ps1 -VersionIncrement patch -ReleaseNotesSource commits
```
