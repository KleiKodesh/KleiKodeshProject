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

// Capture tabId at mount time — stable for this component's lifetime (/search is keyed by tabId)
const tabId = tabStore.activeTabId

const zoom = ref<number>(ZOOM_CONFIG.DEFAULT)
const isSearchActive = computed(() => tabStore.activeTab?.route === '/search')
useZoomHandler({ zoom, enabled: isSearchActive })

const { state: indexingState } = useFullTextSearchIndexingStatus()

// Keep the overlay visible for a short window after indexing completes so the
// "finalizing" message is readable. C# sends isIndexing=false at 100% in one shot —
// without this delay the overlay would disappear before the user sees the message.
const showIndexingOverlay = ref(false)
let overlayHideTimer: ReturnType<typeof setTimeout> | null = null
watch(
  indexingState,
  (s) => {
    if (s.isIndexing) {
      if (overlayHideTimer) { clearTimeout(overlayHideTimer); overlayHideTimer = null }
      showIndexingOverlay.value = true
    } else if (showIndexingOverlay.value) {
      // Was showing — keep it up briefly so the user sees the final state
      overlayHideTimer = setTimeout(() => { showIndexingOverlay.value = false }, 1500)
    }
  },
  { deep: true },
)

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
  loadCachedResults,
} = useFullTextSearch(() => indexingState.value.isIndexing)

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

const searchBarRef = ref<InstanceType<typeof FullTextSearchBar> | null>(null)
const filterPanelRef = ref<HTMLElement | null>(null)
const resultsListRef = ref<InstanceType<typeof FullTextSearchResultsList> | null>(null)
const initialScrollIndex = ref<number | undefined>()
const initialScrollOffset = ref<number | undefined>()
const isAdvancedOpen = ref(false)
// Scroll position owned here — updated by SearchResultsList via saveScroll emit
let lastScrollIndex: number | undefined
let lastScrollOffset: number | undefined

const isAdvancedActive = computed(
  () => maxWordDistance.value !== 10 || requireOrdered.value
     || !expandKetiv.value
     || settings.searchWildcardWrap
     || settings.searchGrammarWrap
     || settings.searchContextMarginWords !== 30,
)

useDropdownClose(
  filterPanelRef,
  () => {
    if (isFilterOpen.value) isFilterOpen.value = false
  },
  { toggleButton: computed(() => searchBarRef.value?.filterBtnRef ?? null) },
)

// Re-run the search whenever any advanced setting changes — the current results
// were generated with the old setting values and are now stale.
watch(
  [
    maxWordDistance,
    requireOrdered,
    expandKetiv,
    () => settings.searchContextMarginWords,
    () => settings.searchWildcardWrap,
    () => settings.searchGrammarWrap,
  ],
  () => {
    if (hasSearched.value && executedQuery.value) {
      handleSearch(executedQuery.value)
    }
  },
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
  if (!fromCache) await handleSearch(term || savedQuery)
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

  // Restore search query and results from cache/session. The scroll position
  // is restored automatically by FullTextSearchResultsList's watcher when results arrive.
  await restoreFromTab()

  searchBarRef.value?.focus()
})

// Save filter state whenever the page goes hidden or is unmounted.
// /search is keyed by tabId so unmount = this tab's search instance is gone (tab switched or closed).
// Tab close triggers closeTab() which deletes the IDB key anyway, but saving first is harmless.
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
      ref="resultsListRef"
      :results="filteredResults"
      :search-query="executedQuery"
      :is-searching="isSearching"
      :has-searched="hasSearched"
      :search-error="searchError"
      :db-not-found="indexingState.dbNotFound"
      :initial-scroll-index="initialScrollIndex"
      :initial-scroll-offset="initialScrollOffset"
      :zoom="zoom"
      @result-click="handleResultClick"
      @save-scroll="onSaveScroll"
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
