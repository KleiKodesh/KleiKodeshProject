# Build — WPF Installer & NSIS Wrapper

The build system for creating the KleiKodesh WPF installer and NSIS wrapper.

## What It Does

Builds three installer variants (x64, x86, AnyCPU) and wraps each in an NSIS installer that checks OS prerequisites before installation.

## Folder Structure

```
Build/
├── Installer/                  — WPF installer project (.NET Framework)
│   ├── Helpers/
│   │   ├── AddinInstaller.cs   — Core install logic: extract VSTO zip, register add-in, save version
│   │   ├── AdminHelper.cs      — UAC elevation ("Run as administrator" button)
│   │   └── ProgramFilesHelper.cs — Find %LocalAppData%, check disk space
│   ├── Pages/
│   │   ├── LandingPage.xaml    — Welcome screen
│   │   ├── AdvancedPage.xaml   — Website whitelist editor
│   │   ├── InstallPage.xaml    — Install progress + final screen
│   │   ├── RepairPage.xaml     — Repair/cleanup with elevation option
│   │   └── (other pages)
│   ├── Dialogs/
│   │   └── WhitelistEditorDialog.xaml — Edit website list before install
│   ├── Resources/
│   │   ├── BrushResources.xaml — Theme colors (mid-gray opacity overlays for dark mode)
│   │   ├── Brushes.xaml        — Renamed to BrushResources.xaml
│   │   └── (other themes)
│   ├── KleiKodeshVstoInstallerWpf.csproj — WPF installer project
│   ├── App.xaml, Main.xaml     — WPF app entry points
│   └── README.md               — Installer-specific docs
├── nsis/                        — NSIS wrapper script
│   ├── KleiKodeshWrapper.nsi    — Main NSIS script
│   ├── MUI_HEBREW.nsh          — Hebrew localization for NSIS
│   └── (resource files)
├── scripts/
│   ├── build-installer.ps1     — Main build script (calls build helpers)
│   ├── build-helpers.ps1       — Helper functions (Get-CurrentVersion, Get-OutputPath, etc.)
│   └── deploy-gh-pages.ps1     — (future) GitHub Pages deployment
├── releases/                    — Output folder for final `.exe` files (created during build)
│   ├── KleiKodeshSetup-vX.Y.Z-x64.exe
│   ├── KleiKodeshSetup-vX.Y.Z-x86.exe
│   ├── KleiKodeshSetup-vX.Y.Z.exe       — AnyCPU variant
│   └── (checksums, release notes)
├── build-menu.bat              — Batch wrapper for easy access to build scripts
├── RELEASE_NOTES.txt           — Template for release notes
└── README.md                   — This file
```

## Build Process

```
build-installer.ps1
  ↓
Calls UpdateVersion.ps1
  → Updates version in AddinInstaller.cs and .csproj files
  ↓
Builds KleiKodeshVsto.sln for all three platforms (x64, x86, AnyCPU)
  → MSBuild with Release config, outputs to bin\Release-x64\, bin\Release-x86\, bin\Release\
  ↓
For each platform, creates VSTO zip (manifest + assemblies)
  ↓
For each platform, calls NSIS with command-line defines
  → Packages VSTO zip + WPF installer + prereq checks
  → Outputs KleiKodeshSetup-vX.Y.Z-{x64,x86,}.exe to releases/
  ↓
Generates checksums (SHA256)
```

## Key Files

### AddinInstaller.cs

The single source of truth for app version:

```csharp
public const string Version = "v3.4.0";
```

Every build run updates this value via `UpdateVersion.ps1`.

### Installation Flow

1. **NSIS wrapper** (`KleiKodeshWrapper.nsi`) checks:
   - Windows 10 or later
   - .NET Framework 4.7.2+
   - VSTO runtime installed
   
2. If prerequisites fail → user sees error, installer exits

3. If OK → launches WPF installer with `--silent` or user UI based on context

4. **WPF installer** (`InstallPage.xaml`):
   - Extracts embedded VSTO zip to `%LocalAppData%\KleiKodesh\`
   - Registers add-in with Word via `AddinInstaller.RegisterAddInAsync()`
   - Saves version to registry: `HKCU\SOFTWARE\KleiKodesh\Version = "v3.4.0"`
   - Adds folder to VSTO trusted locations

5. **On update**: Cache folders (`KitveiHakodesh/cache/`, `BloomFilters/`) are **preserved** — see `.kiro/steering/cache-preservation-on-update.md`

## Version Management

See `.kiro/steering/version-management.md` for:
- Single source of truth (AddinInstaller.cs)
- What files get updated during a release build
- Registry keys written by the installer
- Update checker flow (GitHub releases)

## Build Variants

The build produces **three installer files** from a single code run:

| Variant | Platform | VSTO Output | Installer File |
|---------|----------|-------------|-----------------|
| x64 | `Release\|x64` | `bin\Release-x64\` | `KleiKodeshSetup-vX.Y.Z-x64.exe` |
| x86 | `Release\|x86` | `bin\Release-x86\` | `KleiKodeshSetup-vX.Y.Z-x86.exe` |
| AnyCPU | `Release\|AnyCPU` | `bin\Release\` | `KleiKodeshSetup-vX.Y.Z.exe` |

For details on configuring new projects for the three-variant pipeline, see `.kiro/steering/build-variants.md`.

## Manual Build

```powershell
cd Build/scripts
& .\build-installer.ps1
```

Outputs three `.exe` files to `Build/releases/`.

## Automation

`build-menu.bat` provides a GUI menu (Windows batch file) for quick access to common build tasks.

## Website Whitelist

The installer includes an **AdvancedPage** that lets users customize the website list before installation.

**Single source of truth**: `KleiKodeshVsto/WebSitesLib/WebSitesLib/WebSitesWhitelist.json`

Default list is embedded in the installer zip. User edits are written back to disk on install. See `Build/Installer/README.md` for full details.

## Elevation & UAC

- **WPF installer**: `asInvoker` in manifest — never forces UAC
- **NSIS wrapper**: `RequestExecutionLevel user` — never forces UAC
- **Repair page**: Shows blue info banner with "Run as administrator" button when not elevated; clicking it re-launches the installer with `runas` verb

For full UAC / elevation details, see `.kiro/steering/version-management.md` ("UAC / Elevation Policy" section).

## Dependencies

- NSIS 3.08+ (for `KleiKodeshWrapper.nsi`)
- Visual Studio 2022 Community (for MSBuild, VSTO SDK)
- PowerShell 5+ (for build scripts)
- .NET Framework 4.7.2+ SDK (for targeting the installer)
