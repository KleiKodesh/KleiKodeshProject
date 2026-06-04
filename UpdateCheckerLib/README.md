# UpdateCheckerLib

Library for checking and downloading app updates from GitHub releases.

## What It Does

Monitors GitHub releases for new versions of KleiKodesh and automatically downloads and installs them.

- **UpdateChecker.cs** — Main service that fetches the latest release from GitHub, compares against the installed version (read from the Windows registry), and triggers download if a newer version exists.
- **DownloadManager.cs** — Handles downloading the installer `.exe` file to a temp location and launching it (with UAC handling via `runas` verb).
- **DownloadProgressWindow.xaml** — WPF window showing download progress and cancellation option.
- **GithubRelease.cs** — Data model for GitHub release JSON (version tag, download URL, etc.).

## Integration

Called from `KleiKodeshVsto/Helpers/TaskpaneManager.cs` on first taskpane open (unless the user disabled auto-update checks in settings).

The installer being downloaded is the **NSIS wrapper** (`KleiKodeshSetup-vX.Y.Z.exe`), not the raw WPF installer. The NSIS wrapper checks OS prerequisites (Windows 10+, .NET Framework 4.7.2+, VSTO runtime) before handing off to the WPF installer.

## Version Flow

```
UpdateChecker.GetCurrentVersionFromRegistry()
  → reads HKCU\SOFTWARE\KleiKodesh\Version (e.g. "v3.4.0")
  ↓
Fetches https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest
  ↓
Compares versions (semantic versioning)
  ↓
If newer available:
  → DownloadManager.DownloadAsync() fetches KleiKodeshSetup-vX.Y.Z.exe to %TEMP%
  → DownloadManager.LaunchInstaller() runs it with verb="runas" (UAC + AIS handoff)
  → NSIS wrapper runs → WPF installer → SaveVersion() updates registry
```

For full version management details, see `.kiro/steering/version-management.md`.

## Folder Structure

```
UpdateCheckerLib/
├── UpdateChecker.cs           — Main service
├── DownloadManager.cs         — Download + install launcher
├── DownloadProgressWindow.xaml — Progress UI (WPF)
├── DownloadProgressWindow.xaml.cs
├── GithubRelease.cs           — API model
├── UpdateCheckerLib.csproj    — Must define Release|x64, Release|x86, Release|AnyCPU output paths
└── packages.config            — NuGet dependencies
```

## Build Configuration

Part of the three-variant build pipeline (x64, x86, AnyCPU). The `.csproj` must define `OutputPath` for each platform variant. See `.kiro/steering/build-variants.md`.

## Dependencies

- System.Net.Http (for GitHub API requests)
- System.Windows (WPF for progress window)
- Newtonsoft.Json (for GitHub release JSON parsing)
