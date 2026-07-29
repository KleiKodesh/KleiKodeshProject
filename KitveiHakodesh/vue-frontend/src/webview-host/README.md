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
- Word/clipboard: `exportToWord`, `pasteIntoWord`, `copyImageToClipboard`. `pasteIntoWord` works in BOTH modes — hosted drives Word through the Office PIA (`WordExporter`), dev through the service over raw COM/IDispatch (`AotWordPaste`, since the PIA and `dynamic` are unavailable under native AOT). The clipboard is already set by the copy event, so no HTML crosses the wire either way.
- Search/indexes: `fileSystemSearchWarmup`, `fileSystemSearch`, `openExcludedFoldersManager` (hosted: native WinForms dialog) / `getExcludedFolders` + `setExcludedFolders` (dev: the Vue dialog persists through the service to the same `excluded_folders.json`), `DeleteFtsIndex`, `ResetFtsIndex`, `ResetDocumentLocatorIndex`
- Shared registry settings: `getHbLocalFolderFromRegistry`, `setHbLocalFolderInRegistry`, `getTurnOffUpdates`, `setTurnOffUpdates`, `getDbPathInfo`, `setDbPathDev`, `clearDbPath`. Both modes read and write the SAME registry values (`KitveiHakodesh\Database\Path`, `KitveiHakodesh\HebrewBooks\LocalFolder`, `KleiKodesh\UpdateChecker\TurnOffUpdates`) — hosted via the C# host, dev via the service — so a setting never forks between them. `pickFolder` shows a real native folder dialog in both modes (the service hosts it in dev).
- App/window: `TogglePopOut`, `toggleFullscreen`, `reload`, `resetSettings`, `getDiagnostics`, `clearDbPath`, and `setTheme` — fire-and-forget `{ isDark }` push that keeps the WinForms title bar in sync (applied host-side via DarkNet)

Env flags: `showPopOutButton` = `window.__webviewShowPopOut === true`, and `isVstoEnvironment` (same flag) — true only in the Word task-pane host.

**queries.sql.ts** — all raw SQL strings in the app. Every new SQL query must be added here as a named constant. No inline SQL anywhere else in the codebase.

**queries.types.ts** — the row shapes those queries return: `BookRow`, `CategoryRow`, `BookInfo`, `TocEntry`, `AltTocStructure`, `TocRow`, `LineRow`, `ReverseLineRow`, `CommentaryLinkRow`, `WordLinkAnchor`. Same rule as the SQL strings: anything used as a `query<T>` parameter or a `{ rows: T[] }` service reply is defined here, and a changed SELECT list is changed here in the same edit. The file has **no imports** by design — a row shape must not reference a component prop type, a Vue type, or anything in `features/`. Types that build *on* a row (`CategoryNode` adding `children`/`books`) are view models and belong to the feature that builds them. `seforimApi.ts` re-exports these so callers may keep importing them from the API they call. The C# counterpart is `SeforimModels.cs`; keep the two in step.

**seforimApi.ts** — typed wrappers over the seforim queries (categories, books, TOC entries, connections). Each one routes through the C# host when hosted and the service in dev, so callers never branch on the mode themselves.

**serviceClient.ts** — the dev-mode transport to `KitveiHakodeshService` (MessagePack over the loopback HTTP host). `serviceCall<T>(op, args?)` is the entry point.

**tabMirror.ts** — mirrors the Vue tab store into the native chrome tab strip. Note that this file imports stores and a composable, so unlike everything else here it sits *above* the store layer; it is a known layering exception awaiting a move (see `.kiro/steering/architecture.md`).

**fontsApi.ts** — `detectAvailableFonts()`: the machine's real Hebrew-capable font families, for the settings font picker. Asks the C# host (`getFonts` → WPF) when hosted, the service (`getFonts` → DirectWrite) in dev, and falls back to `fontsCanvasProbe.ts` only when neither answers. `isHosted` is TRUE in dev and cannot pick the path — branch on `__webviewAction`.

**fontsCanvasProbe.ts** — last-resort font detection by canvas text-width measurement against a fixed candidate list. Under-reports by design (it can only confirm names the list already contains) and is never used when a real enumerator is reachable.
