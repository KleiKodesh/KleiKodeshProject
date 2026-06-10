<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useZoomHandler, ZOOM_CONFIG } from '@/composables/useZoom'
import { useFullTextSearch } from './useFullTextSearch'
import { useFullTextSearchFilters, parseSearchQuery } from './useFullTextSearchFilters'
import { useFullTextSearchIndexingStatus } from './useFullTextSearchIndexingStatus'
import { useTabStore } from '@/stores/tabStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useSettingsStore } from '@/stores/settingsStore'
import FullTextSearchBar from './FullTextSearchBar.vue'
import FullTextSearchResultsList from './FullTextSearchResultsList.vue'
import FullTextSearchFilterPanel from './FullTextSearchFilterPanel.vue'
import FullTextSearchAdvancedPanel from './FullTextSearchAdvancedPanel.vue'
import FullTextSearchIndexingOverlay from './FullTextSearchIndexingOverlay.vue'

const tabStore = useTabStore()
const booksStore = useBooksDataStore()
const settings = useSettingsStore()

// /search is keyed by tabId — stable for this component's lifetime
const tabId = tabStore.activeTabId

const zoom = ref<number>(ZOOM_CONFIG.DEFAULT)
const isSearchActive = computed(() => tabStore.activeTab?.route === '/search')
useZoomHandler({ zoom, enabled: isSearchActive })

// ── Indexing status ───────────────────────────────────────────────────────────

const { state: indexingState } = useFullTextSearchIndexingStatus()

const showIndexingOverlay = ref(false)
let overlayHideTimer: ReturnType<typeof setTimeout> | null = null
watch(
  indexingState,
  (s) => {
    if (s.isIndexing) {
      if (overlayHideTimer) { clearTimeout(overlayHideTimer); overlayHideTimer = null }
      showIndexingOverlay.value = true
    } else if (showIndexingOverlay.value) {
      overlayHideTimer = setTimeout(() => { showIndexingOverlay.value = false }, 1500)
    }
  },
  { deep: true },
)

// ── Search ────────────────────────────────────────────────────────────────────

const {
  results,
  isSearching,
  hasSearched,
  executedQuery,
  searchError,
  maxWordDistance,
  requireOrdered,
  expandKetiv,
  executeSearch,
  cancelSearch,
  clearSearch,
  fetchSnippetsForWindow,
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
  checkAllFiltered,
  uncheckAll,
  uncheckAllFiltered,
  handleSearch,
  handleClearSearch,
  handleResultClick,
} = useFullTextSearchFilters(
  () => results.value,
  () => executedQuery.value,
  executeSearch,
  clearSearch,
)

// ── UI state ──────────────────────────────────────────────────────────────────

const searchBarRef = ref<InstanceType<typeof FullTextSearchBar> | null>(null)
const filterPanelRef = ref<HTMLElement | null>(null)
const isAdvancedOpen = ref(false)

const isAdvancedActive = computed(
  () =>
    maxWordDistance.value !== 10 ||
    requireOrdered.value ||
    !expandKetiv.value ||
    settings.searchWildcardWrap ||
    settings.searchGrammarWrap ||
    settings.searchContextMarginWords !== 30,
)

useDropdownClose(filterPanelRef, () => { if (isFilterOpen.value) isFilterOpen.value = false }, {
  toggleButton: computed(() => searchBarRef.value?.filterBtnRef ?? null),
})

// Re-run search when any advanced setting that affects results changes.
// (contextMarginWords only affects snippets — those are re-fetched on next viewport scroll,
//  so no re-search needed for that one.)
watch(
  [maxWordDistance, requireOrdered, expandKetiv,
   () => settings.searchWildcardWrap, () => settings.searchGrammarWrap],
  () => {
    if (hasSearched.value && executedQuery.value) handleSearch(executedQuery.value)
  },
)

// ── Event handlers ────────────────────────────────────────────────────────────

function onSearch(q: string) {
  const { term, atFilters: tokens } = parseSearchQuery(q)
  setAtFilters(tokens)
  tabStore.updateActiveTab({ searchQuery: q, title: `חיפוש: ${term || q}` })
  if (term) handleSearch(term)
}

