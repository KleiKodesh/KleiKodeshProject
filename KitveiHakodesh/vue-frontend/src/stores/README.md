# src/stores

Pinia stores, plus a couple of plain state-infrastructure modules. Owns all *structured* persistence — everything in IndexedDB, and the localStorage slices below. Features may read and write their own scalar settings through `lsGet`/`lsSet` directly; they must not touch an IDB API.

Not everything here is a Pinia store. `tabStatePersistence.ts` and `bookLastRead.ts` are plain modules: they hold no reactive state, but both `tabStore` and feature composables use them, so they belong in neither a feature folder nor `utils`. Anything named `*Store.ts` is a Pinia store; anything else here is not.

**Storage keys and schemas live with their owner, never centrally.** `persistence.ts` is a driver and holds neither. Each module defines the disk names for the values it owns and the shape of what it writes:

| Owner | Defines |
| --- | --- |
| `settingsStore` | the ~42 app settings keys, and `clearPersistedSettings` (settings-vs-structural reset) |
| `bookViewStore` | the book-view UI-state keys (`bookView.*`, `splitView.*`) |
| `workspaceStore` | `Workspace`/`WorkspaceList`, `workspaces.list`, the exported `tabsListKey(wsId)`, and `deleteWorkspaceData` |
| `tabStatePersistence` | `TabState`/`BookState`, and the `app-tabs` key layout (`tab:`, `book:`) |
| `bookLastRead` | `LastReadState`, and the 1000-entry on-disk cap |
| `themeStore`, feature modules | their own single keys |

Every localStorage key is namespaced `area.name` so two owners can never claim the same slot — localStorage has one flat namespace, and a collision silently overwrites with no error. Never add a bare key. Structural keys use the `tabs:` and `workspaces.` prefixes, which is how `clearPersistedSettings` preserves them without a hardcoded name list.

A database may be opened outside `persistence.ts` only when its schema needs something the driver cannot express — in-line keys, secondary indexes, or multiple object stores. `hebrewBooksHistoryStore` qualifies (`keyPath: 'id'` plus a `lastAccessed` index); such an owner must also export a `drop*` function for `appResetState` to call.

Initialization order matters: `workspaceStore` must init before `tabStore`. See `main.ts`.

**tabStore** — central store. Tab lifecycle and navigation. Most features read from it. It re-exports the persistence API from the two modules below, so callers keep using `tabStore.getBookViewState(...)` etc. as before.

**tabStatePersistence.ts** — the `app-tabs` slice: everything keyed by workspace + tab. `TabState` get/set (search filters, scroll restore, per-tab zoom), `BookState` get/set/clear (reading position and commentary layout, per tab *and* book), the in-memory book-state cache, and `deleteAllStateForTab(tabId)`. Use that last one for teardown — it drops the `TabState`, every `BookState` beneath the tab, and the cache entries together, and every close path calls it. A closed tab's reading position is not lost with it: `disposeClosedTab` copies it into the tab's location record first (see `navLocation.ts`). Also exposes `peekTabViewState` / `peekBookViewState`, synchronous reads of the write-through caches, for callers that must capture a position mid-navigation and cannot await.

**The three tab collections.** The app follows the browser model, and the three are deliberately separate — same idea of "a place", different scope and different eviction:

| | browser equivalent | scope | lifetime | eviction |
|---|---|---|---|---|
| `tabStore.tabs` | open tabs | window | until closed | closed → removed |
| `recentLocations` | History + Recently-closed | app-wide, persisted | outlives the tab | LRU, deduped per document |
| `navHistory` | Back/Forward stack | ONE tab | dies with the tab | in memory only |

**navLocation.ts** — the shared record. A `NavLocation` is SELF-DESCRIBING: it carries its own `position`, so it does not depend on any live tab. That is what decouples recents from tabs. Also owns `locationKey` (document identity, for dedupe) and `isRecordableLocation` (home, the singletons and an empty search page are not places worth remembering — navigating onto one records nothing, so pressing Home does not bury the book you were reading).

**recentLocations.ts** — the `app-recent-tabs` slice: locations visited, persisted per workspace, LRU-capped at `RECENT_LOCATIONS_MAX` (50) and deduped per document so revisiting a book bumps its row rather than stacking duplicates. Selecting one navigates the current tab; removing one closes nothing.

**navHistory.ts** — per-tab Back/Forward, in memory. A list plus a cursor rather than two stacks, because navigating from anywhere other than the end must TRUNCATE the forward branch — the rule that makes back-then-navigate behave as people expect.

Recording happens in `tabStore.applyTabPatch`, and only when a patch changes which DOCUMENT a tab shows (`isNavigationPatch`). `tocPath` arrives on every scroll event, so treating it as a navigation would push a history frame per scroll. The native chrome tab strip mirrors `tabs`, never these lists. Patches that merely COMPLETE an already-recorded navigation — a file restore arriving with the served URL and route — go through `tabStore.updateTabWithoutHistory`, so Back never needs two presses to leave a restored file.

**bookLastRead.ts** — the `app-lastread` slice: the global per-book last-read position. Not tab-scoped, and it deliberately outlives tab close — that is what separates it from `BookState`, which has nearly the same shape but describes one tab's view of a book. Owns `LastReadState` and both caps: the 200-entry in-memory cache and the 1000-entry on-disk cap. Always write through `setLastReadPos()` so both are applied. Use `updateActiveTab` for in-place navigation, `openTab` only for explicitly creating a new tab, and `navigateToSingleton(route, pane, openInNewTab)` for singleton routes (singletons are enforced per-pane).

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
