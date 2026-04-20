# KleiKodeshVstoInstallerWpf — WPF Installer (Main App)

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
SettingsPage  ○●○  Ribbon components + default button
    ↓ הבא
    ├─ Kezayit OR WebSites checked → AdvancedPage  ○○●
    │     Kezayit DB picker  (hidden if Kezayit unchecked)
    │     Websites list      (hidden if WebSites unchecked)
    │       └─ "ערוך רשימת אתרים" → WhitelistEditorDialog (modal)
    │     ↓ התקן
    └─ both unchecked → skip AdvancedPage
    ↓
InstallPage   Extraction + registration + version stamp
```

Silent/update mode (`--silent` / `--install`) skips straight to InstallPage.

## Folder Structure

```
KleiKodeshVstoInstallerWpf/
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
│   ├── AdvancedPage.xaml(.cs)    — Step 3: Kezayit DB + website whitelist (conditional)
│   ├── InstallPage.xaml(.cs)     — Extraction + registration progress
│   └── RepairPage.xaml(.cs)      — Repair / uninstall flow
└── Dialogs/
    └── WhitelistEditorDialog.xaml(.cs)  — Modal editor for the website list
```

## Website Whitelist

The default website list is the **single source of truth** at:
```
WebSitesLib/WebSitesLib2/WebSitesWhitelist.json
```
It is embedded into the installer exe as a resource (linked path in csproj, not a copy).

### Extraction rules

| Condition | What happens to `WebSitesWhitelist.json` |
|---|---|
| Fresh install, user did not edit | Extracted from zip (default list) |
| Update (file already exists), user did not edit | **Skipped** — user's list is preserved |
| User edited via dialog (any scenario) | Zip entry skipped; `ApplyPendingWhitelist()` writes the edited version |

`AddinInstaller.PendingWhitelist` is `null` until the user opens the dialog and clicks OK.
`ApplyPendingWhitelist()` is a no-op when `PendingWhitelist` is null.

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
