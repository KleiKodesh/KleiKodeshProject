# Architecture Map

This is a functional architecture map of the app — a Hebrew book reader, mobile-first, strictly RTL.

Every folder described here has a `README.md` that goes deeper: which file to edit for a given task, what to import from where, and what constraints apply. When working in any folder, read its README first.

## App Shell

The root layout is `App.vue`, which stacks two elements vertically:

- `AppTitleBar.vue` — fixed 40px header
- `AppPageView.vue` — fills remaining height, renders the active page

`AppPageView` maps `tabStore.activeTab.route` to a page component. Routes `/book-view` and `/search` are keyed by `activeTabId` so they fully remount on tab switch. All other routes share a single instance.

## Setup Wizard

`SetupWizard.vue` is a full-screen onboarding overlay rendered in `App.vue` when `settingsStore.setupDone` is `false`. It covers the entire app until the user completes or skips it.

Steps (in order):

1. `welcome` — always shown; app logo, intro text
2. `db` — only shown when `isHosted && !dbReady`; lets the user download Zayit/Otzaria or pick an existing `.db` file via `window.__webviewPickDbPath`
3. `theme` — theme picker + app zoom slider
4. `general` — divine name censoring, resume-last-read, new-tab destination
5. `book-display` — header/text fonts, font size, line padding; optional separate commentary settings

Navigation: forward/back buttons with a slide transition; a "skip" button calls `settings.completeSetup()` immediately. Progress is shown as a top-edge accent bar.

Completion: `settings.completeSetup()` sets `setupDone = true` and persists it to IDB at key `setupDone` (via `KEYS.SETTINGS_SETUP_DONE` in `persistence.ts`). Once set, the wizard never shows again.

## Title Bar

`AppTitleBar.vue` is the app's only persistent chrome.

- Physical left side: hamburger nav menu (`AppTitleBarNavDropdown`), theme toggle, toolbar toggle (book view only), PDF filter toggle (PDF tabs only)
- Center: active tab title + TOC path (truncated)
- Physical right side: home button, new tab button, close tab button
- Clicking the title bar opens `AppTitleBarTabDropdown` — the full tab list

Keyboard shortcuts handled here: `Ctrl+W` (close tab), `Ctrl+X` (close all), `Ctrl+J` (toggle bottom panel), `Ctrl+F` (guarded — only intercepted when not in a search input).

## Tab System

Tabs are managed entirely by `tabStore`. Each tab is a `Tab` object with a route, title, and optional route-specific state (bookId, PDF info, search query, TOC path, etc.).

Navigation rules:

- `tabStore.updateActiveTab({ route, title, ...data })` — navigate in-place (most common)
- `tabStore.openTab(...)` — explicitly open a new tab (e.g. "new tab" button)
- `tabStore.navigateToSingleton(route)` — for singleton routes; switches to existing tab if one exists, otherwise replaces current tab

Singleton routes (`/settings`, `/books`, `/hebrewbooks`, `/workspaces`) enforce one-tab-per-route and are never persisted across sessions.

Multi-instance routes (`/book-view`, `/search`, `/pdf-view`) can have multiple tabs open simultaneously.

## Pages & Routes

| Route              | Component                  | Kind                            |
| ------------------ | -------------------------- | ------------------------------- |
| `/`                | `HomePage.vue`             | shared instance                 |
| `/books`           | `BookCatalogPage.vue`      | singleton                       |
| `/book-view`       | `BookViewPage.vue`         | multi-instance (keyed by tabId) |
| `/search`          | `FullTextSearchPage.vue`   | multi-instance (keyed by tabId) |
| `/settings`        | `SettingsPage.vue`         | singleton                       |
| `/hebrewbooks`     | `HebrewBooksPage.vue`      | singleton                       |
| `/pdf-viewer`      | `PdfViewPage.vue`          | multi-instance                  |
| `/html-view`       | `HtmlViewPage.vue`         | multi-instance                  |
| `/workspaces`      | `WorkspaceManagerPage.vue` | singleton                       |
| `/hebrew-calendar` | `HebrewCalendarPage.vue`   | singleton                       |
| `/dictionary`      | `DictionaryPage.vue`       | singleton                       |
| `/midot`           | `HalachicUnitsPage.vue`    | singleton                       |

## Feature Folders (`src/features/`)

### home/

