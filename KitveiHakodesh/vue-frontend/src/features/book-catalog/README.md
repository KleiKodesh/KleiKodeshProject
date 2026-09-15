# book-catalog

Book catalog browser: list and tiles views, with two-tier search.

**BookCatalogPage.vue** - main page, orchestrating the address bar and the active view. It owns which face the bar shows (path or search field) — see the `showSearch` note in the file — and forwards the search input's keys to whichever view is on screen. Which view that is comes from `settingsStore.booksView`, an app-wide preference shared by every `/books` tab, not per-tab state; assigning it persists it, so there is nothing to load on mount.

**BookCatalogView.List.vue** - the catalog list, with folder and book rows.

**BookCatalogView.Tiles.vue** - the same items as a wrapping tile grid, sharing the home page's tile look. Its keyboard steps a whole row at a time and takes left/right within a row (`getColumnsPerRow` / `countGridColumns` in `useInputListNavigation`).

**BookCatalogTitleBar.vue** - the address bar: one pill (`TopSearchBar`) holding home, the breadcrumb or the search field, and — beside the search icon — the list/tiles toggle.

**BookCatalogSearch.vue** - renders book matches and TOC entry matches, in a list or as tiles to match the active view.

**BookCatalogBreadcrumb.vue** - breadcrumb trail for the current folder path.

**useBookCatalog.ts** - navigation state and folder traversal. Use this for any logic that moves through the folder hierarchy.

**useBookCatalogSearch.ts** - two-phase search orchestrator. Phase 1 (instant, synchronous) filters the in-memory book catalog by word-prefix matching against each book's `searchPath`. Phase 2 (debounced, async) kicks in when Phase 1 finds nothing and delegates entirely to `bookCatalogSearchTocHeuristics.ts`. Both phases normalize the query through `normalizeBookPath` from `bookCatalogSearchNormalizer.ts` - add new normalization rules there, not here.

**bookCatalogSearchTocHeuristics.ts** - the four-stage TOC fallback pipeline. Each stage is a named exported function so the logic is easy to follow, test, and extend independently:
- Stage 1 `splitQueryIntoBookAndTocParts` - finds the longest book-matching prefix of the query words; the remainder becomes the TOC search portion.
- Stage 2 `fetchTocRowsForBooks` - loads TOC entries for candidate books from the DB in sqrt-sized batches, strips redundant root entries, and yields between batches to keep the UI responsive. Returns null if cancelled.
- Stage 3 `searchTocRows` - builds a `SearchableTree` and scores all TOC entries against the TOC words.
- Stage 4 `buildTocResultItems` - converts matched nodes into `TocFsItem` objects for the UI.
The top-level `runTocHeuristics` function runs all four stages in sequence and accepts an `isCancelled` callback so stale searches abort cleanly. Add new heuristic stages here - do not add TOC search logic to `useBookCatalogSearch.ts`.

**bookCatalogTree.ts** - builds the category tree from flat DB rows, assigns full category paths to books, and computes period and root-category metadata. Negative IDs on categories or books indicate user-defined custom entries; `buildTree` always sorts them after DB entries at every level of the tree.

**bookCatalogSearch.ts** - inverted index, word lookup, scoring, and `filterBooksByWords`. Stores title-only token sets and counts per book in addition to the full-path token counts, enabling the four-tier ranking: total score → title match count → title token count → catalog tree order. See `SEARCH.md` for the full design.

**bookCatalogSearchNormalizer.ts** - all Hebrew-specific text normalization: abbreviation expansion, spelling variant rules, חסר/מלא decomposition, and ה-prefix stripping. Apply symmetrically to both indexed tokens and query words — never add normalization rules anywhere else.

**bookCatalogSearchMatcher.ts** - score constants (EXACT, PREFIX, NONE) and the per-token scoring function. Import score constants from here whenever comparing tiers.

**bookCatalogTocSearchCache.ts** - LRU disk cache for TOC search results (25 entries). Caches the output of Phase 2 so repeated queries skip DB round-trips entirely.

**SEARCH.md** — detailed design document for the book catalog search architecture.

