# home

Home page with navigation tiles and a unified quick-search bar.

**HomePage.vue** — page shell. Owns the layout (scrolling search + tile group above, fixed date bar below), the search input markup, tile keyboard traversal, and cold-start focus. It wires the composables below and holds no search, tile, or date logic of its own.

**HomePageTile.vue** — single tile with a filled colored icon and label. Emits `tap` (carrying whether Ctrl/⌘ was held), `togglePin`, and `remove`. Add new tiles via `useHomeTiles.ts`, not by editing the template.

**useHomeTiles.ts** — the tile grid's data: the static navigation tile list, the recently-opened list and its per-route icon map, how many recently-opened tiles fit alongside the static ones, and the pin/remove actions. The static tile list must always be kept in sync with the destination list in `AppTitleBarNavDropdown.vue` — when adding, removing, or renaming a destination, update both. Neither list is derived from the other.

**useHomeSearchBar.ts** — the hero search bar's own state: dropdown open/closed, its fixed-position anchor (computed once on open, never tracked reactively — reactive tracking fights the dropdown's `scrollTop`), the animated typing placeholder, and the input's keyboard handling. Knows nothing about where results come from or where selecting one navigates.

**useHomeSearchNavigation.ts** — turns a dropdown selection or a recently-opened tile into a navigation. Owns all four `onSelect*` handlers (catalog book, catalog TOC entry, HebrewBooks book, local file), `openRecentEntry`, and `openFullTextSearch`. Every handler honours Ctrl/⌘-click for open-in-new-tab. This is the only file in the feature that touches `tabStore` / `localFileStore` / the bridge.

**HomePageDateBar.vue** — the bottom bar: clock, nearest-zman button with its teleported popup, Hebrew date, and Daf Yomi. Self-contained, including its own zmanim wiring and popup anchoring. The Hebrew date rolls over at צאת הכוכבים rather than civil midnight, reusing `useNextZman`'s tzeit as the single source of truth.

**useHomeDateBarFit.ts** — keeps the date bar on one line by dropping optional items by priority (clock first, then the zman) instead of wrapping. Handles resizes itself; call `remeasure()` when the bar's *content* changes.

**homeDateInfo.ts** — loads the Hebrew date and Daf Yomi for the date bar. No `use` prefix: a plain module with lazy-loaded shared state, not a reactive composable.

**dafYomiNavigation.ts** — navigates to the Daf Yomi book and line when the user taps the Daf Yomi entry. No `use` prefix, same reason.

**useNextZman.ts** / **NextZmanPopup.vue** — nearest-zman computation and the all-times popup, both consumed by `HomePageDateBar.vue`.

**useHomeSearch.ts** — unified search composable. Takes the search query ref and fans it out across three sources simultaneously: book catalog (instant, title-only, in-memory), HebrewBooks catalog (debounced 300ms, async via C# bridge), and Document Locator file system search (debounced 300ms, async via C# bridge). HebrewBooks and file search only fire when `isHosted`. Each source writes to its own result ref so the dropdown renders partial results as they arrive. Each source is capped at 5 results. Minimum query length is 2 characters.

Otzaria addin rules are not duplicated here — `features/local-file-search/otzariaAddins.ts` owns them, and this composable, the address bar and the file-search page all import from it. That covers rewriting the "תוספים" shorthand to the prefix the index actually stores, ranking files first for such a query, and stripping the prefix off the title when a result opens. Anything reachable from `fileSystemSearch` must go through that module, or the same query behaves differently depending on which input the user typed it into.

**HomeSearchDropdown.vue** — results dropdown rendered below the search bar. Groups results into three sections (ספרים, היברו-בוקס, קבצים) with section headers and loading spinners. Shows only sections that have results or are loading. Emits `selectCatalogBook`, `selectHebrewBook`, `selectFile` — all navigation is handled by `HomePage.vue`, not the dropdown. `selectFile` carries the whole `FileSearchResult` rather than a path/name pair, because the consumer needs `addinName` to title and flag an Otzaria addin. Also accepts optional `tabs`/`activeTabId`/`recentEntries` props (used by the title-bar `AddressBar`, not by HomePage): when non-empty, an open-tabs section (לשוניות פתוחות) and a recently-opened section (נפתחו לאחרונה, `recentlyOpenedStore` entries) render above the search sections and emit `selectTab`/`closeTab`/`selectRecent`.

## Search bar wiring

Split across three files: `HomePage.vue` holds the input markup and refs, `useHomeSearchBar.ts` owns the dropdown's open/anchor/keyboard state, and `useHomeSearch.ts` fetches results.

The search bar wrapper (`searchBarRef`, in `HomePage.vue`) is the `useDropdownClose` target — clicking anywhere outside the wrapper closes the dropdown. The dropdown opens on `@input` when the query is ≥ 2 chars, and on `@focus` when results already exist. Async results (HB, files) open it once they resolve, via `openWhenAsyncResultsArrive`.

`close()` and `reset()` are deliberately different: `close()` only hides the dropdown and preserves what the user typed (used when clicking away), while `reset()` also clears the query and the fetched results (used on Escape, on submit, and after any navigation).

Dependencies point one way: `useHomeSearchBar` knows nothing about navigation. It reports intent via `onSubmit` and `onDropdownKeydown`, and `HomePage.vue` decides what those mean. Keep it that way — having the bar call into `useHomeSearchNavigation` directly would create a cycle, since navigation needs `reset` from the bar.

Keyboard navigation follows the combobox model (`useInputListNavigation`): focus never leaves the input. The input's keydowns are forwarded to `HomeSearchDropdown.onSearchInputKeydown`, which moves a highlight through the flattened item list; Enter with a highlight activates the row, Enter without one submits the full-text search. While the user is arrowing, the page pauses `useHomeSearch` so async results don't reshuffle the list under the highlight; typing resumes it.

## Tile visibility rules

The first two tiles are DB-dependent and swap based on DB state. All other tiles are always visible regardless of DB state.

When `isHosted && !dbReady`: the first two tiles are **הורד מסד ספרים** and **בחר מסד ספרים**, which let the user set up a database.

When DB is available (or not hosted): the first two tiles are **ספרים** and **חיפוש**, which require a DB to function.

Never hide or conditionally render any tile beyond these first two — the rest (פתח קובץ, היברו-בוקס, חיפוש קבצים, מילון, לוח שנה, מידות ושיעורים, סביבות עבודה, הגדרות) are always shown.
