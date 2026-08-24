# home

Home page with navigation tiles and a unified quick-search bar.

**HomePage.vue** — page shell. Owns the layout (scrolling search + tile group above, fixed date bar below), the search input markup, tile keyboard traversal, and cold-start focus. It wires the composables below and holds no search, tile, or date logic of its own.

**HomePageTile.vue** — single tile with a filled colored icon and label. Emits `tap` (carrying whether Ctrl/⌘ was held), `togglePin`, and `remove`. Add new tiles via `useHomeTiles.ts`, not by editing the template.

**useHomeTiles.ts** — the tile grid's data: the static navigation tile list, the recently-opened list and its per-route icon map, how the dynamic tile budget is split between the folder and document groups, and the pin/remove actions. It also owns the grid's width cap (`gridMaxWidth`), which `HomePage.vue` binds as an inline `max-width`: one row is never wider than a full row of static tiles, so recently-opened tiles wrap underneath rather than extending the row on a wide window. The static tile list must always be kept in sync with the destination list in `AppTitleBarNavDropdown.vue` — when adding, removing, or renaming a destination, update both. Neither list is derived from the other.

**Popularity, not recency.** Both dynamic tile groups — the opened documents and the frequently-visited folders — rank by *time-decayed frequency*: each visit adds a point and accumulated points halve every 14 days, so what you open daily outranks what you glanced at an hour ago. The model lives in `stores/popularityScore.ts` and is shared by `recentlyOpenedStore` and `frequentFoldersStore`. Scores decay lazily (each entry records when its score was last computed), so there is no sweep and a long-closed app still comes back correctly aged.

**Frequently visited folders.** `stores/frequentFoldersStore.ts` counts which folders files are opened from. They appear as tiles between the static tiles and the document tiles, carrying the same pin and trash actions; clicking one opens the file dialog already pointed at that folder. Folders get **half** the dynamic tile budget (rounded down): they earn their points from the same opens the documents do, so an unsplit budget would let a morning's work in one folder push the documents off the page. The half-share is a ceiling, not a reservation: when there are fewer folders than that the documents take the slack, and with the documents switched off the folders take the whole budget. Visits are recorded in `tabStore.trackTabNavigation`, which deliberately skips HebrewBooks downloads — those land in our own cache directory, which the user never chose.

**useHomeSearchBar.ts** — the hero search bar's own state: dropdown open/closed, its fixed-position anchor (computed once on open, never tracked reactively — reactive tracking fights the dropdown's `scrollTop`), the animated typing placeholder, and the input's keyboard handling. Knows nothing about where results come from or where selecting one navigates.

**useHomeSearchNavigation.ts** — turns a dropdown selection or a recently-opened tile into a navigation. Owns all four `onSelect*` handlers (catalog book, catalog TOC entry, HebrewBooks book, local file), `openRecentEntry`, and `openFullTextSearch`. Every handler honours Ctrl/⌘-click for open-in-new-tab. This is the only file in the feature that touches `tabStore` / `localFileStore` / the bridge.

**HomePageDateBar.vue** — the bottom bar: clock, nearest-zman button with its teleported popup, Hebrew date, and Daf Yomi. Self-contained, including its own zmanim wiring and popup anchoring. The Hebrew date rolls over at צאת הכוכבים rather than civil midnight, reusing `useNextZman`'s tzeit as the single source of truth.

**useHomeDateBarFit.ts** — keeps the date bar on one line by dropping optional items by priority (clock first, then the zman) instead of wrapping. Handles resizes itself; call `remeasure()` when the bar's *content* changes.

**homeDateInfo.ts** — loads the Hebrew date and Daf Yomi for the date bar. No `use` prefix: a plain module with lazy-loaded shared state, not a reactive composable.

**dafYomiNavigation.ts** — navigates to the Daf Yomi book and line when the user taps the Daf Yomi entry. No `use` prefix, same reason.

**useNextZman.ts** / **NextZmanPopup.vue** — nearest-zman computation and the all-times popup, both consumed by `HomePageDateBar.vue`.

## Search bar wiring

The search engine and the results dropdown are NOT here — they live in `features/global-search/` (`useGlobalSearch.ts`, `GlobalSearchDropdown.vue`), because the title-bar `AddressBar` shares them. This feature owns only the home page's own instance of the search UI: `HomePage.vue` holds the input markup and refs, `useHomeSearchBar.ts` owns the dropdown's open/anchor/keyboard state, and `useHomeSearchNavigation.ts` turns selections into navigation. Result fetching comes from `useGlobalSearch`.

The search bar wrapper (`searchBarRef`, in `HomePage.vue`) is the `useDropdownClose` target — clicking anywhere outside the wrapper closes the dropdown. The dropdown opens on `@input` when the query is ≥ 2 chars, and on `@focus` when results already exist. Async results (HB, files) open it once they resolve, via `openWhenAsyncResultsArrive`.

`close()` and `reset()` are deliberately different: `close()` only hides the dropdown and preserves what the user typed (used when clicking away), while `reset()` also clears the query and the fetched results (used on Escape, on submit, and after any navigation).

Dependencies point one way: `useHomeSearchBar` knows nothing about navigation. It reports intent via `onSubmit` and `onDropdownKeydown`, and `HomePage.vue` decides what those mean. Keep it that way — having the bar call into `useHomeSearchNavigation` directly would create a cycle, since navigation needs `reset` from the bar.

Keyboard navigation follows the combobox model (`useInputListNavigation`): focus never leaves the input. The input's keydowns are forwarded to `GlobalSearchDropdown.onSearchInputKeydown`, which moves a highlight through the flattened item list; Enter with a highlight activates the row, Enter without one submits the full-text search. While the user is arrowing, the page pauses `useGlobalSearch` so async results don't reshuffle the list under the highlight; typing resumes it.

## Tile visibility rules

The first two tiles are DB-dependent and swap based on DB state. All other tiles are always visible regardless of DB state.

When `isHosted && !dbReady`: the first two tiles are **הורד מסד ספרים** and **בחר מסד ספרים**, which let the user set up a database.

When DB is available (or not hosted): the first two tiles are **ספרים** and **חיפוש**, which require a DB to function.

Never hide or conditionally render any tile beyond these first two — the rest (פתח קובץ, היברו-בוקס, חיפוש קבצים, מילון, לוח שנה, מידות ושיעורים, סביבות עבודה, הגדרות) are always shown.
