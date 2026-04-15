# KleiKodesh — כלי קודש

A Microsoft Word add-in suite for Torah document authoring and seforim research.

## Projects

| Folder                                                                                               | Type                  | Purpose                                           |
| ---------------------------------------------------------------------------------------------------- | --------------------- | ------------------------------------------------- |
| [`KleiKodeshVstoInstallerWpf`](KleiKodeshVstoInstallerWpf/README.md)                                 | WPF (.NET)            | **Main app** — installs the VSTO add-in into Word |
| [`KleiKodeshVsto`](KleiKodeshVsto/README.md)                                                         | VSTO (.NET Framework) | Word add-in — ribbon, task panes, all tools       |
| [`DocSeferLib`](DocSeferLib/README.md)                                                               | WPF class library     | Torah document formatting tools (עיצוב תורני)     |
| [`Kezayit`](Kezayit/README.md)                                                                       | Vue 3 + TypeScript    | Seforim viewer frontend (runs in WebView2)        |
| [`Kezayit/CSharpBackend/KezayitLib`](Kezayit/CSharpBackend/KezayitLib/README.md)                     | .NET class library    | C# backend bridging the Vue app to Windows APIs   |
| [`Kezayit/CSharpBackend/BloomSearchEngineLib`](Kezayit/CSharpBackend/BloomSearchEngineLib/README.md) | .NET class library    | Bloom-filter full-text search engine for seforim  |
| [`kleikodesh.github.io`](kleikodesh.github.io/README.md)                                             | Static HTML/CSS/JS    | Public project website and download page          |

## Architecture

```
User runs installer
        ↓
KleiKodeshVstoInstallerWpf  ──extracts & registers──▶  KleiKodeshVsto (Word add-in)
                                                                │
                              ┌─────────────────────────────────┤
                              │                                 │
                        Ribbon buttons                    Task Panes
                              │
              ┌───────────────┼───────────────┐
              │               │               │
          Kezayit         DocSeferLib      RegexFind / WebSites
       (WebView2 Vue)    (WPF formatting)   (HTML / WPF)
              │
        KezayitLib (C# backend)
              │
    BloomSearchEngineLib + SQLite DB
```

## Build

### Release Build (installer)

Use the interactive build menu:

```
Build\build-menu.bat
```

Or call the orchestration script directly:

```powershell
# Increment patch version and create GitHub release
.\Build\build-installer.ps1 -VersionIncrement patch -ReleaseNotesSource commits

# Set an exact version, skip GitHub release
.\Build\build-installer.ps1 -ManualVersion v3.5.0 -NoRelease

# Quick test build — no version change, no clean, no release
.\Build\build-installer.ps1 -ManualVersion v3.2.0 -NoRelease -NoClean
```

The build pipeline:
1. Updates version in `AddinInstaller.cs` and `KleiKodeshVstoInstallerWpf.csproj`
2. `dotnet build` the WPF installer (its prebuild event builds the VSTO add-in via MSBuild)
3. `makensis.exe` wraps everything into `Build/releases/KleiKodeshSetup-vX.Y.Z.exe`
4. Optionally creates a GitHub release via `gh` CLI

### Development Build (MSBuild)

- **Full solution:**
  ```
  & "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" KleiKodeshProject.slnx /m /nologo /verbosity:minimal
  ```
- **SDK-style projects only (dotnet):**
  ```
  dotnet build
  ```
  Works for `KleiKodeshVstoInstallerWpf`, `KezayitLib`, `BloomSearchEngineLib`.  
  Old-style projects (`KleiKodeshVsto`, `DocSeferLib`) require MSBuild from Visual Studio.

## Version

App version is defined in `KleiKodeshVstoInstallerWpf/Helpers/AddinInstaller.cs` as `const string Version`.  
All other version stamps (`.csproj`, NSIS) are derived from it by `KleiKodeshVstoInstallerWpf/UpdateVersion.ps1` during the build.  
After install it is written to the registry at `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh` → `Version`.
