# books-fs

File system browser for the book catalog. List, tiles, and full collapsible tree views with two-tier search.

**BooksFsPage.vue** — main page, orchestrates the title bar, breadcrumb, and active view.

**BooksFsTitleBar.vue** — breadcrumb and view-mode toggle (list / tiles / full tree).

**BooksTreeView.vue** — list and tiles view at the current breadcrumb level.

**BooksFullTree.vue** — collapsible category tree showing the full catalog hierarchy.

**BooksSearchResults.vue** — renders book matches and TOC entry matches.

**BooksBreadcrumb.vue** — breadcrumb trail for the current folder path.

**useBooksFs.ts** — navigation state and folder traversal. Use this for any logic that moves through the folder hierarchy.

**useBooksFsSearch.ts** — two-tier search. Phase 1 matches books using `scoreMatch` from `fuzzyMatch.ts`, sorted by score then tree order. Phase 2 kicks in when no books match: splits the query into a book part and a TOC part, fetches TOC entries for candidate books, and searches them with `matchWords` from `tocSearchUtils.ts`. Do not add fuzzy logic to the TOC phase — it uses exact matching by design.

**booksCategoryTree.ts** — builds the category tree from flat DB rows, assigns `searchPath` strings to books, and computes period and root-category metadata. `searchPath` is what `useBooksFsSearch` matches against — if you change what's searchable on a book, change it here. Negative IDs on categories or books indicate user-defined custom entries; `buildTree` always sorts them after DB entries at every level of the tree, so they appear last in all views and search results.
