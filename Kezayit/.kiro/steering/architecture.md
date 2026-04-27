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
| `/books`           | `BooksFsPage.vue`          | singleton                       |
| `/book-view`       | `BookViewPage.vue`         | multi-instance (keyed by tabId) |
| `/search`          | `SearchPage.vue`           | multi-instance (keyed by tabId) |
| `/settings`        | `SettingsPage.vue`         | singleton                       |
| `/hebrewbooks`     | `HebrewBooksPage.vue`      | singleton                       |
| `/pdf-view`        | `PdfViewPage.vue`          | multi-instance                  |
| `/kiwix-view`      | `KiwixViewPage.vue`        | multi-instance                  |
| `/workspaces`      | `WorkspaceManagerPage.vue` | singleton                       |
| `/hebrew-calendar` | `HebrewCalendarPage.vue`   | singleton                       |
| `/dictionary`      | `DictionaryPage.vue`       | singleton                       |
| `/midot`           | `MidotPage.vue`            | singleton                       |

## Feature Folders (`src/components/`)

### home/

Home page navigation tiles. The tile list in `HomePage.vue` and the menu list in `AppTitleBarNavDropdown.vue` are the two entry points to the same set of destinations — they must always be kept in sync. When adding, removing, or renaming a navigation destination, update both files. The home page uses `navigate()` (navigates in the active tab); the nav dropdown uses `navigateInNewTab()` (always opens a new tab). Neither list is derived from the other — they are maintained in parallel.

- `HomePage.vue`, `HomePageTile.vue`, `useHomeDateInfo.ts`, `useDafYomiNavigation.ts`

### books-fs/

File system browser for the book catalog. Supports list, tiles, and full tree views with search.

- `BooksFsPage.vue` — main page
- `BooksFsTitleBar.vue` — breadcrumb + view toggle
- `BooksTreeView.vue` — list/tiles view
- `BooksFullTree.vue` — collapsible category tree
- `BooksSearchResults.vue` — search results
- `BooksBreadcrumb.vue` — navigation breadcrumb
- `useBooksFs.ts` — navigation and folder traversal
- `useBooksFsSearch.ts` — title + TOC entry search
- `booksCategoryTree.ts` — tree building and category metadata

### book-view/

The main book reader. Orchestrates a split pane (text above, commentary below), a TOC side panel, a floating search bar, and a toolbar.

- `BookViewPage.vue` — orchestrator
- `BookViewToolbar.vue` — zoom, search, TOC, bottom panel toggles
- `BookViewSplitPane.vue` — thin wrapper around `SplitPane` that wires the top/bottom slots
- `BookViewLinesContent.vue` — main text (virtual scroller, line selection)
- `BookViewSearchBar.vue` — floating search (query, mode, match navigation)
- `BookViewTocTree.vue` — TOC side panel (main + alt structures)
- `BookViewTocTreeSection.vue` — TOC section header
- `BookViewCommentaryPanel.vue` — thin wrapper passing props into `CommentaryView`
- `CommentaryView.vue` — commentary display grouped by book
- `CommentaryHeader.vue` — commentary book header (type selector, nav)
- `CommentaryHeaderNav.vue` — prev/next section navigation
- `CommentaryFilterPanel.vue` — dropdown to toggle individual commentary books on/off
- `CommentaryTreeViewNode.vue` — node in the commentary filter tree
- `CommentaryTypeDropdown.vue` — commentary type selector
- `useToc.ts` — loads TOC entries and alt TOC structures for a book; builds a path map (entry id → full breadcrumb string); exposes `getActiveTocEntry` and `getTocPath`
- `useLinesTable.ts` — paginated line fetching from the `line` table in chunks of 200; pre-allocates placeholder slots so the virtualizer has the correct total height immediately, then fills content as chunks arrive; exposes `prioritise(lineIndex)` to move a chunk to the front of the fetch queue
- `useCommentary.ts` — fetches linked commentary for a selected line (or range), groups results by connection type and category, returns `CommentaryGroup[]`
- `useBookViewSearch.ts` — content search (line-based)
- `useCommentarySearch.ts` — commentary search (flat index-based)
- `useBookView.ts` — central composable; owns all data loading, state, event handlers, and watchers. `BookViewPage.vue` is a shell that calls this.
- `useBookViewScrollSync.ts` — syncs active TOC entry and auto-selects commentary on scroll
- `useBookViewSessionRestore.ts` — restores per-book view state from IDB on mount
- `useCommentaryNavigation.ts` — next/prev section navigation for the commentary panel
- `usePinnedCommentary.ts` — tracks the pinned commentary book with default-commentator fallback
- `useTocScrollTracking.ts` — tracks programmatic TOC scrolls to suppress active-entry updates during animation
- `bookViewTypes.ts` — shared types: `SearchMode`, `SidePanelMode`

### search-db/

