# NSIS Installer Wrapper

NSIS (Nullsoft Scriptable Install System) wrapper that surrounds the WPF installer with prerequisite checks, Windows Installed Apps registration, and uninstall support.

## Files

**`KleiKodeshWrapper.nsi`** — Main NSIS compilation script. Flow:
1. Checks prerequisites: Windows 10+, .NET Framework 4.7.2+, VSTO runtime
2. Checks for running Word instances (prompts to close)
3. Launches the WPF installer (`KleiKodeshVstoInstallerWpf.exe`) with appropriate CLI args
4. Writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh` entries
5. Creates Start Menu shortcut for uninstall

## How the Wrapper Works

The NSIS script is parameterized with:
- `PRODUCT_VERSION` — Version string (e.g. "v3.6.1") passed by `build-installer.ps1`
- `WPF_EXE_PATH` — Path to the built WPF installer exe

The wrapper never extracts files itself — the WPF installer handles all file operations. The NSIS layer exists solely for:
- Windows "Installed Apps" list entry
- Add/Remove Programs support
- Prerequisite checking before launching the WPF installer
- Uninstall registry cleanup

## Output

Build produces three installer executables:
| File | Platform | Target |
|------|----------|--------|
| `KleiKodeshSetup-vX.Y.Z-x64.exe` | x64 | 64-bit Office |
| `KleiKodeshSetup-vX.Y.Z-x86.exe` | x86 | 32-bit Office |
| `KleiKodeshSetup-vX.Y.Z.exe` | AnyCPU | Auto-detect at runtime |

Each is a self-extracting NSIS archive containing the WPF installer + prerequisites check.

## Build

The NSIS script is compiled during `build-installer.ps1` execution. Requires NSIS 3.08+ with the Hebrew language module (`MUI_HEBREW.nsh`).
