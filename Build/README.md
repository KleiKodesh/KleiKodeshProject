# Build — Installer Build System

Three-installer pipeline: builds WPF installer + NSIS wrapper for x64, x86, and AnyCPU variants in a single run.

## Quick Start

```powershell
Build\build-menu.bat
```

Launches interactive menu with options to build, clean, test, or manage GitHub releases.

## Folder Structure

```
Build/
├── Installer/              — WPF installer project
│   ├── Helpers/
│   │   ├── AddinInstaller.cs    — Extract VSTO zip, register add-in, version management
│   │   ├── AdminHelper.cs       — UAC elevation & re-launch
│   │   └── WordHelper.cs        — Detect/close Word before install
│   ├── Pages/
│   │   ├── LandingPage.xaml     — Welcome screen
│   │   ├── SettingsPage.xaml    — Ribbon settings
│   │   ├── AdvancedPage.xaml    — Website whitelist editor
│   │   ├── InstallPage.xaml     — Progress + finish
│   │   └── RepairPage.xaml      — Repair/cleanup
│   ├── Dialogs/
│   │   └── WhitelistEditorDialog.xaml — Edit website list
│   └── README.md               — Detailed installer docs
├── nsis/                   — NSIS wrapper
│   ├── KleiKodeshWrapper.nsi    — Prereq checks, wraps WPF installer
│   └── MUI_HEBREW.nsh          — Hebrew localization
├── scripts/                — Build orchestration
│   ├── build-installer.ps1     — Main orchestrator (no interactivity)
│   ├── build-menu.ps1          — Interactive menu (prompts user)
│   ├── build-helpers.ps1       — Shared functions & constants
│   └── UpdateVersion.ps1       — Version bumper
├── releases/               — Output folder
│   ├── KleiKodeshSetup-vX.Y.Z-x64.exe
│   ├── KleiKodeshSetup-vX.Y.Z-x86.exe
│   └── KleiKodeshSetup-vX.Y.Z.exe     (AnyCPU)
├── build-menu.bat         — Entry point (launches PowerShell menu)
└── RELEASE_NOTES.txt      — Template for release notes
```

## Build Flow

```
build-menu.bat  ──┐
                  ├─→ build-menu.ps1 (prompts user: version, notes source, confirm)
                  │
                  └─→ build-installer.ps1 -VersionIncrement patch -ReleaseNotesSource commits
                      ├─ UpdateVersion.ps1 (bumps AddinInstaller.cs + csproj)
                      ├─ dotnet build WPF installer -p:InstallerVariant=x64 -p:VstoPlatform=x64
                      │  └─ Pre-build target: MSBuild KleiKodeshVsto (x64), zip → KleiKodesh.zip → embed as resource
                      │  └─ #if INSTALLER_VARIANT_X64 const baked in
                      ├─ makensis /DPRODUCT_VERSION=vX.Y.Z /DWPF_EXE_PATH=...
                      │  └─ KleiKodeshSetup-vX.Y.Z-x64.exe
                      ├─ (repeat for x86, AnyCPU)
                      └─ gh release create + upload 3 EXEs
```

## Three Installer Variants

Single build run produces three installers:

| Variant | Platform | VSTO Build | Output |
|---------|----------|-----------|--------|
| x64 | `Release\|x64` | `bin\Release-x64\` | `KleiKodeshSetup-vX.Y.Z-x64.exe` |
| x86 | `Release\|x86` | `bin\Release-x86\` | `KleiKodeshSetup-vX.Y.Z-x86.exe` |
| AnyCPU | `Release\|AnyCPU` | `bin\Release\` | `KleiKodeshSetup-vX.Y.Z.exe` |

Each variant embeds the correct VSTO binary for its platform via the pre-build target. See `.kiro/steering/build-variants.md` for configuring new projects in the dependency chain.

## Version Management

**Single source of truth:** `Build/Installer/Helpers/AddinInstaller.cs`

```csharp
public const string Version = "v3.4.0";
```

`UpdateVersion.ps1` syncs this to the csproj `<Version>` tag during every build. Do not edit version anywhere else — see `.kiro/steering/version-management.md` for full details, registry keys, and update checker flow.

## Installation Flow

1. **NSIS wrapper** checks prerequisites (Windows 10+, .NET 4.7.2+, VSTO runtime)
2. **WPF installer** extracts embedded VSTO zip to `%LocalAppData%\KleiKodesh\`
3. Registers add-in with Word + adds to VSTO trusted locations
4. Saves version to registry: `HKCU\SOFTWARE\KleiKodesh\Version`
5. **On update:** Caches preserved (website whitelist, Word→PDF conversions, Bloom filter index) — see `.kiro/steering/cache-preservation-on-update.md`

CLI args (`--silent`, `--repair`, `--wait-for-pid <PID>`) handled in `App.xaml.cs` for auto-update and elevation workflows.

## Website Whitelist

Installer includes an **AdvancedPage** for customizing the website list before installation.

**Source:** `KleiKodeshVsto/WebSitesLib/WebSitesLib/WebSitesWhitelist.json`

Default list embedded in installer zip. User customizations written back to `%LocalAppData%\KleiKodesh\WebSitesWhitelist.json` on install. See `Build/Installer/README.md` for extraction rules.

## Elevation & UAC

- **WPF installer manifest:** `asInvoker` — never forces UAC
- **NSIS wrapper:** `RequestExecutionLevel user` — never forces UAC
- **Repair page:** Shows blue "Run as administrator" button when not elevated; clicking re-launches with `runas` verb

Full details in `.kiro/steering/version-management.md` ("UAC / Elevation Policy" section).

## Dependencies

- **NSIS 3.08+** (for `KleiKodeshWrapper.nsi`)
- **Visual Studio 2022 Community** (MSBuild, VSTO SDK)
- **PowerShell 5+**
- **.NET Framework 4.7.2+ SDK**
