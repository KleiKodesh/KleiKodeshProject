# full-text-search

Full-text search using category and book filters, virtual-scrolled results, indexing progress bar. Backed by FtsLib (custom inverted index engine) on the C# side.

## Components

**FullTextSearchPage.vue** — orchestrates search bar, filter panel, results list, advanced panel, and indexing overlay. Manages per-tab zoom via `useZoomHandler` — zoom is stored in `TabState.searchZoom` (IDB, per-tab) and passed as a prop to `FullTextSearchResultsList`.

**FullTextSearchBar.vue** — search input with filter toggle, advanced toggle, cancel button, and animated placeholder. Shows a live result count badge while streaming.

**FullTextSearchResultsList.vue** — virtual-scrolled results list. Accepts `results` (filtered), `zoom` (optional 50–200), and `initialScrollIndex`/`initialScrollOffset` for session restore. Font size scales with zoom and the global font size setting.

**FullTextSearchFilterPanel.vue** — slide-in filter panel anchored to the right edge. Contains the full category/book tree and a token-based search input at the bottom. Typing a book name filters to a flat matching list (`FullTextSearchFilterBookList`). Typing `@` or pressing Enter commits the current text as a filter token; multiple tokens are unioned.

**FullTextSearchFilterNode.vue** — single category node in the filter tree with expand/collapse and checkbox.

**FullTextSearchFilterBookList.vue** — flat list of books shown inside the filter panel when the user has committed tokens or is typing a book-name query.

**FullTextSearchAdvancedPanel.vue** — two-tab panel (options + syntax reference) for advanced search settings. All settings are stored in `settingsStore` and persisted to localStorage. Changing any setting triggers a re-search automatically.

**FullTextSearchIndexingOverlay.vue** — banner overlay shown while the FTS index is being built. Displays a progress bar with segment flush markers. Stays visible for 1.5s after indexing completes so the user sees the final state.

## Composables

**useFullTextSearch.ts** — executes full-text searches via the C# streaming backend. `FtsSearchStart` returns a `searchId`; results arrive as `searchBatch` events via `chrome.webview`. Each batch is written to `searchCacheStore` incrementally. On re-search or tab restore, cached results are shown immediately and the C# stream resumes from the cached offset by passing `excludedLineIds` (already-cached line IDs) so C# skips snippet generation for those lines.

**useFullTextSearchIndexingStatus.ts** — polls `GetFtsIndexingProgress` on mount, then subscribes to `ftsIndexProgress` and `ftsIndexInvalidated` push events. Drives the indexing overlay and the `isIndexing` flag passed to `useFullTextSearch`.

**useFullTextSearchFilters.ts** — owns all filter state: `checkedBookIds`, `atFilters` (committed `@` tokens), `filteredResults`, `resultCounts`. The effective filter is the intersection of checked books and the union of books matching all `@` tokens. Re-applies the filter on every streaming batch without re-querying C#.

## Cache

`searchCacheStore` (in `src/stores/`) persists search results in the `app-search-cache` IDB database. Each entry stores `{ results, complete, indexingComplete }`. Batches are appended incrementally so partial results survive tab switches and app restarts. LRU-capped at 100 queries. Entries written during indexing (`indexingComplete: false`) are refreshed in the background the next time the same query is run after the index finishes building.

## Query Syntax

See the syntax tab in `FullTextSearchAdvancedPanel.vue`. The canonical reference is the `SeforimIndex` doc comment in `CSharpBackend/SearchEngine/Ftslib/SeforimDb/SeforimIndex.cs`.

## Bridge Actions

| Action | Direction | Description |
| --- | --- | --- |
| `FtsSearchStart` | Vue → C# | Start a search; params: query, excludedLineIds[], maxWordDist, requireOrdered, contextWords, expandKetiv. Returns `{ searchId, failReason }` |
| `FtsSearchCancel` | Vue → C# | Cancel an in-flight search by searchId |
| `GetFtsIndexingProgress` | Vue → C# | Poll current indexing state on mount |
| `ResetFtsIndex` | Vue → C# | Delete index and rebuild from scratch |
| `searchBatch` | C# → Vue | Batch of results (type, searchId, results[]) |
| `searchComplete` | C# → Vue | Stream finished |
| `searchCancelled` | C# → Vue | Stream cancelled |
| `searchError` | C# → Vue | Stream error |
| `ftsIndexProgress` | C# → Vue | Indexing progress tick |
| `ftsIndexInvalidated` | C# → Vue | Index corrupt or DB changed; rebuild started automatically |