Full-text search using Bloom filters. Supports category/book filters and caches results.

- `SearchPage.vue` — main page
- `SearchBar.vue` — search input + filter toggle
- `SearchResultsList.vue` — results (virtual scroller)
- `SearchFilterPanel.vue` — category/book filter tree
- `SearchFilterNode.vue` — filter tree node
- `SearchIndexingOverlay.vue` — indexing progress overlay
- `useBloomSearch.ts` — Bloom filter search execution and caching
- `useIndexingStatus.ts` — indexing status polling
- `useSearchFilters.ts` — filter state (checked books/categories), result filtering, and result click handler
- `searchTypes.ts` — TypeScript types

### settings/

App settings across three tabs: general, reading, and advanced. Also contains the setup wizard.

- `SettingsPage.vue` — three-tab page shell
- `SettingsGeneralPane.vue` — general tab: theme, zoom, toolbar position, reading behavior, divine name censoring
- `SettingsReadingPane.vue` — reading tab: book and commentary font/size/padding settings
- `SettingsAdvancedPane.vue` — advanced tab: database path picker (hosted only), reset settings, full app reset
- `SettingRow.vue`, `HintIcon.vue`, `SliderSetting.vue`, `ToggleGroup.vue`
- `ThemePicker.vue`, `FontDisplaySettings.vue`, `FontSelector.vue`
- `useSettingsPage.ts`
- `SetupWizard.vue` — first-launch onboarding wizard

### hebrew-books/

HebrewBooks catalog browser with download history.

- `HebrewBooksPage.vue`, `HebrewBooksListItem.vue`
- `useHebrewBooks.ts`, `hebrewBooksCatalog.ts`

### conversions/

Halachic unit converter. Singleton route `/midot`. Converts between biblical, Talmudic, and modern units across six systems (length, area, volume, weight, coins, time) with support for multiple halachic opinions.

- `MidotPage.vue` — full converter UI with opinion selector and conversion explanation
- `midot.ts` — all conversion logic (`convert`, `toMetric`, `explainConversion`)
- `units/` — unit definitions per measurement system; `types.ts` for shared types

### pdf/

PDF and Word document viewer. Embeds a PDF.js iframe.

- `PdfViewPage.vue`

### kiwix/

Kiwix ZIM file reader. Embeds Kiwix JS in an iframe to render offline ZIM archives (Wikipedia, reference works, etc.). FTS is disabled by default to avoid loading the Xapian WASM index; a future per-tab setting can enable it.

- `KiwixViewPage.vue` — iframe wrapper loading `/kiwix/www/index.html` with the ZIM virtual host URL

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

### layout/

App shell components.

- `AppTitleBar.vue`, `AppPageView.vue`, `AppTitleBarTabDropdown.vue`, `AppTitleBarNavDropdown.vue`

`AppTitleBarNavDropdown` is the hamburger nav menu. Its destination list mirrors the tiles in `HomePage.vue` — see the `home/` section for the sync rule.

### common/

Shared reusable components used across features.

- `TreeView.vue`, `TreeNode.vue` — generic tree
- `treeTypes.ts` — `TreeNodeItem` interface; import from here, never from `TreeNode.vue`
- `SplitPane.vue` — resizable split pane
- `BottomSearchBar.vue`, `ContextMenu.vue`, `ConfirmDialog.vue`, `LoadingAnimation.vue`
- `IconTreeRtl.vue` — `IconTextBulletListTree` pre-flipped for RTL layout

## Pinia Stores (`src/stores/`)

**tabStore** — tab lifecycle, navigation, and all per-tab/per-book state persistence. The central store — most features read from it.

**bookViewStore** — book viewer UI state: toolbar visibility, floating search bar position, per-tab+book zoom map. Exposes a reactive `zoom` computed for the active tab+book.

**settingsStore** — all app-wide settings (fonts, sizes, padding, zoom, diacritics, censoring, etc.). Each setting has its own IDB key and is watched individually.

**booksDataStore** — lazy-loaded book catalog. Fetches all categories and books on first access, builds the category tree, assigns period metadata.

**workspaceStore** — workspace management. All tab/book IDB keys are workspace-scoped; switching workspaces changes `activeId` and reloads tabs.

**pdfStore** — PDF and Word file handling. Manages conversion state, HebrewBooks download state, and session restore for PDF tabs. Listens to C# push events (`conversionStarted`, `hbPdfReady`, `hbPdfCancelled`).

**zimStore** — Kiwix ZIM file handling. Manages virtual host URL and session restore for `/kiwix-view` tabs. Listens to the `zimReady` C# push event. No conversion pipeline — ZIM files are always local and served directly.

**searchCacheStore** — LRU cache for search results (capped at 100 entries), stored in `app-search-cache` IDB.

**hebrewBooksHistoryStore** — HebrewBooks download history, stored in `app-hb-history` IDB, LRU-capped at 25 entries.

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

