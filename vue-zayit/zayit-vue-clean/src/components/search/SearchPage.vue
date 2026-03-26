<script setup lang="ts">
import { ref, onMounted, nextTick } from 'vue'
import { onClickOutside } from '@vueuse/core'
import { useBloomSearch } from './useBloomSearch'
import { useSearch } from './useSearch'
import { useIndexingStatus } from './useIndexingStatus'
import SearchBar from './SearchBar.vue'
import SearchResultsList from './SearchResultsList.vue'
import SearchFilterPanel from './SearchFilterPanel.vue'
import SearchIndexingOverlay from './SearchIndexingOverlay.vue'
import { useBooksDataStore } from '@/stores/booksDataStore'

const booksStore = useBooksDataStore()

const {
  results, isSearching, hasSearched, executedQuery,
  executeSearch, cancelSearch, clearSearch, loadCachedResults,
} = useBloomSearch()

const {
  searchQuery, isFilterOpen, checkedBookIds, filteredResults, resultCounts,
  initCheckedBooks, toggleFilter, toggleBook, toggleCategory, checkAll, uncheckAll,
  handleSearch, handleClearSearch, handleResultClick, setupWatchers,
} = useSearch(
  () => results.value,
  () => executedQuery.value,
  executeSearch,
  clearSearch,
)

const { state: indexingState } = useIndexingStatus()

const searchBarRef   = ref<InstanceType<typeof SearchBar> | null>(null)
const filterPanelRef = ref<HTMLElement | null>(null)

onClickOutside(filterPanelRef, () => { if (isFilterOpen.value) isFilterOpen.value = false })

setupWatchers(() => hasSearched.value)

onMounted(async () => {
  await booksStore.ensureLoaded()
  initCheckedBooks()
  nextTick(() => searchBarRef.value?.focus())
})
</script>

<template>
  <div class="search-page">
    <SearchResultsList
      :results="filteredResults"
      :search-query="executedQuery"
      :is-searching="isSearching"
      :has-searched="hasSearched"
      @result-click="handleResultClick"
    />

    <SearchBar
      ref="searchBarRef"
      v-model:search-query="searchQuery"
      :is-searching="isSearching"
      :filter-count="checkedBookIds.size"
      :disabled="indexingState.isIndexing"
      @search="handleSearch"
      @cancel="cancelSearch"
      @toggle-filter="toggleFilter"
      @clear="handleClearSearch"
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

    <!-- Indexing overlay — shown while bloom index is being built -->
    <SearchIndexingOverlay
      v-if="indexingState.isIndexing"
      :state="indexingState"
    />
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