function onClearSearch() {
  tabStore.updateActiveTab({ searchQuery: undefined, title: 'חיפוש' })
  handleClearSearch()
}

// ── Filter state persistence (lightweight — just checked books + zoom) ────────

async function saveFilterState() {
  const allCount = booksStore.allBooks.length
  const isAllChecked = allCount > 0 && checkedBookIds.value.size === allCount
  tabStore.setTabViewState(tabId, {
    searchCheckedBookIds: isAllChecked ? undefined : [...checkedBookIds.value],
    searchAtFilters: atFilters.value.length ? [...atFilters.value] : undefined,
    searchZoom: zoom.value !== ZOOM_CONFIG.DEFAULT ? zoom.value : undefined,
  })
}

async function restoreFromTab() {
  const savedQuery = tabStore.activeTab.searchQuery
  if (!savedQuery) return
  searchQuery.value = savedQuery
  const { term, atFilters: tokens } = parseSearchQuery(savedQuery)
  setAtFilters(tokens)
  tabStore.updateActiveTab({ title: `חיפוש: ${term || savedQuery}` })
  await handleSearch(term || savedQuery)
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────

onMounted(async () => {
  await booksStore.ensureLoaded()

  const saved = await tabStore.getTabViewState(tabId)

  if (saved?.searchCheckedBookIds != null) {
    const validIds = new Set(booksStore.allBooks.map((b) => b.id))
    setCheckedBookIds(new Set(saved.searchCheckedBookIds.filter((id) => validIds.has(id))))
  } else {
    initCheckedBooks()
  }

  if (saved?.searchAtFilters?.length) setAtFilters(saved.searchAtFilters)
  if (saved?.searchZoom != null) zoom.value = saved.searchZoom

  await restoreFromTab()

  searchBarRef.value?.focusAndShowHistory()
})

useEventListener(document, 'visibilitychange', () => {
  if (document.visibilityState === 'hidden') saveFilterState()
})
onBeforeUnmount(() => {
  saveFilterState()
  if (overlayHideTimer) clearTimeout(overlayHideTimer)
})
</script>

<template>
  <div class="search-page">
    <FullTextSearchResultsList
      :results="filteredResults"
      :search-query="executedQuery"
      :is-searching="isSearching"
      :has-searched="hasSearched"
      :search-error="searchError"
      :db-not-found="indexingState.dbNotFound"
      :zoom="zoom"
      :fetch-snippets-for-window="fetchSnippetsForWindow"
      @result-click="handleResultClick"
    />

    <FullTextSearchIndexingOverlay v-if="showIndexingOverlay" :state="indexingState" />

    <FullTextSearchAdvancedPanel
      v-if="isAdvancedOpen"
      :max-word-distance="maxWordDistance"
      :require-ordered="requireOrdered"
      :context-words="settings.searchContextMarginWords"
      :expand-ketiv="expandKetiv"
      :wildcard-wrap="settings.searchWildcardWrap"
      :grammar-wrap="settings.searchGrammarWrap"
      @update:max-word-distance="maxWordDistance = $event"
      @update:require-ordered="requireOrdered = $event"
      @update:context-words="settings.searchContextMarginWords = $event"
      @update:expand-ketiv="expandKetiv = $event"
      @update:wildcard-wrap="settings.searchWildcardWrap = $event"
      @update:grammar-wrap="settings.searchGrammarWrap = $event"
      @close="isAdvancedOpen = false"
    />

    <FullTextSearchBar
      ref="searchBarRef"
      v-model:search-query="searchQuery"
      :is-searching="isSearching"
      :filter-count="checkedBookIds.size"
      :at-filter-count="atFilters.length"
      :is-advanced-open="isAdvancedOpen"
      :is-advanced-active="isAdvancedActive"
      :result-count="filteredResults.length"
      :total-result-count="results.length"
      :has-searched="hasSearched"
      @search="onSearch"
      @cancel="cancelSearch"
      @toggle-filter="isFilterOpen = !isFilterOpen"
      @toggle-advanced="isAdvancedOpen = !isAdvancedOpen"
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
      @check-all-filtered="checkAllFiltered"
      @uncheck-all-filtered="uncheckAllFiltered"
      @close="isFilterOpen = false"
      @update:at-filters="setAtFilters"
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
