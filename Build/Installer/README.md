# Build/Installer — WPF Installer (Main App)

This is the **main application** that end users download and run. It packages the VSTO add-in as an embedded zip resource, extracts it to `%LocalAppData%\KleiKodesh`, and registers it with Word.

## What It Does

1. Extracts `KleiKodesh.zip` (embedded resource) to `%LocalAppData%\KleiKodesh`.
2. Registers the add-in manifest in the Word registry key so Word loads it on startup.
3. Adds the manifest to the Office trusted locations list.
4. Writes the current app version to `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh` → `Version`.
5. Supports repair (re-extract) and uninstall (remove files + registry entries).

## Wizard Flow

```
LandingPage  ●○○   Welcome screen
    ↓ הבא
InstallPage   ○●○  Extraction + registration + version stamp
    ↓
SettingsPage  ○○●  Ribbon components + default button
    ↓ הבא
    ├─ KitveiHakodesh OR WebSites checked → ComponentSettingsPage  ○○○
    │     KitveiHakodesh DB picker  (hidden if KitveiHakodesh unchecked)
    │     Websites list      (hidden if WebSites unchecked)
    │       └─ "ערוך רשימת אתרים" → WhitelistEditorDialog (modal)
    │     ↓ סיום
    └─ both unchecked → skip ComponentSettingsPage
    ↓
Exit
```

Silent/update mode (`--silent` / `--install`) skips straight to InstallPage and exits.

Repair mode (`--repair`) cleans up old files/registry, then installs and proceeds to settings.

## Folder Structure

```
Build/Installer/
├── App.xaml / .cs                — Entry point; assembly resolver; CLI arg handling
├── MainWindow.xaml / .cs         — Shell window; page navigation methods
├── KleiKodesh.zip                — Embedded VSTO package (built by pre-build target)
├── UpdateVersion.ps1             — Bumps Version constant + csproj <Version> + DemoApp AssemblyInfo.cs
├── Helpers/
│   ├── AddinInstaller.cs              — Extract, register, whitelist, version; holds Version const
│   ├── AdminHelper.cs                 — UAC elevation & re-launch
│   ├── DocumentLocatorHelper.cs       — Service shutdown/restart for file-locking
│   ├── FullSystemCleaner.cs           — Full uninstall cleanup
│   ├── KitveiHakodeshHelper.cs        — KitveiHakodesh DB path handling
│   ├── SettingsManager.cs             — Registry-backed settings (ribbon visibility etc.)
│   ├── ShellRegistrationHelper.cs     — Shell integration / file associations
│   └── WordHelper.cs                  — Detect / close Word before install
├── Pages/
│   ├── LandingPage.xaml(.cs)     — Step 1: welcome
│   ├── SettingsPage.xaml(.cs)    — Step 2: ribbon settings
│   ├── ComponentSettingsPage.xaml(.cs) — Step 3: KitveiHakodesh DB + website whitelist (conditional)
│   ├── InstallPage.xaml(.cs)     — Extraction + registration progress
│   └── RepairPage.xaml(.cs)      — Repair / uninstall flow
├── Models/
│   └── WhitelistEntry.cs             — Whitelist data model
├── Resources/
│   └── InstallerStyles.xaml          — XAML styles
├── app.manifest                      — Application manifest (asInvoker)
├── AssemblyInfo.cs                   — Assembly metadata
├── InstallProgressWindow.xaml.cs     — Code-behind for install progress
├── KleiKodeshVstoInstallerWpf.csproj — Project file (three-variant output paths)
├── KleiKodesh_Main.ico               — Installer icon
└── Dialogs/
    └── WhitelistEditorDialog.xaml(.cs)  — Modal editor for the website list
```

## Website Whitelist

The default website list is the **single source of truth** at:
```
KleiKodeshVsto/WebSitesLib/WebSitesLib/WebSitesWhitelist.json
```
It is embedded into the installer exe as a resource (linked path in csproj, not a copy).

### Extraction rules

| File/Folder | Fresh Install | Update | Reason |
|---|---|---|---|
| `WebSitesWhitelist.json` | Extracted from zip (default list) | **Skipped** — existing file preserved | User's website customization |
| `KitveiHakodesh/cache/word/` | Extracted (empty) | **Skipped** — existing files preserved | User's cached Word→PDF conversions |
| `KitveiHakodesh/cache/hebrewbooks/` | Extracted (empty) | **Skipped** — existing files preserved | User's cached HebrewBooks downloads |
| `BloomFilters/` | Extracted (empty) | **Skipped** — existing files preserved | Search index (rebuilt on version mismatch) |
| All other files | Extracted | Extracted (overwritten) | App code and resources |

