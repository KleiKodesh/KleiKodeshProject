# KleiKodesh Project Overview

## Main App

The **main application** is the **WPF installer** (`KleiKodeshVstoInstallerWpf`). It installs the VSTO add-in (`KleiKodeshVsto`) into the user's local app data and registers it with Word. When referring to "the app", "the installer", or "app version", this always means the WPF installer project.

- App version is stored in the Windows registry at `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh` → `Version` (e.g. `v3.0.0`).
- The version constant lives in `KleiKodeshVstoInstallerWpf/Helpers/AddinInstaller.cs` as `const string Version`.

## Projects

- `KleiKodeshVstoInstallerWpf` — WPF installer (the main app, see above)
- `KleiKodeshVsto` — VSTO Word add-in (installed by the WPF installer)
- `DocSeferLib` — shared library for the VSTO add-in
- `Kezayit/CSharpBackend/KezayitLib` — C# backend for the Kezayit WebView2 app
- `Kezayit/CSharpBackend/BloomSearchEngineLib` — Bloom filter search engine
- `Kezayit` (Vue/TypeScript) — frontend for the Kezayit seforim viewer

## Bloom Filter Index & Version Detection

The Bloom filter index (`BloomFilters/lines.dat`) is built from the Zayit seforim database.
After each successful index build, the current app version is written to `BloomFilters/lines.ver`.
On startup (`SearchHandler.OnDbReady`), if the installed app version (from registry) differs from the stamped version, the user is prompted (via `bloomIndexVersionMismatch` push event → Vue confirm dialog) whether to rebuild the index.

## Build

- **MSBuild** (for VSTO and WPF projects requiring VS tools): `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- **Full solution build**: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" KleiKodeshProject.slnx /m /nologo /verbosity:minimal`
- **dotnet build** works for SDK-style projects only (`KleiKodeshVstoInstallerWpf`, `KezayitLib`, `BloomSearchEngineLib`). Old-style WPF/VSTO projects (`KleiKodeshVsto`, `WpfLib`) require MSBuild from VS.

## NuGet Quirk — Native Interop DLLs Don't Propagate Transitively

`System.Data.SQLite` ships a **native DLL** (`SQLite.Interop.dll`, x86 + x64). The NuGet package includes MSBuild targets that copy this DLL to the output directory — but those targets **only run in the project that directly references the NuGet package**. They do not propagate through project references.

**Consequence:** If project A references project B, and B uses SQLite, A's output will be missing `SQLite.Interop.dll` → `DllNotFoundException` at runtime (not at build time).

**Fix applied:** `KleiKodeshVsto` references `KezayitLib` which uses SQLite. Since `KleiKodeshVsto` doesn't directly reference SQLite, the interop DLL was explicitly added as `Content` items in `KleiKodeshVsto.csproj`:

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
