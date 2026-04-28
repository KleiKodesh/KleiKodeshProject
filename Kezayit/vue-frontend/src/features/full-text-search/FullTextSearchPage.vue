<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useZoomHandler, ZOOM_CONFIG } from '@/composables/useZoom'
import { useFullTextSearch } from './useFullTextSearch'
import { useFullTextSearchFilters, parseSearchQuery } from './useFullTextSearchFilters'
import { useFullTextSearchIndexingStatus } from './useFullTextSearchIndexingStatus'
import { useTabStore } from '@/stores/tabStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import FullTextSearchBar from './FullTextSearchBar.vue'
import FullTextSearchResultsList from './FullTextSearchResultsList.vue'
import FullTextSearchFilterPanel from './FullTextSearchFilterPanel.vue'
import FullTextSearchIndexingOverlay from './FullTextSearchIndexingOverlay.vue'

const tabStore = useTabStore()
const booksStore = useBooksDataStore()

// Capture tabId at mount time — stable for this component's lifetime (/search is keyed by tabId)
const tabId = tabStore.activeTabId

const zoom = ref<number>(ZOOM_CONFIG.DEFAULT)
const isSearchActive = computed(() => tabStore.activeTab?.route === '/search')
useZoomHandler({ zoom, enabled: isSearchActive })

const {
  results,
  isSearching,
  hasSearched,
  executedQuery,
  executeSearch,
  cancelSearch,
  clearSearch,
  loadCachedResults,
} = useFullTextSearch()
const {
  searchQuery,
  isFilterOpen,
  checkedBookIds,
  atFilters,
  filteredResults,
  resultCounts,
  initCheckedBooks,
  setCheckedBookIds,
  setAtFilters,
  toggleBook,
  toggleCategory,
  checkAll,
  uncheckAll,
  handleSearch,
  handleClearSearch,
  handleResultClick,
} = useFullTextSearchFilters(
  () => results.value,
  () => executedQuery.value,
  executeSearch,
  clearSearch,
)
const { state: indexingState } = useFullTextSearchIndexingStatus()

const searchBarRef = ref<InstanceType<typeof FullTextSearchBar> | null>(null)
const filterPanelRef = ref<HTMLElement | null>(null)
const resultsListRef = ref<InstanceType<typeof FullTextSearchResultsList> | null>(null)
const initialScrollIndex = ref<number | undefined>()
const initialScrollOffset = ref<number | undefined>()
// Scroll position owned here — updated by SearchResultsList via saveScroll emit
let lastScrollIndex: number | undefined
let lastScrollOffset: number | undefined

useDropdownClose(
  filterPanelRef,
  () => {
    if (isFilterOpen.value) isFilterOpen.value = false
  },
  { toggleButton: computed(() => searchBarRef.value?.filterBtnRef ?? null) },
)

function onSearch(q: string) {
  const { term, atFilters: tokens } = parseSearchQuery(q)
  setAtFilters(tokens)
  // Store the full raw query in the tab (for display/restore) but search only the term part
  tabStore.updateActiveTab({ searchQuery: q, title: `חיפוש: ${term || q}` })
  if (term) handleSearch(term)
}
function onClearSearch() {
  tabStore.updateActiveTab({ searchQuery: undefined, title: 'חיפוש' })
  handleClearSearch()
}

function onSaveScroll(pos: { scrollIndex: number; scrollOffset: number }) {
  lastScrollIndex = pos.scrollIndex
  lastScrollOffset = pos.scrollOffset
}

async function saveFilterState() {
  // Capture scroll position directly from the list — don't rely on the saveScroll emit
  // having already fired. onBeforeUnmount order is parent-first, child-second, so the
  // emit-based lastScrollIndex would still be undefined when the parent unmounts.
  const captured = resultsListRef.value?.captureScrollPos()
  if (captured) {
    lastScrollIndex = captured.scrollIndex
    lastScrollOffset = captured.scrollOffset
  }
  const allCount = booksStore.allBooks.length
  const isAllChecked = allCount > 0 && checkedBookIds.value.size === allCount
  const state = {
    searchCheckedBookIds: isAllChecked ? undefined : [...checkedBookIds.value],
    searchAtFilters: atFilters.value.length ? atFilters.value : undefined,
    searchScrollIndex: lastScrollIndex,
    searchScrollOffset: lastScrollOffset,
    searchZoom: zoom.value !== ZOOM_CONFIG.DEFAULT ? zoom.value : undefined,
  }
  tabStore.setTabViewState(tabId, state)
}

