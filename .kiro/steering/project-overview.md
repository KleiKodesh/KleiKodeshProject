# KleiKodesh Project Overview

## Main App

The **main application** is the **WPF installer** (`KleiKodeshVstoInstallerWpf`). It installs the VSTO add-in (`KleiKodeshVsto`) into the user's local app data and registers it with Word. When referring to "the app", "the installer", or "app version", this always means the WPF installer project.

- App version is stored in the Windows registry at `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh` → `Version` (e.g. `v3.0.0`).
- The version constant lives in `KleiKodeshVstoInstallerWpf/InstallProgressWindow.xaml.cs` as `const string Version`.

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