### db.ts

The database access layer. Exports:

- `isHosted` — true when running inside C# WebView2 host (or in dev mode)
- `dbReady` — reactive ref, true once a DB path is available
- `query<T>(sql, params)` — executes SQL via `window.__webviewQuery` (C# host) or a `/query` POST to the Vite dev middleware
- `onWebviewEvent(fn)` — subscribe to C# push events

### bridge.ts

C# host actions for file operations. All functions have dev fallbacks.

- `pickFile()` — native file picker (PDF + Word)
- `restoreLocalPdf(filePath)` — re-register virtual host for a local file
- `restoreHbPdf(bookId, bookTitle, tabId)` — restore HebrewBooks PDF from cache
- `disposePdfHost(filePath)` — decrement virtual host ref count on tab close
- `callBridgeAction(name, ...params)` — call any C# action with positional params (used by search/indexing)
- `resetHostApp()` — full app reset: deletes Bloom index, resets C# settings, reloads
- `resetSearchIndex()` — resets the Bloom search index on the C# side

### queries.sql.ts

All raw SQL strings for the frontend live here. No inline SQL anywhere else in the Vue/TypeScript codebase — every query a composable or store needs must be added to this file and imported from it.

The one exception is `ZayitDbManager.cs` in the C# backend, which owns the SQL used exclusively by the Bloom filter pipeline (indexing and search result hydration). That SQL never crosses into the frontend and is not duplicated in `queries.sql.ts`. No other C# file may contain inline SQL — `DbHandler.cs` is a passthrough that executes whatever SQL the frontend sends; it has no queries of its own.

## Utilities (`src/utils/`)

**persistence.ts** — the only file that touches IndexedDB and localStorage directly. All stores import from here; components and composables never do. Manages 3 IDB databases, all key patterns, LRU cap for lastread, the app reset mechanism, and the `__pendingReset` localStorage flag.

**commentaryNav.ts** — commentary section navigation (next/prev section, TOC-aware).

**hebrewTextProcessing.ts** — diacritics handling and text normalization for Hebrew.

**censorDivineNames.ts** — divine name censoring (replaces ה with ק).

**normalizeText.ts** — `normalize(s)`: lowercases and strips Hebrew/ASCII quote characters. Import this as the base normalization step before any search comparison.

**tocSearchUtils.ts** — TOC search path building (`buildTocSearchPaths`), query splitting (`splitQuery`), and ordered subsequence word matching (`matchWords`).

**detectFonts.ts** — `detectAvailableFonts()` uses canvas measurement to detect which Hebrew and general fonts are installed. Used by `FontSelector.vue`.

**scrollToIndexWithRetry.ts** — virtual scroller scroll-to-index with retry for async rendering.

**resetState.ts** — exports a single `resetting` ref that is set to `true` just before an app reset/reload, blocking all interaction until the page reloads.

**hebrewLearning.ts** — `getDailyLearning(hd)` returns today's schedule for all daily learning cycles (Daf Yomi, Mishna Yomi, Nach Yomi, Rambam, etc.). Used by the home page and the calendar weekly view.

**booksCategoryTree.ts** — pure data logic for the book catalog tree. Exports `buildTree`, `assignFullPaths`, `findCategoryMeta`, `ensureBookSearchMetadata`, and the `BookRow`, `CategoryRow`, `CategoryNode` types. No Vue or Pinia dependencies.

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
5. Restore persisted PDF tabs via `pdfStore.restoreTab()`
6. Mount app to `#app`
7. `initPdfThemeObserver()` — sync PDF iframe theme with app theme
8. `booksDataStore.ensureLoaded()` — lazy-load book catalog in background

## C# Backend

The C# project (`CSharpBackend/`) hosts the Vue app in a WebView2 control.

- Vue builds as a single-file bundle (`vite-plugin-singlefile`) — all JS/CSS inlined into one `index.html`
- `Kezayit.targets` runs `npm run build` after every C# build and copies `dist/` to `bin/{Config}/kezayit/`
- C# injects `window.__webviewQuery`, `window.__webviewAction`, `window.__webviewDbReady` before the app boots
- Push events from C# arrive via `window.__onWebviewEvent`
- Target: .NET 4.8, C# 7.3

Key C# handlers:

- `JsBridge.cs` — handles `__webviewAction` calls (file picker, PDF restore, virtual host management)
- `WebBridge.cs` — WebView2 setup, message routing
- `DbAccess.cs` / `DbHandler.cs` — SQLite access via Dapper
- `SearchHandler.cs` — Bloom filter search
- `HebrewBooksHandler.cs` — HebrewBooks download via WebView2 browser engine
- `PdfHandler.cs` — PDF virtual host management
- `ZimHandler.cs` — ZIM virtual host management (Kiwix reader)
- `WordToPdfConverter.cs` — Word-to-PDF conversion
