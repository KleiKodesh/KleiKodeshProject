<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useZoomHandler, ZOOM_CONFIG } from '@/composables/useZoom'
import { useBloomSearch } from './useBloomSearch'
import { useSearch, parseSearchQuery } from './useSearchFilters'
import { useIndexingStatus } from './useIndexingStatus'
import { useTabStore } from '@/stores/tabStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import SearchBar from './SearchBar.vue'
import SearchResultsList from './SearchResultsList.vue'
import SearchFilterPanel from './SearchFilterPanel.vue'
import SearchIndexingOverlay from './SearchIndexingOverlay.vue'

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
} = useBloomSearch()
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
} = useSearch(
  () => results.value,
  () => executedQuery.value,
  executeSearch,
  clearSearch,
)
const { state: indexingState } = useIndexingStatus()

const searchBarRef = ref<InstanceType<typeof SearchBar> | null>(null)
const filterPanelRef = ref<HTMLElement | null>(null)
const resultsListRef = ref<InstanceType<typeof SearchResultsList> | null>(null)
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

  if (saved?.searchScrollIndex != null) {
    initialScrollIndex.value = saved.searchScrollIndex
    initialScrollOffset.value = saved.searchScrollOffset ?? 0
    lastScrollIndex = saved.searchScrollIndex
    lastScrollOffset = saved.searchScrollOffset ?? 0
  }

  if (saved?.searchZoom != null) {
    zoom.value = saved.searchZoom
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
    <SearchResultsList
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

    <SearchBar
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

    <SearchFilterPanel
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

    <SearchIndexingOverlay v-if="indexingState.isIndexing" :state="indexingState" />
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
