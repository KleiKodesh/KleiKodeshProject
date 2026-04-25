# books-fs

File system browser for the book catalog. List, tiles, and full collapsible tree views with two-tier search.

**BooksFsPage.vue** - main page, orchestrates the title bar, search bar, and top-level Books FS flow.

**BooksFsTitleBar.vue** - breadcrumb and view-mode toggle (list / tiles / full tree).

**BooksFsView.vue** - host for the non-search browsing views.

**BooksFsTreeView.vue** - collapsible category tree showing the full catalog hierarchy.

**BooksFsTileView.vue** - tiles view at the current breadcrumb level.

**BooksFsListView.vue** - list view at the current breadcrumb level.

**BooksFsSearch.vue** - renders book matches and TOC entry matches.

**BooksBreadcrumb.vue** - breadcrumb trail for the current folder path.

**useBooksFsViewItems.ts** - shared item activation and title helpers for the list and tile views.

**useBooksFs.ts** - navigation state and folder traversal. Use this for any logic that moves through the folder hierarchy.

**useBooksFsSearch.ts** - two-phase search orchestrator. Phase 1 (instant, synchronous) filters the in-memory book catalog by word-prefix matching against each book's `searchPath`. Phase 2 (debounced, async) kicks in when Phase 1 finds nothing and delegates entirely to `booksFsTocHeuristics.ts`. Both phases normalize the query through `normalizeBookQuery` from `bookQueryNormalizer.ts` - add new normalization rules there, not here. The `filterBooksByWords` function is exported so the heuristics file can reuse it without circular imports.

**booksFsTocHeuristics.ts** - the four-stage TOC fallback pipeline. Each stage is a named exported function so the logic is easy to follow, test, and extend independently:
- Stage 1 `splitQueryIntoBookAndTocParts` - finds the longest book-matching prefix of the query words; the remainder becomes the TOC search portion.
- Stage 2 `fetchTocRowsForBooks` - loads TOC entries for candidate books from the DB in sqrt-sized batches, strips redundant root entries, and yields between batches to keep the UI responsive. Returns null if cancelled.
- Stage 3 `searchTocRows` - builds a `SearchableTree` and scores all TOC entries against the TOC words.
- Stage 4 `buildTocResultItems` - converts matched nodes into `TocFsItem` objects for the UI.
The top-level `runTocHeuristics` function runs all four stages in sequence and accepts an `isCancelled` callback so stale searches abort cleanly. Add new heuristic stages here - do not add TOC search logic to `useBooksFsSearch.ts`.

**booksCategoryTree.ts** - builds the category tree from flat DB rows, assigns `searchPath` strings to books, and computes period and root-category metadata. `searchPath` is what `useBooksFsSearch` matches against - if you change what's searchable on a book, change it here. Book titles are run through `normalizeBookQuery` when building `searchPath`, so indexed tokens already reflect all normalization rules. Negative IDs on categories or books indicate user-defined custom entries; `buildTree` always sorts them after DB entries at every level of the tree, so they appear last in all views and search results.
