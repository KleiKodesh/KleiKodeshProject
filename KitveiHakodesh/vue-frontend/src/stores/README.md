# src/stores

Pinia stores. The only layer (besides `persistence.ts`) allowed to read from or write to IndexedDB or localStorage. Components and composables never import from `persistence.ts` directly — they go through a store.

Initialization order matters: `workspaceStore` must init before `tabStore`. See `main.ts`.

**tabStore** — central store. Tab lifecycle, navigation, and all per-tab and per-book state persistence. Most features read from it. Use `updateActiveTab` for in-place navigation, `openTab` only for explicitly creating a new tab, and `navigateToSingleton` for singleton routes.

**bookViewStore** — book viewer UI state: toolbar visibility, search bar position, and per-tab+book zoom map. Read `zoom` as a computed for the active tab and book.

**settingsStore** — all app-wide settings. Each setting has its own IDB key and is watched individually so only the changed key is written. Add new settings here, not as local component state.

**booksDataStore** — lazy-loaded book catalog. Call `ensureLoaded()` to trigger the load. Do not fetch categories or books from the DB anywhere else.

**workspaceStore** — workspace management. All tab and book IDB keys are workspace-scoped. Switching workspaces changes `activeId` and reloads tabs.

**pdfStore** — PDF and Word file state. Manages conversion, HebrewBooks download state, and PDF tab session restore. Listens to C# push events. Any code opening or closing a PDF tab should go through this store.

**searchCacheStore** — LRU cache for Bloom filter search results, capped at 100 entries. Do not cache search results anywhere else.

**hebrewBooksHistoryStore** — owns the `app-hb-history` IDB database. Tracks which HebrewBooks PDFs the user has downloaded, LRU-capped at 25 entries. All history reads and writes go through here — do not import from `persistence.ts` for this database anywhere else.
