# KitveiHakodeshLib — C# Backend for the KitveiHakodesh Vue App

A .NET class library that hosts the KitveiHakodesh Vue app inside a WebView2 control and bridges it to native Windows APIs (file system, SQLite, PDF handling, HebrewBooks downloads, Bloom filter search).

## How It Integrates with the VSTO Add-in

`KleiKodeshVsto` instantiates `KitveiHakodeshLib.AppViewer` (a WinForms `UserControl`) and passes it to `TaskpaneManager.Show()`. The task pane hosts the control, which in turn owns the WebView2 instance and all backend handlers.

## Folder Structure

```
KitveiHakodeshLib/
├── AppViewer.cs              — Root UserControl; initialises WebView2, wires up all handlers
├── SplashOverlay.cs          — Fade-in splash screen shown while WebView2 loads
├── Bridge/
│   ├── JsBridge.cs           — Injects window.__webviewAction into the page; routes messages
│   └── WebBridge.cs          — Sends replies and push events back to the Vue app
├── Db/
│   ├── DbHandler.cs          — Handles 'sql' and 'pickDbPath' messages; runs queries
│   └── DbAccess.cs           — SQLite wrapper using Dapper
├── Search/
│   └── SearchHandler.cs      — Bloom filter indexing & search; version-mismatch detection
├── Pdf/
│   └── PdfHandler.cs         — File picker for PDF/Word; virtual host mapping; Word→PDF conversion
├── HebrewBooks/
│   ├── HebrewBooksHandler.cs — Download, cache, and serve HebrewBooks PDFs
│   └── HebrewBooksCsvUpdater.cs — Updates the HebrewBooks catalogue CSV
└── Settings/
    └── AppSettings.cs        — Registry-backed settings for the KitveiHakodesh app
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

Push events (e.g. `bloomIndexVersionMismatch`, indexing progress) use `WebBridge.PushEvent()` and arrive on the same `window.__onWebviewEvent` channel without a request ID.

## Key Handlers

| Message name               | Handler         | Description                                   |
| -------------------------- | --------------- | --------------------------------------------- |
| `sql`                      | `DbHandler`     | Execute a parameterised SQL query             |
| `pickDbPath`               | `DbHandler`     | Open file picker for the seforim SQLite DB    |
| `pickFile`                 | `PdfHandler`    | Open file picker for a local PDF or Word file |
| `restoreLocalPdf`          | `PdfHandler`    | Re-open a PDF from its persisted file path    |
| `restoreHbPdf`             | `PdfHandler`    | Re-open a cached HebrewBooks PDF              |
| `BloomSearchStart`         | `SearchHandler` | Start a Bloom filter search                   |
| `BloomSearchCancel`        | `SearchHandler` | Cancel an in-progress search                  |
| `GetBloomIndexingProgress` | `SearchHandler` | Poll indexing progress                        |
| `BuildBloomIndex`          | `SearchHandler` | Trigger a full index rebuild                  |

## Startup Sequence

1. `AppViewer` creates WebView2 environment with user data folder in the install directory.
2. Maps virtual host `KitveiHakodesh-vue-app` → `KitveiHakodesh/` folder (the built Vue app).
3. Injects `JsBridge.Script` so the page has `window.__webviewAction` available before any JS runs.
4. Navigates to `https://KitveiHakodesh-vue-app/index.html`.
5. On `DOMContentLoaded`, `DbHandler` checks for a previously selected DB path and fires `dbReady` if found.
6. `SearchHandler.OnDbReady()` checks Bloom index version and prompts rebuild if needed.