**Cache preservation logic:** `AddinInstaller.ShouldSkipOnUpdate()` checks if a file exists on disk before extraction. If it does, the file is skipped. This preserves user data and caches across installer updates while still allowing fresh installs to extract the default/empty folders.

`AddinInstaller.PendingWhitelist` is `null` until the user opens the dialog and clicks OK.
`ApplyPendingWhitelist()` is a no-op when `PendingWhitelist` is null.

### How the whitelist works end-to-end

1. The source JSON (`WebSitesWhitelist.json`) contains all entries with `IsVisible` flags — the full catalogue shown in the editor dialog.
2. When the user opens the dialog, the full catalogue is loaded. Each entry's checkbox is pre-set from the user's currently installed file: entries present in the installed file are checked, entries absent are unchecked. On a fresh install (no installed file), the default `IsVisible` values are used.
3. On OK, `SerializeWhitelistJson` writes **only the checked entries** to `PendingWhitelist`, with no `IsVisible` field in the output.
4. The installed JSON therefore contains only the entries the user wanted — no filtering needed at runtime.
5. The VSTO add-in loads the file and shows every entry in it directly.

### Do not
- Add `System.Text.Json` or `System.Web.Extensions` to this project — the embedded-DLL resolver cannot find them at the point the whitelist page loads. The parser/serializer in `ComponentSettingsPage.xaml.cs` is intentionally hand-rolled.
- Call `ApplyPendingWhitelist()` before `ExtractAsync` — the install folder may not exist yet.

## Version Management

The version constant lives in `Helpers/AddinInstaller.cs`:

```csharp
public const string Version = "v8.2.1";
```

`UpdateVersion.ps1` (called by `Build/build-installer.ps1`) syncs this value to the csproj `<Version>` tag. Do not edit the version anywhere else — see `version-management.md` steering file.

## URL Protocol Registration (`kitveihakodeshapp://`)

The app copies deep links to its own content (`kitveihakodeshapp://book/<bookId>?index=<lineIndex>`,
defined in `KitveiHakodesh/vue-frontend/src/utils/appDeepLink.ts`, parsed by
`KitveiHakodeshLib/HostLink.cs`) and can already open them. The installer's part is the registry
entry that tells Windows which exe to launch when such a link is clicked — in Word, a browser,
or Explorer. The notes below are kept because each one is a trap that is easy to fall back into.

### What the app already does — do not re-implement any of it

| Piece | Where | Behaviour |
|---|---|---|
| Argument | `KitveiHakodeshDemoApp/Program.cs` → `GetOpenRequestArgument()` | `argv[1]` is accepted as *either* an existing file path or a link `HostLink.TryParse` accepts. Flags (`--plain`) are ignored. |
| Single instance | same file: `MutexName` / `PipeName` | A second launch does **not** open a second window: it writes `argv[1]` to the running instance's pipe and exits. |
| Routing | `MainForm.OpenRequest(string)` | Link → `AppViewer.OpenBookFromHost`; file → `AppViewer.OpenFileFromPath`. Both queue until Vue posts `appReady`, and both end up as a **new tab** in the existing window (`hostSearchStore.handleHostOpenBook`). |

So a link is handled exactly like a double-clicked file, and the installer's whole job is to
make Windows hand us the URL as `argv[1]`.

### The keys to write

Per-user, `HKCU\Software\Classes` only — same model as `ShellRegistrationHelper`. Never HKLM,
never elevate.

```
HKCU\Software\Classes\kitveihakodeshapp
    (Default)               = "URL:<FriendlyName>"        REG_SZ
    URL Protocol            = ""                          REG_SZ  (empty — this is what makes it a protocol)
    DefaultIcon\(Default)   = "<exe>,0"                   REG_SZ
    shell\open\command\(Default) = "\"<exe>\" \"%1\""      REG_SZ
```

`<exe>` is `Path.Combine(AddinInstaller.InstallPath, ExeName)` — `ExeName` is a `private const`
of `ShellRegistrationHelper`, which is one more reason the registration belongs in that class. I.e.
`%LocalAppData%\KleiKodesh\<app exe>`. Take the name from that existing constant rather than
retyping it — the exe name is Hebrew and easy to get wrong.

Rules that are easy to get wrong:

- `URL Protocol` **must exist and be an empty string**. Without it the key is just a ProgId and
  Windows will not route the scheme to it.
- Quote both halves of the command: `"<exe>" "%1"`. The exe name contains a space, and an
  quoted `%1` is what keeps the whole URL one argv element whatever characters it grows
  (the shell percent-encodes a protocol URL, so a literal space never reaches us — the quoting
  is insurance against the next character that would split it).
