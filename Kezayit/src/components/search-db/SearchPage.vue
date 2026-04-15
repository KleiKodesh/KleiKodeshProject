<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useBloomSearch } from './useBloomSearch'
import { useSearch } from './useSearchFilters'
import { useIndexingStatus } from './useIndexingStatus'
import { useTabStore } from '@/stores/tabStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import SearchBar from './SearchBar.vue'
import SearchResultsList from './SearchResultsList.vue'
import SearchFilterPanel from './SearchFilterPanel.vue'
import SearchIndexingOverlay from './SearchIndexingOverlay.vue'

const tabStore = useTabStore()
const booksStore = useBooksDataStore()

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
  filterBookQuery,
  checkedBookIds,
  filteredResults,
  resultCounts,
  initCheckedBooks,
  setCheckedBookIds,
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
const initialScrollIndex = ref<number | undefined>()
const initialScrollOffset = ref<number | undefined>()

useDropdownClose(
  filterPanelRef,
  () => {
    if (isFilterOpen.value) isFilterOpen.value = false
  },
  { toggleButton: computed(() => searchBarRef.value?.filterBtnRef ?? null) },
)

function onSearch(q: string) {
  tabStore.updateActiveTab({ searchQuery: q })
  handleSearch(q)
}
function onClearSearch() {
  tabStore.updateActiveTab({ searchQuery: undefined })
  handleClearSearch()
}

async function saveFilterState() {
  const allCount = booksStore.allBooks.length
  const isAllChecked = checkedBookIds.value.size === allCount
  const existing = await tabStore.getTabViewState(tabStore.activeTabId) ?? {}
  tabStore.setTabViewState(tabStore.activeTabId, {
    ...existing,
    searchFilterQuery: filterBookQuery.value || undefined,
    searchCheckedBookIds: isAllChecked ? undefined : [...checkedBookIds.value],
  })
}

async function restoreFromTab() {
  const savedQuery = tabStore.activeTab.searchQuery
  if (!savedQuery) return
  searchQuery.value = savedQuery
  const fromCache = await loadCachedResults(savedQuery)
  if (!fromCache) handleSearch(savedQuery)
}

onMounted(async () => {
  await booksStore.ensureLoaded()

  const saved = await tabStore.getTabViewState(tabStore.activeTabId)

  // Restore filter state before initCheckedBooks so we don't overwrite it
  if (saved?.searchCheckedBookIds != null) {
    setCheckedBookIds(new Set(saved.searchCheckedBookIds))
  } else {
    initCheckedBooks()
  }
  if (saved?.searchFilterQuery) {
    filterBookQuery.value = saved.searchFilterQuery
  }

  if (saved?.searchScrollIndex != null) {
    initialScrollIndex.value = saved.searchScrollIndex
    initialScrollOffset.value = saved.searchScrollOffset ?? 0
  }

  await restoreFromTab()
  searchBarRef.value?.focus()
})

useEventListener(document, 'visibilitychange', () => {
  if (document.visibilityState === 'hidden') saveFilterState()
})
onBeforeUnmount(saveFilterState)
</script>

<template>
  <div class="search-page">
    <SearchResultsList
      :results="filteredResults"
      :total-results="results.length"
      :search-query="executedQuery"
      :is-searching="isSearching"
      :has-searched="hasSearched"
      :initial-scroll-index="initialScrollIndex"
      :initial-scroll-offset="initialScrollOffset"
      @result-click="handleResultClick"
    />

    <SearchBar
      ref="searchBarRef"
      v-model:search-query="searchQuery"
      :is-searching="isSearching"
      :filter-count="checkedBookIds.size"
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
      v-model:filter-book-query="filterBookQuery"
      @toggle-book="toggleBook"
      @toggle-category="toggleCategory"
      @check-all="checkAll"
      @uncheck-all="uncheckAll"
      @close="isFilterOpen = false"
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
