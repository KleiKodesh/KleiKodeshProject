# search-db

Full-text search using Bloom filters. Category and book filters, virtual-scrolled results, indexing progress overlay.

## Components

**SearchPage.vue** — orchestrates search bar, filter panel, results list, and indexing overlay.

**SearchBar.vue** — search input with filter toggle button.

**SearchResultsList.vue** — virtual-scrolled results list. Accepts `results` (filtered) and `totalResults` (unfiltered count) to show a count bar above the list. Shows live count during streaming.

**SearchFilterPanel.vue** — slide-in filter panel. Contains the category/book tree and a book-name search input at the bottom. The book-name query is owned by `useSearchFilters` (via `v-model:filter-book-query`) so it persists when the panel closes. When a query is typed the tree is replaced by a flat matching-book list (`SearchFilterBookList`).

**SearchFilterNode.vue** — single node in the filter tree (category or book row with checkbox).

**SearchFilterBookList.vue** — flat list of books shown inside the filter panel when the user types a book-name query. Same row styling as the tree nodes.

**SearchIndexingOverlay.vue** — full-screen overlay shown while the Bloom index is being built.

## Composables

**useBloomSearch.ts** — executes Bloom filter searches via the C# backend. Supports incremental caching: each batch is written to `searchCacheStore` as it arrives. On re-search or tab restore, cached partial results are shown immediately and the C# stream resumes from the last cached offset (`skipCount`). All search execution goes through here.

**useIndexingStatus.ts** — polls the C# backend for indexing progress. Use this to drive the overlay and any other indexing-dependent UI.

**useSearchFilters.ts** — owns all filter state: `checkedBookIds`, `filterBookQuery`, `isFilterOpen`. The effective filter is the intersection of checked books and books whose title matches `filterBookQuery` — both must pass for a result to be shown. Modify filter behavior here, not in the components.

**searchTypes.ts** — TypeScript types for the search feature.

## Cache

`searchCacheStore` (in `src/stores/`) persists search results in the `app-search-cache` IDB database. Each entry stores `{ results, complete }`. Batches are appended incrementally so partial results survive tab switches and app restarts. LRU-capped at 100 queries. The C# `BloomSearchStart` action accepts an optional `skipCount` parameter to resume streaming from a given offset.