- Use `CreateSubKey`, so re-running the installer overwrites the path — an update that moves the
  install folder must not leave the old command behind.
- No `SHChangeNotify` is needed for a protocol (it is resolved per launch, not cached like
  file associations). Calling the existing `NotifyShell()` anyway is harmless.

### Where it lives

`ShellRegistrationHelper.RegisterProtocol()`, called from `Helpers/InstallRunner.cs` right after
`AddinInstaller.CreateKitveiHakodeshShortcut()`. It sits in `ShellRegistrationHelper` because that
class already owns `HKCU\Software\Classes` and holds the exe-name constant.

Registered **unconditionally**, not behind the "Open with" checkbox on `ComponentSettingsPage`.
That checkbox is about claiming *other* applications' file types, which is a preference; opening
our own links is the app's own function, and a user who has no idea the scheme exists cannot be
expected to opt in to it. There is deliberately no `UnregisterProtocol`: nothing in the UI can
turn the scheme off, so uninstall is its only remover.

### Uninstall

Two removers, because there are two paths that clean up. A key left behind points the scheme at a
deleted exe, which fails silently.

- `Build/nsis/KleiKodeshWrapper.nsi`, `Section Uninstall` — the real uninstall, next to the
  existing `Applications\<exe>` cleanup:

  ```nsis
  DeleteRegKey HKCU "Software\Classes\kitveihakodeshapp"
  ```

- `Helpers/FullSystemCleaner.cs` — the Repair page's cleanup, alongside the `SOFTWARE\KleiKodesh`
  removal and *outside* the `deepClean` guard, so a normal clean removes it too:

  ```csharp
  DeleteRegistrySubtree(Registry.CurrentUser, @"Software\Classes\kitveihakodeshapp", result, log);
  ```

### Do NOT register `seforimapp://`

`HostLink.LegacyAppScheme` is still *parsed* so links copied before the scheme was renamed keep
opening, but that scheme belongs to a different application (`io.github.kdroidfilter.seforimapp`).
Registering it would hijack that app's links.

### If the *write* ever moves to the NSIS wrapper too

Only the delete lives in NSIS today. The equivalent write, for reference:

```nsis
WriteRegStr HKCU "Software\Classes\kitveihakodeshapp" "" "URL:$(^Name)"
WriteRegStr HKCU "Software\Classes\kitveihakodeshapp" "URL Protocol" ""
WriteRegStr HKCU "Software\Classes\kitveihakodeshapp\DefaultIcon" "" "$INSTDIR\<app exe>,0"
WriteRegStr HKCU "Software\Classes\kitveihakodeshapp\shell\open\command" "" '"$INSTDIR\<app exe>" "%1"'
```

The WPF installer is the better home for the write: it already knows `InstallPath` and the exe
name, and it re-runs on every update, so a moved install folder is corrected there.

### Verifying

1. `reg query HKCU\Software\Classes\kitveihakodeshapp /s` — four values as above.
2. App **closed**: `start "" "kitveihakodeshapp://book/5?index=3"` → the app launches and opens
   book 5 scrolled to line 3 (with the open-line flash).
3. App **open**: same command → no second **window**, a new tab in the existing one. A second
   process does start; it finds the mutex taken, forwards over the pipe and exits, so the
   invariant to watch is the window count, not the process list. This is the single-instance
   path and the one worth actually testing.
4. Minimized app: same command → the window restores, then the tab opens.
5. `index` is a 0-based positional line index, not a row id, so pick a `bookId` whose book has
   more lines than the index you test with.

## Registry Keys Written

| Key | Value | Purpose |
|---|---|---|
| `HKCU\SOFTWARE\KleiKodesh` | `Version` | App version for update/index checks |
| `HKCU\Software\Microsoft\Office\Word\Addins\KleiKodesh` | `Manifest`, `LoadBehavior=3` | Registers add-in with Word |
| `HKCU\Software\Microsoft\Office\Word\AddinsData\KleiKodesh` | Trust data | Office trusted list |
| `HKCU\SOFTWARE\Microsoft\VSTO\Security\Inclusion\{base64}` | `Url`, `PublicKey` | VSTO trust |
| `HKCU\SOFTWARE\Microsoft\VSTO\Security\TrustedPaths\{base64}` | `Path` | Trusted folder |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh` | `DisplayVersion` etc. | Windows Installed Apps (written by NSIS wrapper) |
| `HKCU\Software\Classes\kitveihakodeshapp` | `URL Protocol`, `shell\open\command` | Opens `kitveihakodeshapp://` deep links in the app (see above) |