async function restoreFromTab() {
  const savedQuery = tabStore.activeTab.searchQuery
  if (!savedQuery) return
  searchQuery.value = savedQuery
  const { term, atFilters: tokens } = parseSearchQuery(savedQuery)
  setAtFilters(tokens)
  tabStore.updateActiveTab({ title: `חיפוש: ${term || savedQuery}` })
  const fromCache = await loadCachedResults(term || savedQuery)
  if (!fromCache) handleSearch(term || savedQuery)
}

onMounted(async () => {
  await booksStore.ensureLoaded()

  const saved = await tabStore.getTabViewState(tabId)

  if (saved?.searchCheckedBookIds != null) {
    const validIds = new Set(booksStore.allBooks.map((b) => b.id))
    const restored = new Set(saved.searchCheckedBookIds.filter((id) => validIds.has(id)))
    setCheckedBookIds(restored)
  } else {
    initCheckedBooks()
  }

  if (saved?.searchAtFilters?.length) {
    setAtFilters(saved.searchAtFilters)
  }

  // Restore zoom BEFORE restoring scroll — zoom affects item height estimates in the
  // virtualizer, so if zoom is applied after results populate the scroll lands in the wrong place.
  if (saved?.searchZoom != null) {
    zoom.value = saved.searchZoom
  }

  if (saved?.searchScrollIndex != null) {
    initialScrollIndex.value = saved.searchScrollIndex
    initialScrollOffset.value = saved.searchScrollOffset ?? 0
    lastScrollIndex = saved.searchScrollIndex
    lastScrollOffset = saved.searchScrollOffset ?? 0
  }

  await restoreFromTab()
  searchBarRef.value?.focus()
})

// Save filter state whenever the page goes hidden or is unmounted.
// /search is keyed by tabId so unmount = this tab's search instance is gone (tab switched or closed).
// Tab close triggers closeTab() which deletes the IDB key anyway, but saving first is harmless.
useEventListener(document, 'visibilitychange', () => {
  if (document.visibilityState === 'hidden') saveFilterState()
})
onBeforeUnmount(saveFilterState)
</script>

<template>
  <div class="search-page">
    <FullTextSearchResultsList
      ref="resultsListRef"
      :results="filteredResults"
      :total-results="results.length"
      :search-query="executedQuery"
      :is-searching="isSearching"
      :has-searched="hasSearched"
      :initial-scroll-index="initialScrollIndex"
      :initial-scroll-offset="initialScrollOffset"
      :zoom="zoom"
      @result-click="handleResultClick"
      @save-scroll="onSaveScroll"
    />

    <FullTextSearchBar
      ref="searchBarRef"
      v-model:search-query="searchQuery"
      :is-searching="isSearching"
      :filter-count="checkedBookIds.size"
      :at-filter-count="atFilters.length"
      :disabled="indexingState.isIndexing"
      @search="onSearch"
      @cancel="cancelSearch"
      @toggle-filter="isFilterOpen = !isFilterOpen"
      @clear="onClearSearch"
    />

    <FullTextSearchFilterPanel
      v-if="isFilterOpen"
      ref="filterPanelRef"
      :checked-book-ids="checkedBookIds"
      :result-counts="resultCounts"
      :has-searched="hasSearched"
      :at-filters="atFilters"
      @toggle-book="toggleBook"
      @toggle-category="toggleCategory"
      @check-all="checkAll"
      @uncheck-all="uncheckAll"
      @close="isFilterOpen = false"
      @update:at-filters="setAtFilters"
    />

    <FullTextSearchIndexingOverlay v-if="indexingState.isIndexing" :state="indexingState" />
  </div>
</template>

<style scoped>
.search-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  position: relative;
  background: var(--bg-primary);
}
</style>
