# KleiKodeshVstoInstallerWpf — WPF Installer (Main App)

This is the **main application** that end users download and run. It packages the VSTO add-in as an embedded zip resource, extracts it to `%LocalAppData%\KleiKodesh`, and registers it with Word.

## What It Does

1. Extracts `KleiKodesh.zip` (embedded resource) to `%LocalAppData%\KleiKodesh`.
2. Registers the add-in manifest in the Word registry key so Word loads it on startup.
3. Adds the manifest to the Office trusted locations list.
4. Writes the current app version to `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh` → `Version`.
5. Supports repair (re-extract) and uninstall (remove files + registry entries).

## Folder Structure

```
KleiKodeshVstoInstallerWpf/
├── MainWindow.xaml / .cs         — Shell window; navigates between pages
├── InstallProgressWindow.xaml.cs — Progress overlay shown during extraction
├── App.xaml / .cs                — Application entry point
├── KleiKodesh.zip                — Embedded VSTO package (the add-in + all dependencies)
├── UpdateVersion.ps1             — Dev script: bumps version constant and rebuilds
├── Helpers/
│   ├── AddinInstaller.cs         — Core logic: extract, register, uninstall; holds Version constant
│   ├── RegistryHelper.cs         — Registry read/write wrappers
│   └── ...
├── Pages/
│   ├── LandingPage.xaml          — Welcome / first-run screen
│   ├── InstallPage.xaml          — Install / repair flow
│   ├── SettingsPage.xaml         — Post-install settings
│   └── RepairPage.xaml           — Repair / uninstall flow
└── Resources/                    — Icons, images, WPF resource dictionaries
```

## Version Management

The version constant lives in `Helpers/AddinInstaller.cs`:

```csharp
public const string Version = "v3.4.0";
```

To bump the version, run `UpdateVersion.ps1` or edit the constant directly. After install the version is written to the registry and used by `BloomSearchEngineLib` to detect whether the Bloom filter index needs rebuilding.

## Registry Keys Written

| Key                                                         | Value                        | Purpose                             |
| ----------------------------------------------------------- | ---------------------------- | ----------------------------------- |
| `HKCU\SOFTWARE\KleiKodesh`                                  | `Version`                    | App version for update/index checks |
| `HKCU\Software\Microsoft\Office\Word\Addins\KleiKodesh`     | `Manifest`, `LoadBehavior=3` | Registers add-in with Word          |
| `HKCU\Software\Microsoft\Office\Word\AddinsData\KleiKodesh` | Trust data                   | Adds to Office trusted list         |
