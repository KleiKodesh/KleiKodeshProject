# Build Scripts — PowerShell Build Orchestration

PowerShell scripts that orchestrate the build, packaging, and deployment pipeline for KleiKodesh.

## Files

**`build-menu.bat`** — Entry point batch file that launches the interactive build menu.

**`build-menu.ps1`** — Interactive build menu (prompts user for version, notes source, confirmation). Launched by `build-menu.bat`. Options:
- Build (full three-variant build)
- Clean (remove build artifacts)
- Test (run validation)
- GitHub release (create + upload)
- Quick build (VSTO only, no installer)

**`build-installer.ps1`** — Main orchestrator (headless, no interactivity). Called by `build-menu.ps1`. Flow:
1. Calls `UpdateVersion.ps1` to bump version in `AddinInstaller.cs` + `.csproj`
2. Builds VSTO add-in for x64 → creates embedded zip
3. Builds WPF installer for x64 via `dotnet build`
4. Wraps in NSIS → `KleiKodeshSetup-vX.Y.Z-x64.exe`
5. Repeats for x86, AnyCPU
6. Optionally creates GitHub release with 3 installer EXEs

**`build-helpers.ps1`** — Shared functions used by other build scripts:
- `Get-VersionFromSource` — Reads version from `AddinInstaller.cs`
- `Invoke-MSBuild` — Wrapper around MSBuild with platform config
- `New-InstallerVariant` — Builds a single platform variant
- `Test-Prerequisites` — Checks for required tools (MSBuild, NSIS, VS)
- `New-GitHubRelease` — Creates release + uploads artifacts

## SvgToPng Subfolder

Contains a small C# console project (`SvgToPng.csproj`) used by build scripts to convert SVG ribbon icons to PNG for use in the NSIS installer. Run via `build-helpers.ps1` if icon assets have changed.

## Usage

```powershell
# Interactive menu (recommended)
Build\build-menu.bat

# Headless build (for CI/automation)
.\Build\scripts\build-installer.ps1 -VersionIncrement patch -ReleaseNotesSource commits
```
