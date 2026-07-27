---
inclusion: fileMatch
fileMatchPattern: "Build/**"
---

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

`Build/Installer/UpdateVersion.ps1` (called by `Build/scripts/build-installer.ps1`) updates:

| File                                                           | Field                  | Format                  |
| -------------------------------------------------------------- | ---------------------- | ----------------------- |
| `Build/Installer/Helpers/AddinInstaller.cs`         | `const string Version` | `"vX.Y.Z"`              |
| `Build/Installer/KleiKodeshVstoInstallerWpf.csproj` | `<Version>`            | `X.Y.Z` (no `v` prefix) |

The NSIS script (`Build/nsis/KleiKodeshWrapper.nsi`) receives `${PRODUCT_VERSION}` as a command-line define from `build-installer.ps1` — it does **not** need to be edited manually.

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
| `--wait-for-pid <PID>`   | Start hidden; show the window once the given process exits (max 30s — PIDs get recycled). Passed by `RunPendingInstaller()` so the wizard appears only after Word/the app has fully closed. |

### Full Update Install Flow

```
Word taskpane / KitveiHakodesh app opens
  → UpdateChecker Step 1 (sync, disk-only): %TEMP%\KleiKodeshSetup.exe newer than
    registry? → arm RunPendingInstaller() + show "עדכון זמין" notification
  → UpdateChecker Step 2 (async): GitHub latest newer than registry? → silent
    download to %TEMP%\KleiKodeshSetup.exe.partial → validated against
    Content-Length → renamed to KleiKodeshSetup.exe (atomic; a killed process
    never leaves a half-written KleiKodeshSetup.exe). If the on-disk file already
    claims the latest version, its byte size is verified against the GitHub
    release asset size — a mismatch (truncated pre-.partial download) forces a
    fresh download.
  → User closes Word/app → RunPendingInstaller() launches the NSIS wrapper
    unelevated with "--wait-for-pid <pid>" (no --silent — the wizard is shown)
  → NSIS checks .NET/VSTO prereqs, passes the args through to the WPF installer
  → WPF installer starts hidden, waits (max 30s) for the pid to exit, then shows
    the landing page
  → Extracts VSTO zip, registers addin, SaveVersion() → exits 0
  → NSIS writes/overwrites HKCU\...\Uninstall\KleiKodesh\DisplayVersion = "vX.Y.Z"
```

A truncated NSIS exe still reports its full `ProductVersion` (the version resource
lives in the stub at the start of the file) — this is why the `.partial` rename and
the asset-size check exist. Do not remove either: without them one interrupted
download wedges the updater into announcing an update on every launch and failing
to install on every close, forever ("already downloaded" skip blocks the repair).

**No duplicate in Windows Installed Apps** — the uninstall key name is always the fixed string `KleiKodesh`, so `WriteRegStr` overwrites in-place on every install/update.

### UAC / Elevation Policy

- **Normal install** (`InstallPage`): writes to `%LocalAppData%` and `HKCU` only — no elevation needed.
- **Repair/cleanup** (`RepairPage` → `FullSystemCleaner`): also targets `HKLM` for old-version leftovers, but those calls are wrapped in `catch (UnauthorizedAccessException)` and skipped gracefully if not elevated. The UI shows a blue info banner when not elevated with a "הפעל כמנהל 🛡" button.
- **Elevate button**: calls `AdminHelper.RelaunchAsAdmin("--repair")` — relaunches the same exe with `runas` (UAC prompt) and exits the current instance. The elevated instance receives `--repair`, navigates straight to `RepairPage` with `autoRun: true`, and skips the confirm dialog since the user already confirmed by clicking the button.
- **`--repair` arg**: handled in `App.xaml.cs` → calls `MainWindow.NavigateToRepairOnLoad()` which opens `RepairPage(autoRun: true)` directly, bypassing the landing page entirely.
- **WPF installer manifest**: `asInvoker` — correct, never forces UAC.
- **NSIS wrapper**: `RequestExecutionLevel user` — **MUST stay `user`** (this is a per-user install: `%LOCALAPPDATA%` + HKCU). It was briefly `admin` (June–July 2026, for the uninstaller's `sc stop/delete DocumentLocatorSvc`) and that broke auto-update: a surprise UAC prompt at Word/app close, silently swallowed when declined or policy-denied, and — worse — a standard user approving with admin credentials installed into the *admin's* profile, so the real user never updated. The uninstaller now elevates just its two `sc` commands through one `ExecShellWait "runas" cmd.exe` call (skipped when the service isn't registered); the installer side self-elevates only `DocumentLocator.Service.exe --install` (see `DocumentLocatorHelper.EnsureServiceInstalledAsync`).
- **`DownloadManager.LaunchInstaller`**: **MUST NOT use `Verb = "runas"`** — the `runas` verb forces a UAC consent prompt regardless of the target exe's manifest (an earlier note here claimed it was a promptless AIS handoff; that is wrong — AIS *is* the elevation path). A declined/denied prompt surfaced as Win32 error 1223, which is deliberately swallowed, so updates silently never ran. Child processes survive parent exit on Windows; no handoff trick is needed for the installer to outlive Word/the app, and `--wait-for-pid` makes the installer wait for the host to fully exit before showing UI.

If a user wants to clean HKLM leftovers from very old versions, they can run the WPF installer manually as administrator — the repair page will then have full access.

`KleiKodeshVsto/Resources/UpdateKleiKodesh.ps1` is a **legacy script** — superseded by `UpdateCheckerLib`. Do not rely on it.

## What Is NOT Synced (intentionally)

- `KleiKodeshVsto/Properties/AssemblyInfo.cs` — uses its own internal VSTO assembly version (`1.0.87.10` style). This is a separate build counter unrelated to the app semver. Do not sync it.
- All other `AssemblyInfo.cs` files in sub-libraries — library versions, not the app version.

## Adding a New Version Target

Add it to the `Update-AllVersionTargets` function in `UpdateVersion.ps1`. Do NOT add ad-hoc version strings elsewhere.

## Version Format

- **Component count outranks the numbers** (`UpdateChecker.CompareVersions`): a version with more parts is always newer — `v0.2.3.4` > `v1.2.3` and > `v12.345.456`. Only versions with the same number of parts compare numerically. This lets the scheme move to four-part numbers and restart low without any installed three-part version blocking the update. ⚠ One-way ratchet: once a four-part release is published, three-part tags will forever look older to installed clients.
- App version: `vMAJOR.MINOR.PATCH` (semver with `v` prefix, e.g. `v3.4.0`)
- csproj `<Version>`: `MAJOR.MINOR.PATCH` (no `v` prefix, e.g. `3.4.0`)
- GitHub release tag: same as app version (`v3.4.0`)
- Registry `Version` value: same as app version (`v3.4.0`)
- NSIS `DisplayVersion`: same as app version (`v3.4.0`)

## Build Script Regex

`Build/scripts/build-installer.ps1` reads the version back after `UpdateVersion.ps1` runs using the `Get-CurrentVersion` function from `build-helpers.ps1`:

```powershell
Select-String -Path $AddinInstallerPath -Pattern 'const string Version\s*=\s*"([^"]+)"'
```

The `\s*=\s*` handles the aligned spacing in `AddinInstaller.cs`.
