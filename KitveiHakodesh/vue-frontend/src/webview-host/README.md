# src/webview-host

Database access and C# host bridge. Everything that communicates outside the Vue app lives here. Nothing outside this folder should call `fetch` against the DB or invoke C# actions directly.

## Protocol

The transport is WebView2-injected globals (from `JsBridge.cs` in the C# host), not `window.postMessage`:

- **Outbound (JS→C#)**: `window.__webviewAction(action, args?): Promise<unknown>` — the main Promise-based RPC channel; each call is matched to its reply by id. SQL goes over `window.__webviewQuery(sql, params)`. Other injected globals: `__webviewPickDbPath`, `__webviewSetDbPath`, `__webviewDbPath`, `__webviewDbReady`, `__webviewShowPopOut`, `__webviewHbLocalFolder`, `__webviewIsDark` (read once at startup by `themeStore.init()`).
- **Inbound (C#→JS)**: push events arrive on `window.__onWebviewEvent(msg)`, where `msg.event` names the event. Subscribe via `onWebviewEvent(fn)` from `seforimDb.ts` (returns an unsubscribe). There is no dedicated bridge composable — subscription is this function plus per-feature listeners.

Push event names in use: `dbPathPicked` (`seforimDb.ts`, setup wizard); the local-file lifecycle `localFileConversionStarted`, `localFileTxtReady`, `localFileReady`, `localFileConversionReady`, `localFileError`, `hbPdfReady`, `hbPdfCancelled` (all handled in `localFileStore`); `ftsDbNotFound`, `ftsIndexInvalidated` (`useFullTextSearchIndexingStatus`); and `fileSystemIndexingStatus`. No push event carries tab identity — the local-file events echo back the `tabId` sent in the request args (`restoreHbPdf`, `triggerHbDownload`), and there is no C#→JS open/switch/close-tab event.

**seforimDb.ts** — seforim database access layer. Import `query<T>(sql, params)` to run SQL against the main seforim DB, `isHosted` to check if running inside the C# host, `dbReady` to reactively gate DB-dependent UI, and `onWebviewEvent(fn)` to subscribe to C# push events. Queries route through the C# host in production and the Vite dev middleware in development — callers do not need to know which.

**dictionaryDb.ts** — dictionary database access layer. Import `queryDict<T>` for the Aramaic dictionary (KitveiHakodesh_dictionary.db). Never import from seforimDb — the separate file makes it impossible to accidentally query the wrong database.

**dictionaryDb.sql.ts** — all SQL strings for the dictionary database. No inline SQL anywhere else in the dictionary query layer.

**dictionarySeforimDb.ts** — seforim DB queries for the dictionary feature (מצודת ציון, מלבי"ם באור המילות, מחברת מנחם, ספר הערוך). Book IDs are looked up at runtime by title pattern and cached.

**userSettingsDb.ts** — access layer for the user settings database (user_settings.db). Mirrors the seforimDb pattern with `queryUserSettings` and `executeUserSettings`.

**userSettingsDb.sql.ts** — all SQL strings for the user settings database (user_highlights, user_notes tables). No inline SQL anywhere else.

**bridge.ts** — the `__webviewAction` wrapper (`action<T>(name, args)`, positional variant `callBridgeAction<T>(name, ...params)`), env flags, and the named host actions. Import from here for any native interaction; all actions have dev-mode fallbacks.

- Files: `pickFile`, `restoreLocalFile`, `readTxtFileContent`, `restoreHbPdf`, `disposeLocalFileHost`, `pickFolder`
- HebrewBooks: `hbSearch`, `triggerHbDownload`, `checkHbLocalFiles`, `deleteHbLocalFile`, `revealHbLocalFile`, `triggerHbSaveAs`, `clearHbLocalFolder`
- Word/clipboard: `exportToWord`, `pasteIntoWord`, `copyImageToClipboard`
- Search/indexes: `fileSystemSearchWarmup`, `fileSystemSearch`, `openExcludedFoldersManager`, `DeleteFtsIndex`, `ResetFtsIndex`, `ResetDocumentLocatorIndex`
- App/window: `TogglePopOut`, `toggleFullscreen`, `reload`, `resetSettings`, `getDiagnostics`, `clearDbPath`, and `setTheme` — fire-and-forget `{ isDark }` push that keeps the WinForms title bar in sync (applied host-side via DarkNet)

Env flags: `showPopOutButton` = `window.__webviewShowPopOut === true`, and `isVstoEnvironment` (same flag) — true only in the Word task-pane host.

**queries.sql.ts** — all raw SQL strings in the app. Every new SQL query must be added here as a named constant. No inline SQL anywhere else in the codebase.

**devFallbacks.ts** — dev-mode fallbacks for all host operations. Contains the fetch-based DB transports (`devQuery`, `devQueryDict`, `devQueryWikiDict`) and browser file-input pickers (`devPickPdf`, `devPickZim`). Only called when running outside the C# WebView2 host. Never import this file from production logic paths directly — it is only consumed by the other files in this folder.