Home page navigation tiles. The tile list in `HomePage.vue` and the menu list in `AppTitleBarNavDropdown.vue` are the two entry points to the same set of destinations — they must always be kept in sync. When adding, removing, or renaming a navigation destination, update both files. The home page uses `navigate()` (navigates in the active tab); the nav dropdown uses `navigateInNewTab()` (always opens a new tab). Neither list is derived from the other — they are maintained in parallel.

- `HomePage.vue`, `HomePageTile.vue`, `useHomeDateInfo.ts`, `useDafYomiNavigation.ts`

### book-catalog/

Book catalog browser. Supports list, tiles, and full tree views with search.

- `BookCatalogPage.vue` — main page; owns view switching (list/tiles/tree) via `<component :is>` map
- `BookCatalogTreeView.vue` — collapsible category tree
- `BookCatalogSearch.vue` — search results
- `BookCatalogBreadcrumb.vue` — navigation breadcrumb
- `useBookCatalog.ts` — navigation and folder traversal
- `useBookCatalogSearch.ts` — title + TOC entry search
- `booksCategoryTree.ts` — tree building and category metadata

### book-view/

The main book reader. Orchestrates a split pane (text above, commentary below), a TOC side panel, a floating search bar, and a toolbar. Organized into three subfolders: `lines/`, `toc/`, and `commentary/`.

**Main components:**
- `BookViewPage.vue` — orchestrator
- `BookViewToolbar.vue` — zoom, search, TOC, bottom panel toggles
- `BookViewSplitPane.vue` — thin wrapper around `SplitPane` that wires the top/bottom slots
- `BookViewSidePanel.vue` — side panel container for TOC
- `BookViewSearchBar.vue` — floating search (query, mode, match navigation)
- `BookViewRelatedBooksDropdown.vue` — dropdown for related books
- `bookViewTypes.ts` — shared types: `SearchMode`, `SidePanelMode`

**Main composables:**
- `useBookView.ts` — central composable; owns all data loading, state, event handlers, and watchers. `BookViewPage.vue` is a shell that calls this.
- `useBookViewSearch.ts` — content search (line-based)
- `useBookViewScrollSync.ts` — syncs active TOC entry and auto-selects commentary on scroll
- `useBookViewSessionRestore.ts` — restores per-book view state from IDB on mount
- `useBookViewPinnedCommentary.ts` — tracks the pinned commentary book with default-commentator fallback

**lines/ subfolder** — main text rendering with virtual scroller and line selection:
- `BookViewLinesContent.vue` — main text (virtual scroller, line selection)
- `useBookViewLinesTable.ts` — paginated line fetching from the `line` table in chunks of 200; pre-allocates placeholder slots so the virtualizer has the correct total height immediately, then fills content as chunks arrive; exposes `prioritise(lineIndex)` to move a chunk to the front of the fetch queue
- `useBookViewLineRenderer.ts` — line rendering logic
- `useBookViewLineCopyMenu.ts` — context menu for line copying

**toc/ subfolder** — table of contents side panel:
- `BookViewTocTree.vue` — TOC side panel (main + alt structures)
- `BookViewTocTreeSection.vue` — TOC section header
- `useBookViewToc.ts` — loads TOC entries and alt TOC structures for a book; builds a path map (entry id → full breadcrumb string); exposes `getActiveTocEntry` and `getTocPath`
- `useBookViewTocScrollTracking.ts` — tracks programmatic TOC scrolls to suppress active-entry updates during animation
- `tocSearchUtils.ts` — TOC search utilities

**commentary/ subfolder** — commentary display and navigation:
- `CommentaryView.vue` — commentary display grouped by book
- `CommentaryHeader.vue` — commentary book header (type selector, nav)
- `CommentaryHeaderNav.vue` — prev/next section navigation
- `CommentaryTreePanel.vue` — tree panel for commentary filtering
- `CommentaryTreeSectionNode.vue` — node in the commentary tree
- `commentaryTreeTypes.ts` — types for commentary tree
- `useCommentary.ts` — fetches linked commentary for a selected line (or range), groups results by connection type and category
- `useCommentarySearch.ts` — commentary search (flat index-based)
- `useCommentaryNavigation.ts` — next/prev section navigation for the commentary panel
- `useCommentaryRender.ts` — commentary rendering logic
- `useCommentaryScroll.ts` — commentary scroll handling
- `useCommentaryTocPaths.ts` — TOC path building for commentary
- `useCommentaryTreeSearch.ts` — search within commentary tree
- `useCommentaryCopy.ts` — copy functionality for commentary

### full-text-search/

