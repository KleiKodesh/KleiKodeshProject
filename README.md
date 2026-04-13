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

- **Full solution (MSBuild):**
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
After install it is written to the registry at `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh` → `Version`.
