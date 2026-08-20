# KitveiHakodeshLib — C# Backend for the KitveiHakodesh Vue App

A .NET class library that hosts the KitveiHakodesh Vue app inside a WebView2 control and bridges it to native Windows APIs (file system, SQLite, PDF handling, HebrewBooks downloads, FtsLib full-text search).

The C# side is a thin shell: one `AppViewer` UserControl owning one WebView2, hosted by a WinForms form (`MainForm` in the demo app) or the VSTO task pane. All UI beyond the window chrome lives in Vue — there is no C# tab UI at all; tabs, panes, and split view are purely frontend concepts. The bridge is a Promise-based RPC: action messages come in via `window.__webviewAction`, replies and push events go out via `window.__onWebviewEvent`. Dark mode is driven by Vue — the frontend sends a `setTheme` action on every theme change, and `AppViewerTheme.cs` applies it to the host window title bar via DarkNet (initial state is passed to Vue at startup via the injected `window.__webviewIsDark`).

## How It Integrates with the VSTO Add-in

`KleiKodeshVsto` instantiates `KitveiHakodeshLib.AppViewer` (a WinForms `UserControl`) and passes it to `TaskpaneManager.Show()`. The task pane hosts the control, which in turn owns the WebView2 instance and all backend handlers.

## Folder Structure

```
KitveiHakodeshLib/
├── AppViewer.cs                    — Root UserControl; WebView2 environment setup, initialisation, public API
├── AppViewerMessageHandlers.cs     — AppViewer partial: bridge message dispatch and all Handle* methods
├── AppViewerNavigation.cs          — AppViewer partial: navigation guard (allowlist) and reload logic
├── AppViewerSplash.cs              — AppViewer partial: splash screen show/hide
├── AppViewerTheme.cs               — AppViewer partial: DarkNet title-bar theme wiring and setTheme handler
├── SplashOverlay.cs                — Fade-in splash screen shown while WebView2 loads
├── WordExporter.cs                 — Exports content to Word
├── HebrewBooks.db                  — Local HebrewBooks catalogue database
├── KleiKodesh_Main.png             — Splash screen image resource
├── Bridge/
│   ├── JsBridge.cs                 — Injects window.__webviewAction into the page; routes messages
│   └── WebBridge.cs                — Sends replies and push events back to the Vue app
├── Db/
│   ├── DbHandler.cs                — Handles 'sql' and 'pickDbPath' messages; runs queries
│   └── DbAccess.cs                 — SQLite wrapper using Dapper
├── Diagnostics/
│   ├── AppLogger.cs                — Application-level logging
│   └── EnvironmentDiagnostics.cs   — Environment info diagnostics
├── Dictionary/
│   ├── DictionaryHandler.cs        — Dictionary lookup handler
│   └── WordThesaurusProvider.cs    — Thesaurus data provider
├── FileSystemSearch/
│   ├── FileSystemSearchHandler.cs  — Bridge handler for file-system search and excluded folders management
│   ├── ExcludedFoldersForm.cs      — RTL Hebrew WinForms dialog for managing excluded folders
│   └── DocumentLocatorAdapter.cs   — Adapter for DocumentLocator service
├── HebrewBooks/
│   ├── HebrewBooksHandler.cs       — Download, cache, and serve HebrewBooks PDFs
│   └── HebrewBooksDb.cs            — HebrewBooks catalogue database access
├── Helpers/
│   └── FontsProvider.cs            — Hebrew-capable font enumeration (DirectWrite; TWIN of the service's HebrewFontsProvider)
├── Pdf/
│   ├── LocalFileHandler.cs         — File picker for local files; virtual host mapping; Word→PDF conversion
│   └── WordToPdfConverter.cs       — Converts Word documents to PDF
├── Properties/
│   └── AssemblyInfo.cs             — Assembly metadata
├── Resources/
│   └── HebrewBooks.db              — Embedded HebrewBooks catalogue DB resource
├── Search/
│   ├── SearchHandler.cs            — FtsLib indexing & search; index lifecycle management
│   ├── FtsIndexBuilder.cs          — Background index builder
│   ├── FtsIndexState.cs            — Tracks index build state
│   └── FtsSearchExecutor.cs        — Executes search queries via FtsLib
├── Settings/
│   ├── AppSettings.cs              — Registry-backed settings for the KitveiHakodesh app
│   └── ShellRegistration.cs        — Windows shell registration
└── UserSettings/
    ├── UserSettingsDbHandler.cs    — User settings database handler
    └── UserSettingsDbAccess.cs     — User settings data access
```

## Message Flow

```
Vue app
  └─ window.__webviewAction("sql", { query, params })
        ↓  (WebView2 WebMessageReceived)
  JsBridge.cs  →  DbHandler.HandleAsync()
        ↓
  DbAccess.QueryAsync()  →  SQLite
        ↓
  WebBridge.Reply(id, rows)
        ↓  (ExecuteScriptAsync)
  window.__onWebviewEvent({ id, payload })
        ↓
Vue app receives result
```

Push events (e.g. `ftsIndexProgress`, `ftsIndexInvalidated`) use `WebBridge.PushEvent()` and arrive on the same `window.__onWebviewEvent` channel without a request ID.

## Key Handlers

| Message name               | Handler         | Description                                   |
| -------------------------- | --------------- | --------------------------------------------- |
| `sql`                      | `DbHandler`     | Execute a parameterised SQL query             |
| `pickDbPath`               | `DbHandler`     | Open file picker for the seforim SQLite DB    |
| `pickFile`                 | `LocalFileHandler` | Open file picker for a local file or Word document |
| `restoreLocalFile`         | `LocalFileHandler` | Re-open a local file from its persisted file path |
| `restoreHbPdf`             | `LocalFileHandler` | Re-open a cached HebrewBooks PDF              |
| `FtsSearchStart`           | `SearchHandler` | Start a full-text search                      |
| `FtsSearchCancel`          | `SearchHandler` | Cancel an in-progress search                  |
| `GetFtsIndexingProgress`   | `SearchHandler` | Poll indexing progress                        |
| `ResetFtsIndex`            | `SearchHandler` | Trigger a full FTS index rebuild              |
| `fileSystemSearchWarmup`   | `FileSystemSearchHandler` | Warm up the DocumentLocator service in the background |
| `fileSystemSearch`         | `FileSystemSearchHandler` | Search the file system via the DocumentLocator service |
| `ResetDocumentLocatorIndex` | `FileSystemSearchHandler` | Wipe and rebuild the file-system index from scratch |
| `openExcludedFoldersManager` | `FileSystemSearchHandler` | Open the RTL WinForms dialog to manage excluded folders; persists changes immediately (search-time filtering, no rebuild needed) |
| `setTheme`                 | `AppViewerTheme` | Apply light/dark to the host window title bar via DarkNet; sent by Vue on every theme change |

## Startup Sequence

1. `AppViewer` creates WebView2 environment with user data folder in the install directory.
2. Maps virtual host `KitveiHakodesh-vue-app` → `KitveiHakodesh/` folder (the built Vue app).
3. Injects `JsBridge.Script` so the page has `window.__webviewAction` available before any JS runs.
4. Navigates to `https://KitveiHakodesh-vue-app/index.html`.
5. On `DOMContentLoaded`, `DbHandler` checks for a previously selected DB path and fires `dbReady` if found.
6. `SearchHandler.OnDbReady()` triggers FtsLib index build if no valid index exists.