Full-text search backed by FtsLib (LSM-style segment index with delta+varint compressed posting lists). Supports category/book filters and caches results in IDB.

- `FullTextSearchPage.vue` — main page
- `FullTextSearchBar.vue` — search input + filter toggle
- `FullTextSearchResultsList.vue` — results (virtual scroller)
- `FullTextSearchFilterPanel.vue` — category/book filter tree
- `FullTextSearchFilterNode.vue` — filter tree node
- `FullTextSearchIndexingOverlay.vue` — indexing progress overlay; shown while the index is building; search is enabled as soon as the first segment is flushed (partial index)
- `useFullTextSearch.ts` — search execution and IDB caching; streams results from C# in batches via `chrome.webview` message events; enriches each batch with TOC paths via a SQL query; resumes interrupted searches from the cache skip offset
- `useFullTextSearchIndexingStatus.ts` — subscribes to `ftsIndexProgress` push events from C#; handles `ftsIndexInvalidated` (automatic rebuild when DB changes or index is corrupt)
- `useFullTextSearchFilters.ts` — filter state (checked books/categories), result filtering, and result click handler
- `fullTextSearchTypes.ts` — TypeScript types

#### Query syntax (passed verbatim to FtsLib)

- Multiple words are AND-ed. `word*` is a wildcard (prefix, infix, or suffix). `word~` / `word~2` / `word~3` is fuzzy (edit distance 1–3). `a | b` is OR within one AND slot.
- The frontend passes two additional parameters: `maxWordDistance` (default 10 — maximum token distance between matched terms in a line) and `requireOrdered` (default false — whether terms must appear in query order). These are applied inside FtsLib's snippet pipeline, not during index intersection.

### settings/

App settings across three tabs: general, reading, and advanced. Also contains the setup wizard.

- `SettingsPage.vue` — single-page settings view with a sticky search bar and a nav panel dropdown for jumping to sections; all settings rendered in one continuous scroll
- `SettingsAdvancedPane.vue` — calendar city picker, database path picker (hosted only), and reset actions; accepts `visibleSections` prop for search filtering
- `SettingRow.vue`, `HintIcon.vue`, `SliderSetting.vue`, `ToggleGroup.vue`
- `ThemePicker.vue`, `FontDisplaySettings.vue`, `FontSelector.vue`
- `useSettingsPage.ts`
- `SetupWizard.vue` — first-launch onboarding wizard

### hebrew-books/

HebrewBooks catalog browser with download history.

- `HebrewBooksPage.vue`, `HebrewBooksListItem.vue`
- `useHebrewBooks.ts`, `hebrewBooksCatalog.ts`

### halachic-units/

Halachic unit converter. Singleton route `/midot`. Converts between biblical, Talmudic, and modern units across six systems (length, area, volume, weight, coins, time) with support for multiple halachic opinions.

- `HalachicUnitsPage.vue` — full converter UI with opinion selector and conversion explanation
- `halachicUnits.ts` — all conversion logic (`convert`, `toMetric`, `explainConversion`)
- `units/` — unit definitions per measurement system; `types.ts` for shared types

### pdf-viewer/

PDF viewer with OCR support. Embeds a PDF.js iframe and provides OCR text extraction.

- `PdfViewPage.vue` — main PDF viewer page
- `PdfOcrResultPopup.vue` — OCR result display popup
- `usePdfOcrSelection.ts` — OCR selection and text extraction
- `pdfOcrInjectedScript.ts` — script injected into PDF.js iframe

### html-view/

HTML file viewer for local HTML documents.

- `HtmlViewPage.vue` — main HTML viewer page

### dictionary/

Hebrew dictionary lookup. Singleton route `/dictionary`.

- `DictionaryPage.vue` — main dictionary page
- `DictionaryWordPage.vue` — individual word page
- `dictionaryCache.ts` — LRU cache for dictionary lookups
- `dictionaryTypes.ts` — TypeScript types

### workspace/

Workspace CRUD UI.

- `WorkspaceManagerPage.vue`

### hebrew-calendar/

Hebrew calendar page. Monthly grid and weekly detail views with zmanim. Singleton route.

- `HebrewCalendarPage.vue` — orchestrator, view-mode toggle, city picker
- `MonthlyView.vue` — monthly grid with Hebrew dates, holidays, parasha
- `WeeklyView.vue` — week view with events, candle lighting, zmanim, daily learning
- `DayRow.vue`, `CalendarHeader.vue`, `calendarTypes.ts`
- `useMonthlyView.ts` — month navigation, keeps Gregorian and Hebrew month in sync
- `useWeeklyView.ts` — week data via `@hebcal/core`, daily learning via `hebrewLearning.ts`
- `useZmanim.ts` — city selection, geolocation, zmanim calculation

