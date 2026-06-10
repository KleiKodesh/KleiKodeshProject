# full-text-search

Full-text search using category and book filters, virtual-scrolled results, indexing progress bar. Backed by FtsLib (custom inverted index engine) on the C# side.

## Architecture — Two-Phase Search

Search is split into two phases to give instant results without waiting for snippet generation:

Phase 1 — `FtsSearchStart`: C# runs an index-only pass (`SearchIds`) and returns all matching `{ lineId, bookId, bookTitle }` in a single `idsComplete` push event. No snippet work at all — very fast. The frontend receives the full result list and renders placeholder rows immediately.

Phase 2 — `FtsGetSnippets`: Vue calls this synchronous RPC with the lineIds currently visible in the virtualizer viewport. C# generates snippets for exactly those lines (reads line content from DB, runs highlight logic). Called automatically as the user scrolls. Results are cached in-memory in `FullTextSearchResultsList` for the lifetime of the search.

No persistence — results are fast enough to re-run on demand. Recent query history is stored in a module-level array (session-only, cleared on reload).

## Components

**FullTextSearchPage.vue** — orchestrates search bar, filter panel, results list, advanced panel, and indexing overlay. Manages per-tab zoom via `useZoomHandler`. Only filter state (checked books, @ tokens, zoom) is persisted to IDB via `TabState`.

**FullTextSearchBar.vue** — search input with filter toggle, advanced toggle, cancel button, and animated placeholder. Shows a live result count badge. Maintains a module-level session history array (no IDB).

**FullTextSearchResultsList.vue** — virtual-scrolled results list. Renders placeholder shimmer rows until snippets arrive. Calls `fetchSnippetsForWindow` on scroll to fetch snippets for the visible range + overscan. Caches fetched snippets in a `Map<lineId, data>` for the current search session.

**FullTextSearchFilterPanel.vue** — slide-in filter panel anchored to the right edge. Contains the full category/book tree and a token-based search input. Typing commits tokens as `@` filters; multiple tokens are unioned against the book catalog.

**FullTextSearchFilterNode.vue** — single category node in the filter tree with expand/collapse and checkbox.

**FullTextSearchFilterBookList.vue** — flat list of books shown when the user has committed tokens or is typing a book-name query.

**FullTextSearchAdvancedPanel.vue** — two-tab panel (options + syntax reference). All settings are stored in `settingsStore` and persisted to localStorage. Changing any result-affecting setting triggers a re-search automatically.

**FullTextSearchIndexingOverlay.vue** — banner overlay shown while the FTS index is being built. Stays visible 1.5s after indexing completes.

## Composables

**useFullTextSearch.ts** — owns the two-phase search flow. `executeSearch` fires `FtsSearchStart` and registers a `chrome.webview` listener for the `idsComplete` event. `fetchSnippetsForWindow` calls `FtsGetSnippets` and `GET_TOC_PATHS_FOR_LINES` in parallel for a list of lineIds.

**useFullTextSearchIndexingStatus.ts** — polls `GetFtsIndexingProgress` on mount, then subscribes to `ftsIndexProgress` and `ftsIndexInvalidated` push events.

**useFullTextSearchFilters.ts** — owns all filter state: `checkedBookIds`, `atFilters`, `filteredResults`, `resultCounts`. Re-applies the filter on every results change without re-querying C#.

## Bridge Actions

| Action | Direction | Description |
| --- | --- | --- |
| `FtsSearchStart` | Vue → C# | Start ID-only search. Params: query, expandKetiv. Returns `{ searchId, failReason }` |
| `FtsSearchCancel` | Vue → C# | Cancel an in-flight ID search by searchId |
| `FtsGetSnippets` | Vue → C# | Generate snippets for a lineId list. Params: lineIds[], query, maxWordDist, requireOrdered, contextWords, expandKetiv. Returns `{ snippets[] }` synchronously |
| `GetFtsIndexingProgress` | Vue → C# | Poll current indexing state on mount |
| `ResetFtsIndex` | Vue → C# | Delete index and rebuild from scratch |
| `idsComplete` | C# → Vue | Full `{ lineId, bookId, bookTitle }[]` result set |
| `idsCancelled` | C# → Vue | ID search was cancelled |
| `idsError` | C# → Vue | ID search failed |
| `ftsIndexProgress` | C# → Vue | Indexing progress tick |
| `ftsIndexInvalidated` | C# → Vue | Index corrupt or DB changed; rebuild started automatically |
