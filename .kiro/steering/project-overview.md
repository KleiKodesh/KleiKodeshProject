# KleiKodesh Project Overview

## Main App

The **main application** is the **WPF installer** (`Build/Installer`). It installs the VSTO add-in (`KleiKodeshVsto`) into the user's local app data and registers it with Word. When referring to "the app", "the installer", or "app version", this always means the WPF installer project.

- App version is stored in the Windows registry at `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh` → `Version` (e.g. `v3.0.0`).
- The version constant lives in `Build/Installer/Helpers/AddinInstaller.cs` as `const string Version`.

## Projects

- `Build/Installer` — WPF installer (the main app, see above)
- `KleiKodeshVsto` — VSTO Word add-in (installed by the WPF installer)
- `KleiKodeshVsto/DocDesign` — WPF library for Torah document formatting (columns, paragraphs, spacing)
- `KleiKodeshVsto/RegexInWord/RegexFindLib` — WPF library for regex find & replace in Word
- `KleiKodeshVsto/WebSitesLib` — WPF library for curated website browser task pane
- `UpdateCheckerLib` — Library for checking and downloading updates from GitHub
- `WpfLib` — Shared WPF utilities and helpers
- `KitveiHakodesh` (Vue/TypeScript) — frontend for the KitveiHakodesh seforim viewer
- `KitveiHakodesh/CSharpBackend/KitveiHakodeshLib` — C# backend for the KitveiHakodesh WebView2 app
- `KitveiHakodesh/CSharpBackend/BloomSearchEngineLib` — Bloom filter search engine
- `hebrew-typing-tutor` — Browser-based Hebrew touch-typing tutor (separate project, Vue/TypeScript)
- `kleikodesh-website` — Public marketing website, hosted at kleikodesh.github.io (static HTML/CSS/JS)

## Website Whitelist (Installer)

The installer lets users customize the website list before installation via `AdvancedPage` → `WhitelistEditorDialog`.

**Single source of truth:** `KleiKodeshVsto/WebSitesLib/WebSitesLib/WebSitesWhitelist.json`

**How it works:**
- User never opens the dialog → whitelist untouched (existing file preserved on update, default extracted on fresh install)
- User opens the dialog → full catalogue shown; each entry pre-checked based on the installed file (present = checked, absent = unchecked; fresh install uses default `IsVisible`)
- On OK → only checked entries written to disk, no `IsVisible` field in output
- The VSTO add-in loads whatever is on disk and shows all of it — no `IsVisible` filtering at runtime

`AddinInstaller.PendingWhitelist` is `null` until the user opens the dialog and clicks OK.

See `Build/Installer/README.md` for full details.

## Bloom Filter Index & Version Detection

The Bloom filter index (`BloomFilters/lines.dat`) is built from the Zayit seforim database.
After each successful index build, the current app version is written to `BloomFilters/lines.ver`.
On startup (`SearchHandler.OnDbReady`), if the installed app version (from registry) differs from the stamped version, the user is prompted (via `bloomIndexVersionMismatch` push event → Vue confirm dialog) whether to rebuild the index.

## Build

- **MSBuild** (for VSTO and WPF projects requiring VS tools): `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- **Full solution build**: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" KleiKodeshProject.slnx /m /nologo /verbosity:minimal`
- **dotnet build** works for SDK-style projects only (`Build/Installer`, `KitveiHakodeshLib`, `BloomSearchEngineLib`). Old-style WPF/VSTO projects (`KleiKodeshVsto`, `WpfLib`) require MSBuild from VS.

## NuGet Quirk — Native Interop DLLs Don't Propagate Transitively

`System.Data.SQLite` ships a **native DLL** (`SQLite.Interop.dll`, x86 + x64). The NuGet package includes MSBuild targets that copy this DLL to the output directory — but those targets **only run in the project that directly references the NuGet package**. They do not propagate through project references.

**Consequence:** If project A references project B, and B uses SQLite, A's output will be missing `SQLite.Interop.dll` → `DllNotFoundException` at runtime (not at build time).

**Fix applied:** `KleiKodeshVsto` references `KitveiHakodeshLib` which uses SQLite. Since `KleiKodeshVsto` doesn't directly reference SQLite, the interop DLL was explicitly added as `Content` items in `KleiKodeshVsto.csproj`:

```xml
<Content Include="..\packages\Stub.System.Data.SQLite.Core.NetFramework.1.0.119.0\build\net46\x64\SQLite.Interop.dll">
  <Link>x64\SQLite.Interop.dll</Link>
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
<Content Include="..\packages\Stub.System.Data.SQLite.Core.NetFramework.1.0.119.0\build\net46\x86\SQLite.Interop.dll">
  <Link>x86\SQLite.Interop.dll</Link>
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

**Rule:** Any project that transitively depends on SQLite (via a project reference chain) must explicitly include these `Content` items. Pure managed NuGet packages (Dapper, HtmlAgilityPack, GongSolutions.WPF.DragDrop, System.Text.Json, etc.) do **not** have this problem — they copy normally through project references.

## README Files

Each project folder contains a `README.md` describing its purpose, folder structure, and how it integrates with the VSTO add-in. When exploring or modifying a project, read its README first for orientation.

| README                                                                                                               | Covers                                                             |
| -------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| [`README.md`](../../README.md)                                                                                       | Root overview, architecture diagram, build instructions            |
| [`Build/Installer/README.md`](../../Build/Installer/README.md)                                                       | WPF installer: extraction, registry keys, version management       |
| [`KleiKodeshVsto/README.md`](../../KleiKodeshVsto/README.md)                                                         | VSTO add-in: ribbon, task panes, helpers                           |
| [`KleiKodeshVsto/DocDesign/README.md`](../../KleiKodeshVsto/DocDesign/README.md)                                                                   | Torah formatting library: Columns, Paragraphs, Spacing             |
| [`KleiKodeshVsto/RegexInWord/RegexFindLib/README.md`](../../KleiKodeshVsto/RegexInWord/RegexFindLib/README.md)                                         | Regex find & replace library for Word                              |
| [`KleiKodeshVsto/WebSitesLib/README.md`](../../KleiKodeshVsto/WebSitesLib/README.md)                                                               | Curated website browser task pane                                  |
| [`KitveiHakodesh/README.md`](../../KitveiHakodesh/README.md)                                                                       | Vue 3 frontend: components, stores, host bridge, build             |
| [`KitveiHakodesh/CSharpBackend/README.md`](../../KitveiHakodesh/CSharpBackend/README.md)                                           | C# backend projects overview                                       |
| [`KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/README.md`](../../KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/README.md)                     | WebView2 host, message bridge, all handlers                        |
| [`KitveiHakodesh/CSharpBackend/BloomSearchEngineLib/README.md`](../../KitveiHakodesh/CSharpBackend/BloomSearchEngineLib/README.md) | Bloom filter search engine: indexing, searching, version detection |
| [`hebrew-typing-tutor/README.md`](../../hebrew-typing-tutor/README.md)                                               | Browser-based Hebrew touch-typing tutor: exercises, progress       |
| [`kleikodesh-website/README.md`](../../kleikodesh-website/README.md)                                                 | Public website (GitHub Pages): homepage, downloads, features       |