## App Shell (`src/layout/`)

- `AppTitleBar.vue`, `AppPageView.vue`, `AppTitleBarTabDropdown.vue`, `AppTitleBarNavDropdown.vue`

`AppTitleBarNavDropdown` is the hamburger nav menu. Its destination list mirrors the tiles in `HomePage.vue` — see the `home/` section for the sync rule.

## Shared Components (`src/components/`)

Reusable UI primitives used across multiple features. No feature-specific logic lives here.

- `TreeView.vue`, `TreeNode.vue` — generic tree
- `treeTypes.ts` — `TreeNodeItem` interface; import from here, never from `TreeNode.vue`
- `SplitPane.vue` — resizable split pane
- `BottomSearchBar.vue`, `ContextMenu.vue`, `ConfirmDialog.vue`, `LoadingAnimation.vue`
- `HintIcon.vue` — tooltip hint icon
- `IconTreeRtl.vue` — `IconTextBulletListTree` pre-flipped for RTL layout

## App Shell (`src/layout/`)

## Pinia Stores (`src/stores/`)

**tabStore** — tab lifecycle, navigation, and all per-tab/per-book state persistence. The central store — most features read from it.

**bookViewStore** — book viewer UI state: toolbar visibility, floating search bar position, per-tab+book zoom map. Exposes a reactive `zoom` computed for the active tab+book.

**settingsStore** — all app-wide settings (fonts, sizes, padding, zoom, diacritics, censoring, etc.). Each setting has its own localStorage key and is watched individually.

**booksDataStore** — lazy-loaded book catalog. Fetches all categories and books on first access, builds the category tree, assigns period metadata.

**workspaceStore** — workspace management. All tab/book IDB keys are workspace-scoped; switching workspaces changes `activeId` and reloads tabs.

**localFileStore** — Local file and Word handling state. Manages conversion state, HebrewBooks download state, and session restore for PDF/HTML tabs. Listens to C# push events (`conversionStarted`, `hbPdfReady`, `hbPdfCancelled`).

**searchCacheStore** — LRU cache for FTS search results (capped at 100 entries), stored in `app-search-cache` IDB.

**hebrewBooksHistoryStore** — HebrewBooks download history, stored in `app-hb-history` IDB, LRU-capped at 25 entries.

**pdfOcrStore** — PDF OCR state and results caching.

## Composables (`src/composables/`)

**useAppNavigation.ts** — central navigation handler. Routes singletons via `navigateToSingleton()`, handles file picker, external links, and search navigation.

**useVirtualScrollerKeys.ts** — keyboard nav for virtual scrollers (`Ctrl+Home`, `Ctrl+End`). Required on every component that uses `@tanstack/vue-virtual`. The scroll container must have `tabindex="0"`.

**useZoom.ts** — zoom handler for keyboard (`Ctrl+±/0`), wheel (`Ctrl+scroll`), and pinch (2-finger touch). Config: MIN=50, MAX=200, DEFAULT=100, STEP=10.

**useListKeyNav.ts** — arrow-key + Home/End navigation for plain DOM lists; tracks `focusedIndex` and scrolls the focused item into view.

**useTextSelectionKeys.ts** — `Ctrl+A` (select all text in a container) and `Ctrl+F` (open search) scoped to a specific element.

**useTileGridKeys.ts** — 2D arrow-key navigation for tile grids; computes column count from container width to handle Up/Down correctly.

**useVirtualListKeyNav.ts** — arrow-key + `Ctrl+Home`/`Ctrl+End` navigation for `@tanstack/vue-virtual` lists; calls `scrollToIndex` on the virtualizer.

**useLineCopy.ts** — intercepts the browser `copy` event on a scroller element; when the user has selected all, writes each line as a `<div>` in `text/html` and strips HTML tags for `text/plain`, so copied text has no inline line breaks.

**useDropdownClose.ts** — drop-in replacement for `onClickOutside` that also closes on window blur and handles the toggle-button race condition. Use on every dropdown instead of `onClickOutside` directly.

### seforimDb.ts

The main seforim database access layer. Exports:

