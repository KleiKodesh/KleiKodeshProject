# Build Scripts — PowerShell Build Orchestration

PowerShell scripts that orchestrate the build, packaging, and deployment pipeline for KleiKodesh.

## Files

**`build-menu.bat`** (located at `Build/build-menu.bat`, not in this folder) — Entry point batch file that launches the interactive build menu.

**`build-menu.ps1`** — Interactive build menu (prompts user for version, notes source, confirmation). Launched by `build-menu.bat`. Options:
1. Full Release Build + GitHub Release
2. Release Build Only (no GitHub release)
3. Quick Build Test (no version change)
4. Clean Build (wipe dirs first)
5. View Build Info
6. Open Build Folder
7. Clear All GitHub Releases

Version choice includes **Auto** and notes choice includes **AI**: both run `.github/scripts/ai-release-plan.ps1` (Gemini, needs `GEMINI_API_KEY`) first, like the GitHub Actions release.

**`build-installer.ps1`** — Main orchestrator (headless, no interactivity). Called by `build-menu.ps1` and by `.github/workflows/release.yml`. Flow:
1. Drops the Vue build stamp + `*.tsbuildinfo`, wipes `KleiKodeshVsto\bin\Release\` + `KitveiHakodeshDemoApp\bin\Release\`
2. Calls `UpdateVersion.ps1` to bump version in `AddinInstaller.cs` + `.csproj` + the KitveiHakodesh DemoApp's `AssemblyInfo.cs`, then verifies the stamp landed
3. Restores packages (msbuild restore, nuget for packages.config, `npm ci` in CI / `npm install --no-save` locally); cleans the solution unless `-NoClean`
4. Creates a fresh 5-year self-signed add-in signing certificate
5. Vue `npm run build` → service native-AOT publish → VSTO build (AnyCPU) → payload pack → WPF installer `dotnet build` → NSIS → `KleiKodeshSetup-vX.Y.Z.exe`
6. Builds `KitveiHakodeshDemoApp` (Release|AnyCPU) via MSBuild → zips output → `KitveiHakodeshPortable-vX.Y.Z.zip`
7. Unless `-NoRelease`: creates the GitHub release (deletes an existing same-tag release first), uploads EXE + ZIP, then dispatches the website update, `release-issue-check.yml` and `drive-upload.yml` (syncs Google Drive to the latest two releases)

**`build-helpers.ps1`** — Shared path constants and utility functions used by other build scripts:
- `$DemoAppProjectPath` — Path to `KitveiHakodeshDemoApp.csproj`
- `$DemoAppReleaseDir` — Path to `KitveiHakodeshDemoApp\bin\Release\`
- `Get-CurrentVersion` — Reads version from `AddinInstaller.cs`
- `Find-MSBuild` — Locates MSBuild from Visual Studio installation
- `Invoke-SolutionClean` — Cleans the solution via MSBuild or dotnet
- `Update-SigningCertificate` — New self-signed code-signing cert, thumbprint written into `KleiKodeshVsto.csproj`
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
