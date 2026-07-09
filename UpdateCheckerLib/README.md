# UpdateCheckerLib

Library for checking and downloading app updates from GitHub releases.

## What It Does

Monitors GitHub releases for new versions of KleiKodesh and automatically downloads and installs them. The update flow is split into two completely independent concerns:

1. **Sync disk check** — instant, no network. Reads the version embedded in a previously downloaded installer file and compares it to the installed version.
2. **Async GitHub check** — background network call. Downloads a newer installer silently if one is available.

This separation means the user notification is never blocked by a network request, and there are no threading concerns around showing the dialog.

## Files

- **UpdateChecker.cs** — Public API. `GetReadyUpdateVersion()` (sync disk check) and `CheckForUpdateAsync()` (background download).
- **DownloadManager.cs** — Internal. Handles file version reading, downloading, cleanup, and launching the installer on close.
- **UpdateNotificationForm.cs** — Minimal topmost WinForms dialog. `TopMost = true` ensures the user never misses it behind other windows.
- **DownloadProgressForm.cs** — WinForms progress form (used if a visible download is ever needed in future).
- **GithubRelease.cs** — Data model for GitHub release JSON (version tag, assets, etc.).
- **UpdateException.cs** — Structured exception for download/check failures.

## Update Flow

### Session startup (VSTO: first task pane open / Demo app: form load)

```
Step 1 — sync, on calling thread, instant:
  GetReadyUpdateVersion()
    no file in %TEMP%          → null
    file version <= registry   → delete file (clean up) → null
    file version > registry    → set PendingInstallerPath → return version

  version returned → UpdateNotificationForm.Show(...)   ← topmost dialog

Step 2 — async, fire-and-forget Task.Run, always runs:
  CheckForUpdateAsync()
    hit GitHub API
    not newer than registry    → done
    file already that version  → done (skip re-download)
    download to %TEMP%\KleiKodeshSetup.exe
    (no UI, no state changes)
```

### On close (Word shutdown / form closing)

```
RunPendingInstaller()
  PendingInstallerPath set? → launch installer with runas verb → done
  not set?                  → nothing
```

### Version embedded in the NSIS exe

The NSIS build script passes `VIProductVersion` and `VIAddVersionKey` directives so the downloaded `KleiKodeshSetup.exe` carries its version in the PE header. `FileVersionInfo.GetVersionInfo(path).ProductVersion` returns e.g. `"v8.6.0"` — the same format used everywhere.

### Cleanup

`GetReadyUpdateVersion()` deletes `%TEMP%\KleiKodeshSetup.exe` when its version equals or is older than the installed version. This means:
- After a successful install, the next session silently cleans up the file.
- Stale files from failed/partial downloads are also cleaned up automatically.

## Integration

| Caller | Where called | Thread |
|---|---|---|
| `KleiKodeshVsto/Helpers/TaskpaneManager.cs` | First task pane open | VSTO UI thread |
| `KitveiHakodeshDemoApp/MainForm.cs` | `MainForm_Load` | WinForms UI thread |

Both callers own the notification message text (Hebrew, context-appropriate). The library provides no hardcoded message strings.

## Folder Structure

```
UpdateCheckerLib/
├── UpdateChecker.cs           — Public API: GetReadyUpdateVersion, CheckForUpdateAsync
├── DownloadManager.cs         — Internal: file version, download, cleanup, launch
├── UpdateNotificationForm.cs  — Topmost "update ready" dialog (code-only, no designer)
├── DownloadProgressForm.cs    — Progress UI (for visible downloads if needed)
├── GithubRelease.cs           — GitHub API response model
├── UpdateException.cs         — Structured exception types
├── UpdateCheckerLib.csproj
└── packages.config
```

## Build Configuration

Part of the three-variant build pipeline (x64, x86, AnyCPU). See `.kiro/steering/build-variants.md`.

For full version management details, see `.kiro/steering/version-management.md`.