- `isHosted` — true when running inside C# WebView2 host (or in dev mode)
- `dbReady` — reactive ref, true once a DB path is available
- `query<T>(sql, params)` — executes SQL via `window.__webviewQuery` (C# host) or a `/query` POST to the Vite dev middleware
- `onWebviewEvent(fn)` — subscribe to C# push events

### dictionaryDb.ts

Dictionary database access layer. Separate from the main seforim DB.

- `queryDict<T>(sql, params)` — executes SQL against the dictionary database

### dictionarySeforimDb.ts

Seforim-specific dictionary queries.

### queries.sql.ts

All raw SQL strings for the seforim database live here. No inline SQL anywhere else in the Vue/TypeScript codebase — every query a composable or store needs must be added to this file and imported from it.

### dictionaryDb.sql.ts

All raw SQL strings for the dictionary database.

### bridge.ts

C# host actions for file operations. All functions have dev fallbacks.

- `pickFile()` — native file picker (PDF + Word)
- `restoreLocalFile(filePath)` — re-register virtual host for a local file
- `restoreHbPdf(bookId, bookTitle, tabId)` — restore HebrewBooks PDF from cache
- `disposeLocalFileHost(filePath)` — decrement virtual host ref count on tab close
- `callBridgeAction(name, ...params)` — call any C# action with positional params (used by search/indexing)
- `resetHostApp()` — full app reset: deletes FTS index, resets C# settings, reloads
- `resetSearchIndex()` — resets the FTS index on the C# side (triggers a fresh rebuild)

### devFallbacks.ts

Development fallback implementations for C# bridge functions when running in browser dev mode.

## Utilities (`src/utils/`)

**persistence.ts** — the only file that touches IndexedDB and localStorage directly. All stores import from here; components and composables never do. Manages 3 IDB databases, all key patterns, LRU cap for lastread, the app reset mechanism, and the `__pendingReset` localStorage flag.

**hebrewTextProcessing.ts** — diacritics handling and text normalization for Hebrew.

**censorDivineNames.ts** — divine name censoring (replaces ה with ק).

**normalizeText.ts** — `normalize(s)`: lowercases and strips Hebrew/ASCII quote characters. Import this as the base normalization step before any search comparison.

**segmentSearchTree.ts** — generic segment-aware search tree for any hierarchical node list. `SegmentSearchTree` matches query words as an ordered subsequence across ancestor path segments. Used by `TreeView.vue`, commentary filter panels, and search utilities.

**detectFonts.ts** — `detectAvailableFonts()` uses canvas measurement to detect which Hebrew and general fonts are installed. Used by `FontSelector.vue`.

**scrollToIndexWithRetry.ts** — virtual scroller scroll-to-index with retry for async rendering.

**hebrewKetivExpander.ts** — Hebrew text expansion utilities for ketiv/keri handling.

## Theme System (`src/theme/`)

- `theme.css` — CSS custom properties (colors, fonts, spacing) for all themes
- `themeStore.ts` — active theme preset + reading background color
- `themes.ts` — theme loading, custom theme support, PDF theme observer
- `themeTypes.ts` — TypeScript types
- `themeColorUtils.ts` — color manipulation utilities
- `ThemeToggle.vue` — toggle button in the title bar
- `themes.json` — built-in theme presets

Default theme is `vscode-dark`. Custom themes are stored in `app-settings` IDB at key `customThemes`.

## Initialization Order (`main.ts`)

1. `idbCheckAndExecReset()` — clear all DBs if a reset was scheduled last session
2. Create Pinia
3. `workspaceStore.init()` — must be first; `tabStore` depends on `activeId`
4. In parallel: `tabStore.init()`, `bookViewStore.init()`, `settingsStore.init()`, `themeStore.init()`, `loadCustomThemes()`
5. Restore persisted local file tabs via `localFileStore.restoreTab()`
6. Mount app to `#app`
7. `initPdfThemeObserver()` — sync PDF iframe theme with app theme
8. `booksDataStore.ensureLoaded()` — lazy-load book catalog in background

## C# Backend

The C# project (`CSharpBackend/`) hosts the Vue app in a WebView2 control.

- Vue builds as a single-file bundle (`vite-plugin-singlefile`) — all JS/CSS inlined into one `index.html`
- `KitveiHakodesh.targets` runs `npm run build` after every C# build and copies `dist/` to `bin/{Config}/KitveiHakodesh/`
- C# injects `window.__webviewQuery`, `window.__webviewAction`, `window.__webviewDbReady` before the app boots
- Push events from C# arrive via `window.__onWebviewEvent`
- Target: .NET 4.8, C# 7.3

Key C# handlers:

- `JsBridge.cs` — handles `__webviewAction` calls (file picker, PDF restore, virtual host management)
- `WebBridge.cs` — WebView2 setup, message routing
- `DbAccess.cs` / `DbHandler.cs` — SQLite access via Dapper
- `SearchHandler.cs` — FTS index lifecycle orchestrator (actor-thread pattern); delegates to `FtsIndexBuilder`, `FtsIndexState`, and `FtsSearchExecutor` in `KitveiHakodeshLib/Search/`
- `HebrewBooksHandler.cs` — HebrewBooks download via WebView2 browser engine
- `LocalFileHandler.cs` — local file virtual host management
- `ZimHandler.cs` — ZIM virtual host management (Kiwix reader)
- `WordToPdfConverter.cs` — Word-to-PDF conversion

### FTS Search Pipeline

The full-text search pipeline spans three layers: FtsLib (the index engine), KitveiHakodeshLib (the C# orchestration layer), and the Vue frontend.

**FtsLib** (`CSharpBackend/FtsLib/`) is a standalone library with its own git repo. It exposes a single public entry point: `SeforimIndex` in `FtsLib/Seforim/`. The `Core/` folder contains the index engine internals — `IndexWriter`, `IndexReader`, `SegmentMerger`, `QueryParser`, `SearchExecutor`, `SnippetBuilder`, `Tokenizer`, `FuzzyExpander`, `WildcardExpander`, and the posting-list iterators. The `Misc/` folder contains `ZayitDb.cs`, which owns all SQL for reading lines from the seforim database during indexing and search result hydration. `SeforimIndex` is the only class the rest of the app ever touches.

**KitveiHakodeshLib/Search/** contains four classes that orchestrate the lifecycle:

- `FtsIndexState` — owns all mutable state (current state machine position, `SeforimIndex` instance, DB path, running tasks). All field mutations go through its named transition methods. State machine: Idle → Building → Ready → Merging → Ready.
- `FtsIndexBuilder` — starts and runs the build (`Task.Run`) and background merge. Pushes `ftsIndexProgress` events to the frontend every 5000 lines. Marks the index partially ready as soon as the first segment is flushed so search is available before the build completes.
- `FtsSearchExecutor` — runs each search on a `Task.Run` thread, streams results back to the frontend in batches via `WebBridge.PushEvent` (which calls `PostWebMessageAsString` → `chrome.webview` message events). Batch size starts at 1 and doubles up to 16, then switches to a 150ms timer flush. Applies `maxWordDistance` and `requireOrdered` filters inside the snippet loop.
- `SearchHandler` — thin orchestrator. Serializes all lifecycle operations (OnDbReady, ResetAndReindex, HandleDeleteIndex) through a `BlockingCollection<Action>` actor thread so they never race. Delegates search start/cancel to `FtsSearchExecutor`.

**Vue frontend** (`src/features/full-text-search/`):

- `useFullTextSearch.ts` — calls `FtsSearchStart` via `callBridgeAction`, receives batches via a `chrome.webview` message listener keyed by `searchId`, enriches each batch with TOC paths via `GET_TOC_PATHS_FOR_LINES`, appends to the IDB cache via `searchCacheStore`. On a cache hit with a complete result set, skips the stream entirely. On a partial cache hit, passes `skipCount` to C# so the stream resumes from where it left off.
- `useFullTextSearchIndexingStatus.ts` — subscribes to `ftsIndexProgress` push events via `onWebviewEvent`. Handles `ftsIndexInvalidated` (automatic rebuild when DB changes or index is corrupt).

**Bridge actions** (all handled by `SearchHandler` via `JsBridge.cs`):

| Action | Direction | Description |
| --- | --- | --- |
| `FtsSearchStart` | Vue → C# | Start a search; returns `{ searchId }` |
| `FtsSearchCancel` | Vue → C# | Cancel an in-flight search by `searchId` |
| `GetFtsIndexingProgress` | Vue → C# | Poll current indexing state on mount |
| `ResetFtsIndex` | Vue → C# | Delete index and rebuild from scratch |
| `searchBatch` | C# → Vue | Batch of `FullTextSearchResult` objects |
| `searchComplete` | C# → Vue | Stream finished |
| `searchCancelled` | C# → Vue | Stream cancelled |
| `searchError` | C# → Vue | Stream error |
| `ftsIndexProgress` | C# → Vue | Indexing progress tick |
| `ftsIndexInvalidated` | C# → Vue | Index corrupt or missing; rebuild started automatically |
