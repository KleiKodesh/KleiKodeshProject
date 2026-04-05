# Architecture Map

This is a functional architecture map of the app — a Hebrew book reader, mobile-first, strictly RTL.

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

Completion: `settings.completeSetup()` sets `setupDone = true` and persists it to IDB at key `setupDone` (via `KEYS.SETTINGS_SETUP_DONE` in `idbPersistence.ts`). Once set, the wizard never shows again.

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

| Route          | Component                  | Kind                            |
| -------------- | -------------------------- | ------------------------------- |
| `/`            | `HomePage.vue`             | shared instance                 |
| `/books`       | `BooksFsPage.vue`          | singleton                       |
| `/book-view`   | `BookViewPage.vue`         | multi-instance (keyed by tabId) |
| `/search`      | `SearchPage.vue`           | multi-instance (keyed by tabId) |
| `/settings`    | `SettingsPage.vue`         | singleton                       |
| `/hebrewbooks` | `HebrewBooksPage.vue`      | singleton                       |
| `/pdf-view`    | `PdfViewPage.vue`          | multi-instance                  |
| `/workspaces`  | `WorkspaceManagerPage.vue` | singleton                       |

## Feature Folders (`src/components/`)

### home/

Home page with 6 navigation tiles: Books, Search, Open File, HebrewBooks, Workspaces, Settings.

- `HomePage.vue`, `HomePageTile.vue`

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
- `BookViewCommentaryPanel.vue` — commentary panel container
- `CommentaryView.vue` — commentary display grouped by book
- `CommentaryHeader.vue` — commentary book header (type selector, nav)
- `CommentaryHeaderNav.vue` — prev/next section navigation
- `CommentaryTreePanel.vue` — commentary TOC tree
- `CommentaryTreeViewNode.vue` — commentary TOC node
- `CommentaryTypeDropdown.vue` — commentary type selector
- `useToc.ts` — loads TOC entries and alt TOC structures for a book; builds a path map (entry id → full breadcrumb string); exposes `getActiveTocEntry` and `getTocPath`
- `useLinesTable.ts` — paginated line fetching from the `line` table in chunks of 200; pre-allocates placeholder slots so the virtualizer has the correct total height immediately, then fills content as chunks arrive; exposes `prioritise(lineIndex)` to move a chunk to the front of the fetch queue
- `useCommentary.ts` — fetches linked commentary for a selected line (or range), groups results by connection type and category, returns `CommentaryGroup[]`
- `useBookViewSearch.ts` — content search (line-based)
- `useCommentarySearch.ts` — commentary search (flat index-based)

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

App settings across three tabs: general, reading, and reset. Also contains the setup wizard.

- `SettingsPage.vue`, `SettingRow.vue`, `SliderSetting.vue`, `ToggleGroup.vue`
- `ThemePicker.vue`, `FontDisplaySettings.vue`, `FontSelector.vue`
- `useSettingsPage.ts`
- `SetupWizard.vue` — first-launch onboarding wizard

### hebrew-books/

HebrewBooks catalog browser with download history.

- `HebrewBooksPage.vue`, `HebrewBooksListItem.vue`
- `useHebrewBooks.ts`, `hebrewBooksCatalog.ts`, `hebrewBooksHistory.ts`

### pdf/

PDF and Word document viewer. Embeds a PDF.js iframe.

- `PdfViewPage.vue`

### workspace/

Workspace CRUD UI.

- `WorkspaceManagerPage.vue`

### layout/

App shell components.

- `AppTitleBar.vue`, `AppPageView.vue`, `AppTitleBarTabDropdown.vue`, `AppTitleBarNavDropdown.vue`

### common/

Shared reusable components used across features.

- `TreeView.vue`, `TreeNode.vue` — generic tree
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

**searchCacheStore** — LRU cache for search results (capped at 100 entries), stored in `app-settings` IDB under `search:` prefix.

## Composables (`src/composables/`)

**useAppNavigation.ts** — central navigation handler. Routes singletons via `navigateToSingleton()`, handles file picker, external links, and search navigation.

**useVirtualScrollerKeys.ts** — keyboard nav for virtual scrollers (`Ctrl+Home`, `Ctrl+End`). Required on every component that uses `@tanstack/vue-virtual`. The scroll container must have `tabindex="0"`.

**useZoom.ts** — zoom handler for keyboard (`Ctrl+±/0`), wheel (`Ctrl+scroll`), and pinch (2-finger touch). Config: MIN=50, MAX=200, DEFAULT=100, STEP=10.

**useListKeyNav.ts** — arrow-key + Home/End navigation for plain DOM lists; tracks `focusedIndex` and scrolls the focused item into view.

**useTextSelectionKeys.ts** — `Ctrl+A` (select all text in a container) and `Ctrl+F` (open search) scoped to a specific element.

**useTileGridKeys.ts** — 2D arrow-key navigation for tile grids; computes column count from container width to handle Up/Down correctly.

**useVirtualListKeyNav.ts** — arrow-key + `Ctrl+Home`/`Ctrl+End` navigation for `@tanstack/vue-virtual` lists; calls `scrollToIndex` on the virtualizer.

**useLineCopy.ts** — intercepts the browser `copy` event on a scroller element; when the user has selected all, writes each line as a `<div>` in `text/html` and strips HTML tags for `text/plain`, so copied text has no inline line breaks.

## Host & Database (`src/host/`)

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

### queries.sql.ts

All raw SQL strings live here. No inline SQL anywhere else in the codebase.

## Utilities (`src/utils/`)

**idbPersistence.ts** — the only file that touches IndexedDB directly. All stores import from here; components and composables never do. Manages 3 databases, all key patterns, LRU cap for lastread, and the app reset mechanism.

**commentaryNav.ts** — commentary section navigation (next/prev section, TOC-aware).

**hebrewTextProcessing.ts** — diacritics handling and text normalization for Hebrew.

**censorDivineNames.ts** — divine name censoring (replaces ה with ק).

**normalizeText.ts** — strips ASCII and Hebrew quote characters and lowercases; used as the base normalization step for all search comparisons.

**tocSearchUtils.ts** — TOC search path building (`buildTocSearchPaths`), query splitting (`splitQuery`), and ordered subsequence word matching (`matchWords`).

**scrollToIndexWithRetry.ts** — virtual scroller scroll-to-index with retry for async rendering.

**resetState.ts** — exports a single `resetting` ref that is set to `true` just before an app reset/reload, blocking all interaction until the page reloads.

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

The C# project (`ZayitVueHost/Kezayit`) hosts the Vue app in a WebView2 control.

- Vue builds as a single-file bundle (`vite-plugin-singlefile`) — all JS/CSS inlined into one `index.html`
- `ZayitVue.targets` runs `npm run build` after every C# build and copies `dist/` to `bin/{Config}/kezayit/`
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
- `WordToPdfConverter.cs` — Word-to-PDF conversion
