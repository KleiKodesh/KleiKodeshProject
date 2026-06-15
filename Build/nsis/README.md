# NSIS Installer

NSIS (Nullsoft Scriptable Install System) configuration for the KleiKodesh installer.

## Files

- `KleiKodeshWrapper.nsi` — Main NSIS script that wraps the WPF installer
- `InstallerHeader.bmp` — Header image for installer welcome page
- `SideBanner.bmp` — Side banner for installer pages

## How It Works

1. NSIS runs prerequisite checks (Windows version, .NET Framework, Office version)
2. Launches the WPF installer (`KleiKodeshVstoInstallerWpf.exe`)
3. Waits for completion and handles any errors
4. Updates Windows "Installed Apps" registry entries
5. Creates uninstall support

## Output

Produces three .exe files for distribution:
- `KleiKodeshSetup-vX.Y.Z-x64.exe` — 64-bit only
- `KleiKodeshSetup-vX.Y.Z-x86.exe` — 32-bit only
- `KleiKodeshSetup-vX.Y.Z.exe` — Auto-detect (AnyCPU)
