# Version Management

## Single Source of Truth

The app version lives in **one place only**:

```
Build/Installer/Helpers/AddinInstaller.cs
```

```csharp
public const string Version         = "v3.4.0";
```

All other version stamps are derived from this value by `UpdateVersion.ps1` during the build.

## What Gets Updated on Every Release Build

`Build/Installer/UpdateVersion.ps1` (called by `Build/build-installer.ps1`) updates:

| File                                                           | Field                  | Format                  |
| -------------------------------------------------------------- | ---------------------- | ----------------------- |
| `Build/Installer/Helpers/AddinInstaller.cs`         | `const string Version` | `"vX.Y.Z"`              |
| `Build/Installer/KleiKodeshVstoInstallerWpf.csproj` | `<Version>`            | `X.Y.Z` (no `v` prefix) |

The NSIS script (`Build/KleiKodeshWrapper.nsi`) receives `${PRODUCT_VERSION}` as a command-line define from `build-installer.ps1` — it does **not** need to be edited manually.

## Version Flow at Runtime

```
Build → AddinInstaller.cs (const Version)
      → SaveVersion() writes HKCU\SOFTWARE\KleiKodesh → Version = "vX.Y.Z"
      → UpdateChecker.GetCurrentVersionFromRegistry() reads it
      → Compared against GitHub latest release tag
      → NSIS writes DisplayVersion to HKCU\...\Uninstall\KleiKodesh (Windows Installed Apps)
```

## Registry Locations Written by the Installer

| Key                                                                   | Value                                     | Written by                                       |
| --------------------------------------------------------------------- | ----------------------------------------- | ------------------------------------------------ |
| `HKCU\SOFTWARE\KleiKodesh`                                            | `Version = "vX.Y.Z"`                      | `AddinInstaller.SaveVersion()`                   |
| `HKCU\Software\Microsoft\Office\Word\Addins\KleiKodesh`               | `Manifest`, `FriendlyName`, etc.          | `AddinInstaller.RegisterAddInAsync()`            |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh` | `DisplayVersion`, `UninstallString`, etc. | NSIS wrapper (post-install)                      |
| `HKCU\SOFTWARE\Microsoft\VSTO\Security\Inclusion\{base64}`            | `Url`, `PublicKey`                        | `AddinInstaller.AddToOfficeInclusionListAsync()` |
| `HKCU\SOFTWARE\Microsoft\VSTO\Security\TrustedPaths\{base64}`         | `Path`                                    | `AddinInstaller.AddFolderToTrustedLocations()`   |

## Update Checker

`UpdateCheckerLib/UpdateChecker.cs` is the active updater:

- Reads current version from `HKCU\SOFTWARE\KleiKodesh\Version`
- Fetches latest from `https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest`
- Triggered from `TaskPaneManager` on first taskpane open (unless user disabled it)
- Downloads `KleiKodeshSetup-vX.Y.Z.exe` (NSIS wrapper) to `%TEMP%\KleiKodeshSetup.exe`
- Schedules it to run on Word shutdown via `RunPendingInstaller()`

### WPF Installer CLI Args

| Arg                      | Effect                                                                                                                                |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------- |
| _(none)_                 | Normal UI — landing page                                                                                                              |
| `--silent` / `--install` | Skip straight to install progress page, exit 0 on success / 1 on failure. Used by auto-updater and NSIS `--silent` passthrough.       |
| `--repair`               | Skip straight to repair page with auto-run (no confirm dialog). Used when relaunching as admin from the repair page elevation banner. |

### Full Update Install Flow

```
Word taskpane opens
  → UpdateChecker detects newer GitHub release
  → User confirms → DownloadManager downloads KleiKodeshSetup-vX.Y.Z.exe to %TEMP%
  → User closes Word → RunPendingInstaller() launches NSIS wrapper with "--silent"
  → NSIS checks .NET/VSTO prereqs, passes "--silent" through to WPF installer
  → WPF installer starts with --silent → straight to InstallPage (no landing UI)
  → Extracts VSTO zip, registers addin, SaveVersion() → exits 0
  → NSIS writes/overwrites HKCU\...\Uninstall\KleiKodesh\DisplayVersion = "vX.Y.Z"
```

**No duplicate in Windows Installed Apps** — the uninstall key name is always the fixed string `KleiKodesh`, so `WriteRegStr` overwrites in-place on every install/update.

### UAC / Elevation Policy

- **Normal install** (`InstallPage`): writes to `%LocalAppData%` and `HKCU` only — no elevation needed.
- **Repair/cleanup** (`RepairPage` → `FullSystemCleaner`): also targets `HKLM` for old-version leftovers, but those calls are wrapped in `catch (UnauthorizedAccessException)` and skipped gracefully if not elevated. The UI shows a blue info banner when not elevated with a "הפעל כמנהל 🛡" button.
- **Elevate button**: calls `AdminHelper.RelaunchAsAdmin("--repair")` — relaunches the same exe with `runas` (UAC prompt) and exits the current instance. The elevated instance receives `--repair`, navigates straight to `RepairPage` with `autoRun: true`, and skips the confirm dialog since the user already confirmed by clicking the button.
- **`--repair` arg**: handled in `App.xaml.cs` → calls `MainWindow.NavigateToRepairOnLoad()` which opens `RepairPage(autoRun: true)` directly, bypassing the landing page entirely.
- **WPF installer manifest**: `asInvoker` — correct, never forces UAC.
- **NSIS wrapper**: `RequestExecutionLevel user` — correct, never forces UAC.
- **`DownloadManager.LaunchInstaller`**: **MUST use `Verb = "runas"`** — this is not for elevation, but because `runas` hands off to the Windows Application Information Service (AIS) which runs as a separate system service outside Word's process. Without `runas`, `ShellExecuteEx` runs in-process and gets killed when Word shuts down before `Process.Start` returns. The NSIS wrapper has `RequestExecutionLevel user` so no UAC prompt appears, but the AIS handoff is what makes the launch survive Word's shutdown.

If a user wants to clean HKLM leftovers from very old versions, they can run the WPF installer manually as administrator — the repair page will then have full access.

`KleiKodeshVsto/Resources/UpdateKleiKodesh.ps1` is a **legacy script** — superseded by `UpdateCheckerLib`. Do not rely on it.

## What Is NOT Synced (intentionally)

- `KleiKodeshVsto/Properties/AssemblyInfo.cs` — uses its own internal VSTO assembly version (`1.0.87.10` style). This is a separate build counter unrelated to the app semver. Do not sync it.
- All other `AssemblyInfo.cs` files in sub-libraries — library versions, not the app version.

## Adding a New Version Target

Add it to the `Update-AllVersionTargets` function in `UpdateVersion.ps1`. Do NOT add ad-hoc version strings elsewhere.

## Version Format

- App version: `vMAJOR.MINOR.PATCH` (semver with `v` prefix, e.g. `v3.4.0`)
- csproj `<Version>`: `MAJOR.MINOR.PATCH` (no `v` prefix, e.g. `3.4.0`)
- GitHub release tag: same as app version (`v3.4.0`)
- Registry `Version` value: same as app version (`v3.4.0`)
- NSIS `DisplayVersion`: same as app version (`v3.4.0`)

## Build Script Regex

`build-installer.ps1` reads the version back after `UpdateVersion.ps1` runs using:

```powershell
Select-String -Path $progressWindowPath -Pattern 'const string Version\s*=\s*"([^"]+)"'
```

The `\s*=\s*` handles the aligned spacing in `AddinInstaller.cs`.
