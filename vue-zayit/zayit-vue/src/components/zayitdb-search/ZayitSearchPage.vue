<template>
  <div class="flex-column height-fill search-page position-relative">
    <!-- Results area -->
    <SearchResultsList ref="resultsListRef"
                       :results="filteredResults"
                       :search-query="executedQuery"
                       :is-searching="isSearching"
                       :has-searched="hasSearched"
                       @result-click="handleResultClick" />

    <!-- Search bar at bottom -->
    <SearchBar ref="searchBarRef"
               v-model:search-query="searchQuery"
               :is-searching="isSearching"
               :filter-count="checkedBookIds.size"
               @search="handleSearch"
               @cancel="cancelSearch"
               @toggle-filter="toggleFilter"
               @clear="handleClearSearch" />

    <!-- Filter panel -->
    <FsCheckedTree v-if="isFilterOpen"
                   ref="filterPanelRef"
                   :checked-book-ids="checkedBookIds"
                   :result-counts="resultCounts"
                   :has-searched="hasSearched"
                   @toggle-book="toggleBook"
                   @toggle-category="toggleCategory"
                   @check-all="checkAllBooks"
                   @uncheck-all="uncheckAllBooks"
                   @close="isFilterOpen = false" />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick, computed } from 'vue'
import { onClickOutside } from '@vueuse/core'
import { useTabStore } from '@/data/stores/tabStore'
import { useBloomSearch } from '@/components/zayitdb-search/useBloomSearch'
import { useZayitSearchPage } from '@/components/zayitdb-search/useZayitSearchPage'
import FsCheckedTree from '@/components/zayitdb-search/FsCheckedTree.vue'
import SearchBar from '@/components/zayitdb-search/SearchBar.vue'
import SearchResultsList from '@/components/zayitdb-search/SearchResultsList.vue'

// Store
const tabStore = useTabStore()

// Bloom search composable
const {
  results,
  isSearching,
  hasSearched,
  executedQuery,
  executeSearch: executeSearchComposable,
  cancelSearch,
  clearSearch: clearSearchComposable,
  loadCachedResults
} = useBloomSearch()

// Page logic composable
const {
  searchQuery,
  isFilterOpen,
  checkedBookIds,
  currentSearchState,
  filteredResults,
  resultCounts,
  initializeCheckedBooks,
  toggleFilter,
  toggleBook,
  toggleCategory,
  checkAllBooks,
  uncheckAllBooks,
  handleSearch,
  handleClearSearch,
  handleResultClick,
  setupStateWatchers
} = useZayitSearchPage(
  () => results.value,
  () => executedQuery.value,
  executeSearchComposable,
  clearSearchComposable
)

const searchBarRef = ref<InstanceType<typeof SearchBar> | null>(null)
const resultsListRef = ref<InstanceType<typeof SearchResultsList> | null>(null)
const filterPanelRef = ref<InstanceType<typeof FsCheckedTree>>()

// Computed ref to get the actual DOM element from the component
const filterPanelElement = computed(() => {
  return filterPanelRef.value?.$el as HTMLElement | undefined
})

// Close filter panel when clicking outside
onClickOutside(filterPanelElement, () => {
  if (isFilterOpen.value) {
    isFilterOpen.value = false
  }
})

// Setup state watchers
setupStateWatchers(() => hasSearched.value)

// Restore state on mount
onMounted(async () => {
  initializeCheckedBooks()

  if (currentSearchState.value) {
    searchQuery.value = currentSearchState.value.searchQuery
    hasSearched.value = currentSearchState.value.hasSearched

    // Restore tab title if there's a search query
    if (currentSearchState.value.searchQuery.trim()) {
      const currentTab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'kezayit-search')
      if (currentTab) {
        currentTab.title = `חיפוש: ${currentSearchState.value.searchQuery}`
      }
    }

    // Load results from cache
    if (hasSearched.value && searchQuery.value.trim()) {
      const loaded = await loadCachedResults(searchQuery.value)

      if (!loaded) {
        await handleSearch(searchQuery.value)
      }
    }
  }

  nextTick(() => {
    searchBarRef.value?.focus()
  })
})
</script>

<style scoped>
.search-page {
  position: relative;
}
</style>
