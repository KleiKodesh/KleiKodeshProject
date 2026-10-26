# book-catalog

Book catalog browser: list and tiles views, with Lucene-backed search.

**BookCatalogPage.vue** - main page, orchestrating the address bar and the active view. It owns which face the bar shows (path or search field) — see the `showSearch` note in the file — and forwards the search input's keys to whichever view is on screen. Which view that is comes from `settingsStore.booksView`, an app-wide preference shared by every `/books` tab, not per-tab state; assigning it persists it, so there is nothing to load on mount.

**BookCatalogView.List.vue** - the catalog list, with folder and book rows.

**BookCatalogView.Tiles.vue** - the same items as a wrapping tile grid, sharing the home page's tile look. Its keyboard steps a whole row at a time and takes left/right within a row (`getColumnsPerRow` / `countGridColumns` in `useInputListNavigation`).

**BookCatalogTitleBar.vue** - the address bar: one pill (`TopSearchBar`) holding home, the breadcrumb or the search field, and — beside the search icon — the list/tiles toggle.

**BookCatalogSearch.vue** - renders book matches and TOC entry matches, in a list or as tiles to match the active view.

**BookCatalogBreadcrumb.vue** - breadcrumb trail for the current folder path.

**useBookCatalog.ts** - navigation state and folder traversal. Use this for any logic that moves through the folder hierarchy.

**useBookCatalogSearch.ts** - catalog search. One debounced `catalogTocSearch` call (via `serviceCall`) to the Lucene catalog index in the KitveiHakodesh service (Core `SeforimDbCatalogIndex`) answers the whole query: book-title docs and full-TOC-path docs together, ranked by the index (literal matches, then TOC level, then catalog tree order), never capped. Same path in dev and hosted. All normalization (abbreviations, spelling variants, ה-prefix) lives in Core `SeforimDbCatalogTextNormalizer` - add rules there, not in the frontend.

**bookTitleSearch.ts** - `searchBookTitleIds`: book ids matching a query by title, path or author, best first, via `catalogTitleSearch` on the same index. Waits while the index builds; resolves null when stale or when the index cannot answer (the shared index status says why). Used by the full-text-search @ filter, its filter panel, daf-yomi navigation and the home daily-learning resolver.

**bookCatalogTree.ts** - builds the category tree from flat DB rows, assigns full category paths to books, and computes period and root-category metadata. Negative IDs on categories or books indicate user-defined custom entries; `buildTree` always sorts them after DB entries at every level of the tree.
