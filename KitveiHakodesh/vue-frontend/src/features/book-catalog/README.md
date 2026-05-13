# book-catalog

Book catalog browser. List, tiles, and full collapsible tree views with two-tier search.

**BookCatalogPage.vue** - main page, orchestrates the title bar, search bar, and view switching between list, tiles, and tree via a `<component :is>` map.

**BookCatalogTreeView.vue** - collapsible category tree showing the full catalog hierarchy.

**BookCatalogSearch.vue** - renders book matches and TOC entry matches.

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
