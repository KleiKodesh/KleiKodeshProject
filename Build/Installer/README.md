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
    ├─ KitveiHakodesh OR WebSites checked → AdvancedPage  ○○○
    │     KitveiHakodesh DB picker  (hidden if KitveiHakodesh unchecked)
    │     Websites list      (hidden if WebSites unchecked)
    │       └─ "ערוך רשימת אתרים" → WhitelistEditorDialog (modal)
    │     ↓ סיום
    └─ both unchecked → skip AdvancedPage
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
├── UpdateVersion.ps1             — Bumps Version constant + csproj <Version>
├── Helpers/
│   ├── AddinInstaller.cs         — Extract, register, whitelist, version; holds Version const
│   ├── SettingsManager.cs        — Registry-backed settings (ribbon visibility etc.)
│   ├── TaskpaneManager.cs        — (shared with VSTO) taskpane lifecycle
│   └── WordHelper.cs             — Detect / close Word before install
├── Pages/
│   ├── LandingPage.xaml(.cs)     — Step 1: welcome
│   ├── SettingsPage.xaml(.cs)    — Step 2: ribbon settings
│   ├── AdvancedPage.xaml(.cs)    — Step 3: KitveiHakodesh DB + website whitelist (conditional)
│   ├── InstallPage.xaml(.cs)     — Extraction + registration progress
│   └── RepairPage.xaml(.cs)      — Repair / uninstall flow
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

| Condition | What happens to `WebSitesWhitelist.json` |
|---|---|
| User never opened the editor dialog, fresh install | Extracted from zip (default list) |
| User never opened the editor dialog, update (file exists) | **Skipped** — existing file preserved |
| User opened dialog and clicked OK | Zip entry skipped; `ApplyPendingWhitelist()` writes only the checked entries (no `IsVisible` field) |
| User opened dialog and clicked Cancel | Same as "never opened" — `PendingWhitelist` stays null |

`AddinInstaller.PendingWhitelist` is `null` until the user opens the dialog and clicks OK.
`ApplyPendingWhitelist()` is a no-op when `PendingWhitelist` is null.

### How the whitelist works end-to-end

1. The source JSON (`WebSitesWhitelist.json`) contains all entries with `IsVisible` flags — the full catalogue shown in the editor dialog.
2. When the user opens the dialog, the full catalogue is loaded. Each entry's checkbox is pre-set from the user's currently installed file: entries present in the installed file are checked, entries absent are unchecked. On a fresh install (no installed file), the default `IsVisible` values are used.
3. On OK, `SerializeWhitelistJson` writes **only the checked entries** to `PendingWhitelist`, with no `IsVisible` field in the output.
4. The installed JSON therefore contains only the entries the user wanted — no filtering needed at runtime.
5. The VSTO add-in loads the file and shows every entry in it directly.

### Do not
- Add `System.Text.Json` or `System.Web.Extensions` to this project — the embedded-DLL resolver cannot find them at the point the whitelist page loads. The parser/serializer in `AdvancedPage.xaml.cs` is intentionally hand-rolled.
- Call `ApplyPendingWhitelist()` before `ExtractAsync` — the install folder may not exist yet.

## Version Management

The version constant lives in `Helpers/AddinInstaller.cs`:

```csharp
public const string Version = "v3.6.1";
```

`UpdateVersion.ps1` (called by `Build/build-installer.ps1`) syncs this value to the csproj `<Version>` tag. Do not edit the version anywhere else — see `version-management.md` steering file.

## Registry Keys Written

| Key | Value | Purpose |
|---|---|---|
| `HKCU\SOFTWARE\KleiKodesh` | `Version` | App version for update/index checks |
| `HKCU\Software\Microsoft\Office\Word\Addins\KleiKodesh` | `Manifest`, `LoadBehavior=3` | Registers add-in with Word |
| `HKCU\Software\Microsoft\Office\Word\AddinsData\KleiKodesh` | Trust data | Office trusted list |
| `HKCU\SOFTWARE\Microsoft\VSTO\Security\Inclusion\{base64}` | `Url`, `PublicKey` | VSTO trust |
| `HKCU\SOFTWARE\Microsoft\VSTO\Security\TrustedPaths\{base64}` | `Path` | Trusted folder |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh` | `DisplayVersion` etc. | Windows Installed Apps (written by NSIS wrapper) |
