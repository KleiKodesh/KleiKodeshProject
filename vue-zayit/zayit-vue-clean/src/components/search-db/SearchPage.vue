<script setup lang="ts">
import { ref, watch, onMounted, nextTick } from 'vue'
import { onClickOutside } from '@vueuse/core'
import { useBloomSearch } from './useBloomSearch'
import { useSearch } from './useSearch'
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
  checkedBookIds,
  filteredResults,
  resultCounts,
  initCheckedBooks,
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

onClickOutside(filterPanelRef, () => {
  if (isFilterOpen.value) isFilterOpen.value = false
})

function onSearch(q: string) {
  tabStore.updateActiveTab({ searchQuery: q })
  handleSearch(q)
}
function onClearSearch() {
  tabStore.updateActiveTab({ searchQuery: undefined, searchScrollIndex: undefined })
  handleClearSearch()
}

async function restoreFromTab() {
  const savedQuery = tabStore.activeTab.searchQuery
  const savedIndex = tabStore.activeTab.searchScrollIndex
  if (!savedQuery || !savedIndex) return

  searchQuery.value = savedQuery
  const fromCache = await loadCachedResults(savedQuery)

  if (!fromCache) {
    handleSearch(savedQuery)
    // wait for streaming search to finish
    await new Promise<void>((resolve) => {
      const stop = watch(isSearching, (val) => {
        if (!val) {
          stop()
          resolve()
        }
      })
    })
  }

  // wait for results to render into the DOM
  await nextTick()
  resultsListRef.value?.scrollToIndex(savedIndex)
}

onMounted(async () => {
  await booksStore.ensureLoaded()
  initCheckedBooks()
  await restoreFromTab()
  nextTick(() => searchBarRef.value?.focus())
})
</script>

<template>
  <div class="search-page">
    <SearchResultsList
      ref="resultsListRef"
      :results="filteredResults"
      :search-query="executedQuery"
      :is-searching="isSearching"
      :has-searched="hasSearched"
      @result-click="handleResultClick"
      @scrolled="tabStore.updateActiveTab({ searchScrollIndex: $event })"
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
