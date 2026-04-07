# search-db

Full-text search using Bloom filters. Category and book filters, virtual-scrolled results, indexing progress overlay.

**SearchPage.vue** — orchestrates search bar, filter panel, results list, and indexing overlay.

**SearchBar.vue** — search input with filter toggle.

**SearchResultsList.vue** — virtual-scrolled results list.

**SearchFilterPanel.vue** — collapsible category and book filter tree.

**SearchFilterNode.vue** — single node in the filter tree.

**SearchIndexingOverlay.vue** — full-screen overlay shown while the Bloom index is being built.

**useBloomSearch.ts** — executes Bloom filter searches via the C# backend and caches results in `searchCacheStore`. All search execution goes through here.

**useIndexingStatus.ts** — polls the C# backend for indexing progress. Use this to drive the overlay and any other indexing-dependent UI.

**useSearchFilters.ts** — filter tree state, result filtering, and result click navigation. Modify filter behavior here, not in the components.

**searchTypes.ts** — TypeScript types for the search feature. Add new search-related types here.
