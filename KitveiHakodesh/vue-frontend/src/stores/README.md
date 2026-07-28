# src/stores

Pinia stores. The only layer (besides `persistence.ts`) allowed to read from or write to IndexedDB or localStorage. Components and composables never import from `persistence.ts` directly — they go through a store.

Initialization order matters: `workspaceStore` must init before `tabStore`. See `main.ts`.

**tabStore** — central store. Tab lifecycle, navigation, and all per-tab and per-book state persistence. Most features read from it. Use `updateActiveTab` for in-place navigation, `openTab` only for explicitly creating a new tab, and `navigateToSingleton(route, pane, openInNewTab)` for singleton routes (singletons are enforced per-pane).

Split-view panes: one flat `tabs` array holds both panes' tabs, and `Tab.pane` is the sole discriminator — absent or `1` means pane 1, `2` means pane 2. There is no second store, shell id, or per-pane array. Pane 1 and pane 2 have parallel function pairs: `openTab`/`openPane2Tab`, `switchTab`/`switchPaneTab`, `closeTab`/`closePane2Tab`, `updateActiveTab`/`updatePane2ActiveTab`, with active ids `activeTabId`/`pane2ActiveTabId` and computeds `pane1Tabs`/`pane2Tabs`/`activeTabForPane(pane)`. Pane-1 select and update also do the MRU move-to-front — the only tab reordering in the app (no drag UI). `closeAllTabs()` resets pane 1 only, leaving pane 2 intact; `ensurePane2HasTab()` guarantees pane 2 is never empty. Components inside a shell should not call the `*Pane2*` functions directly — go through `useAppShellPane`/`usePaneNavigation` (see `src/composables`).

Persistence: the tab list is saved to localStorage per workspace under `KEYS.tabsList(wsId)` as `{ tabs, activeTabId, nextId }`; singleton-route tabs and in-memory-only fields (e.g. `localFileVirtualUrl`) are stripped before saving. Per-tab and per-book view state go to IndexedDB (`KEYS.tab`, `KEYS.book`).

**bookViewStore** — book viewer UI state: toolbar visibility, search bar position, and per-tab+book zoom map. Read `zoom` as a computed for the active tab and book. Also owns split-view state (not tabStore): `splitViewEnabled`, `splitViewFraction` (0.5 default), `focusedPaneId` (1|2), mutated via `toggleSplitView`/`disableSplitView`/`setSplitViewFraction`/`setFocusedPane`; persisted under `KEYS.SETTINGS_SPLIT_VIEW` / `KEYS.SETTINGS_SPLIT_VIEW_FRACTION`.

**settingsStore** — all app-wide settings. Each setting has its own localStorage key and is watched individually so only the changed key is written. Add new settings here, not as local component state.

To add one: put a default in `DEFAULTS`, declare a `ref`, call `loadSetting(KEYS.X, theRef)` inside `init()`, call `persistSetting(theRef, KEYS.X)`, and export the ref. Consumers then just assign it — the watcher persists. Per-feature *display* preferences belong here too (`booksView`, `fileSearchSortOrder`); only genuinely per-tab state belongs on the tab.

**booksDataStore** — lazy-loaded book catalog. Call `ensureLoaded()` to trigger the load. Do not fetch categories or books from the DB anywhere else.

**workspaceStore** — workspace management. All tab and book IDB keys are workspace-scoped. Switching workspaces changes `activeId` and reloads tabs.

**localFileStore** — Local file and Word file state. Manages conversion, HebrewBooks download state, and PDF/HTML tab session restore. Listens to C# push events. Any code opening or closing a local file tab should go through this store.

**searchCacheStore** — LRU cache for full-text search results, capped at 100 entries. Do not cache search results anywhere else.

**hebrewBooksHistoryStore** — owns the `app-hb-history` IDB database. Tracks which HebrewBooks PDFs the user has downloaded, LRU-capped at 25 entries. All history reads and writes go through here — do not import from `persistence.ts` for this database anywhere else.

**recentlyOpenedStore** — owns the `app-recently-opened` IDB database. Tracks the last 16 documents opened across /book-view, /pdf-view, /html-view, and /txt-view. LRU with bump-to-front on re-open. Loaded lazily on first access (no boot-time cost). All recently opened reads and writes go through here.

**pdfOcrStore** — Pinia store for PDF OCR state. Manages OCR activation toggle, script selection (Hebrew/Rashi/mixed), and the skip-existing-text flag.
