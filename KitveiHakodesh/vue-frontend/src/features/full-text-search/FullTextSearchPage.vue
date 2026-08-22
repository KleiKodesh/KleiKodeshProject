<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useEventListener, useMediaQuery } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useZoomHandler, ZOOM_CONFIG } from '@/composables/useZoom'
import { useFullTextSearch } from './useFullTextSearch'
import { useFullTextSearchFilters, parseSearchQuery } from './useFullTextSearchFilters'
import { useFullTextSearchIndexingStatus } from './useFullTextSearchIndexingStatus'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useTabStore } from '@/stores/tabStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { lsGet, lsSet, lsDelete } from '@/utils/persistence'
import FullTextSearchBar from './FullTextSearchBar.vue'
import FullTextSearchResultsList from './FullTextSearchResultsList.vue'
import FullTextSearchFilterPanel from './FullTextSearchFilterPanel.vue'
import FullTextSearchAdvancedPanel from './FullTextSearchAdvancedPanel.vue'
import FullTextSearchIndexingOverlay from './FullTextSearchIndexingOverlay.vue'

const paneNavigation = usePaneNavigation()
const tabStore = useTabStore()
const booksStore = useBooksDataStore()
const settings = useSettingsStore()

// Capture tabId at mount time — stable for this component's lifetime (/search is keyed by tabId)
const tabId = paneNavigation.activeTabId

// Synchronous localStorage mirror of the scroll position. The canonical per-tab state lives
// in IDB (setTabViewState), but that write is ASYNC — on a page reload (F5) the document is
// torn down before the transaction commits, so the reload-time save never landed and every
// boot restored the PREVIOUS save (tab-switch saves worked because the SPA keeps running
// long enough for IDB to commit). localStorage writes are synchronous and survive teardown,
// so the mirror always holds the latest position; boot prefers it over the IDB value.
const scrollMirrorKey = `search.scroll:${tabId}`

const zoom = ref<number>(ZOOM_CONFIG.DEFAULT)
const isSearchActive = computed(() => paneNavigation.activeTab?.route === '/search')
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
  expandRelated,
  grammarWrap,
  sortOrder,
  executeSearch,
  cancelSearch,
  clearSearch,
  loadCachedResults,
  clearCachedResults,
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
  checkVisible,
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
// Seed the restore target from the localStorage mirror SYNCHRONOUSLY at setup — it's
// available before the first render, unlike the async IDB read in onMounted. This is what
// lets the results list scroll to the target before its first paint (no flash of the list
// top followed by a jump). The IDB value in onMounted remains as fallback for saves that
// predate the mirror.
const _scrollMirror = lsGet<{ i: number; o: number }>(scrollMirrorKey)
const initialScrollIndex = ref<number | undefined>(_scrollMirror?.i)
const initialScrollOffset = ref<number | undefined>(_scrollMirror?.o)
const isAdvancedOpen = ref(false)
const advancedPanelRef = ref<HTMLElement | null>(null)
// Scroll position owned here — updated by SearchResultsList via saveScroll emit
let lastScrollIndex: number | undefined
let lastScrollOffset: number | undefined

const isAdvancedActive = computed(
  () => maxWordDistance.value !== 10 || requireOrdered.value
     || !expandKetiv.value
     || expandRelated.value
     || grammarWrap.value
     || settings.searchContextMarginWords !== 30,
)

// The filter panel closes on outside-click only in narrow "overlay" mode, where
// it floats over the results. In wide mode (≥520px) it's a persistent side panel
// beside the results, so clicking the results should not dismiss it.
// Breakpoint mirrors the `@media (min-width: 520px)` layout switch below.
const isOverlayMode = useMediaQuery('(max-width: 519.98px)')

useDropdownClose(
  filterPanelRef,
  () => {
    if (isFilterOpen.value) isFilterOpen.value = false
  },
  {
    toggleButton: computed(() => searchBarRef.value?.filterBtnRef ?? null),
    enabled: isOverlayMode,
  },
)

// The advanced panel is a popup over the results in every size, so unlike the filter
// panel it closes on outside-click unconditionally — no overlay-mode gate.
//
// The toggle button has to be named, or clicking it while the panel is open would close
// it and then reopen it: onClickOutside runs on the CLICK, capture-phase on window, so
// it fires before the button's own bubble-phase handler (@click.stop can't cancel a
// capture listener that already ran). Naming the button makes the composable return
// without closing, leaving the single close to the button's toggle. Nothing reads the
// returned `justClosed` — that guard is for toggles that set the flag themselves.
useDropdownClose(
  advancedPanelRef,
  () => {
    if (isAdvancedOpen.value) isAdvancedOpen.value = false
  },
  { toggleButton: computed(() => searchBarRef.value?.advancedBtnRef ?? null) },
)

// Re-run the search whenever any advanced setting changes — the current results
// were generated with the old setting values and are now stale.
watch(
  [
    maxWordDistance,
    requireOrdered,
    expandKetiv,
    expandRelated,
    grammarWrap,
    () => settings.searchContextMarginWords,
  ],
  () => {
    if (hasSearched.value && executedQuery.value) {
      handleSearch(executedQuery.value)
    }
  },
)

// The index builds in the background; a search issued before the first segments
// flush returns 'indexNotReady' and the results area shows that error until the
// user searches again. Re-run the pending search automatically the moment the
// index becomes searchable, so results appear without a manual retry.
watch(
  () => indexingState.value.isReady,
  (ready, wasReady) => {
    if (ready && !wasReady && searchError.value === 'indexNotReady' && executedQuery.value) {
      handleSearch(executedQuery.value)
    }
  },
)

function onSearch(q: string) {
  const { term, atFilters: tokens } = parseSearchQuery(q)
  setAtFilters(tokens)
  paneNavigation.updateActiveTab({ searchQuery: q, title: `חיפוש: ${term || q}` })
  if (term) handleSearch(term)
}
function onClearSearch() {
  paneNavigation.updateActiveTab({ searchQuery: undefined, title: 'חיפוש' })
  handleClearSearch()
}

// The address bar (and anything else outside this page) launches a search by
// patching the tab's searchQuery. When the tab is already on /search the page
// doesn't remount and restoreFromTab never re-runs, so watch the tab's saved
// query and execute external changes here. The page's own onSearch also patches
// the tab, but by then the local input already holds that value — the equality
// guard skips those self-updates.
watch(
  () => paneNavigation.tabs.find((t) => t.id === tabId)?.searchQuery,
  (q) => {
    if (!q || q === searchQuery.value) return
    searchQuery.value = q
    const { term, atFilters: tokens } = parseSearchQuery(q)
    setAtFilters(tokens)
    paneNavigation.updateActiveTab({ title: `חיפוש: ${term || q}` })
    if (term) handleSearch(term)
  },
)

// A /search → /search navigation that carries a position: picking a search row from the
// address-bar dropdown (or Back/Forward onto one). The page is keyed by tabId alone, so
// navigating in place does NOT remount it and none of the mount-time restore above runs.
// The tab patch carries the position instead (tabStore.applyLocationPosition), and this
// watcher is the consuming half — it seeds the same initialScrollIndex the mount path uses
// and re-arms the results list, whose restore watcher is one-shot.
//
// `searchRestore` and `searchQuery` arrive in the SAME patch, so this watcher and the
// searchQuery watcher above both fire for one navigation. We deliberately do NOT depend on
// which runs first: executeSearch is async and awaits cancelSearch() before clearing
// results, so there is no instant at which "results are already the new set" is guaranteed.
// Seeding the target is safe regardless — the child re-arms rather than restoring on the
// spot, and its watcher waits for the target row to actually arrive.
watch(
  () => paneNavigation.tabs.find((t) => t.id === tabId)?.searchRestore,
  (restore) => {
    if (!restore) return
    initialScrollIndex.value = restore.index
    initialScrollOffset.value = restore.offset
    // Consume it, like openTocLineIndex: the field describes ONE navigation, and leaving it
    // set would make an unrelated later patch (a title refresh) re-trigger this restore.
    paneNavigation.updateActiveTab({ searchRestore: undefined })
    resultsListRef.value?.armRestore()
  },
)

function onSaveScroll(pos: { scrollIndex: number; scrollOffset: number }) {
  lastScrollIndex = pos.scrollIndex
  lastScrollOffset = pos.scrollOffset
}

function onNavigateToBook(bookId: number) {
  resultsListRef.value?.scrollToBook(bookId)
}

async function saveFilterState() {
  const captured = resultsListRef.value?.captureScrollPos()
  if (captured) {
    lastScrollIndex = captured.scrollIndex
    lastScrollOffset = captured.scrollOffset
  }
  const allCount = booksStore.allBooks.length
  const isAllChecked = allCount > 0 && checkedBookIds.value.size === allCount
  // Persist an explicit book subset ONLY when it's a real, partial selection. An empty
  // set (filter not yet initialized, or everything unchecked) must never be saved as [] —
  // that restores as "no books" and hides every result. Fall back to "all" (undefined).
  const persistSubset = allCount > 0 && checkedBookIds.value.size > 0 && !isAllChecked
  const state = {
    searchCheckedBookIds: persistSubset ? [...checkedBookIds.value] : undefined,
    searchAtFilters: atFilters.value.length ? [...atFilters.value] : undefined,
    searchScrollIndex: lastScrollIndex,
    searchScrollOffset: lastScrollOffset,
    searchZoom: zoom.value !== ZOOM_CONFIG.DEFAULT ? zoom.value : undefined,
    // Persist the sort only when it's the non-default 'relevance' — 'lineId' is the
    // reset-on-new-search default, so there's nothing to remember for it.
    searchSortOrder: sortOrder.value !== 'lineId' ? sortOrder.value : undefined,
  }
  // Mirror the scroll scalars to localStorage FIRST — synchronous, so it lands even when
  // this runs during reload teardown and the async IDB write below never commits.
  if (state.searchScrollIndex != null) {
    lsSet(scrollMirrorKey, { i: state.searchScrollIndex, o: state.searchScrollOffset ?? 0 })
  }
  tabStore.setTabViewState(tabId, state)
}

async function restoreFromTab() {
  const savedQuery = paneNavigation.activeTab.searchQuery
  if (!savedQuery) return
  searchQuery.value = savedQuery
  const { term, atFilters: tokens } = parseSearchQuery(savedQuery)
  setAtFilters(tokens)
  paneNavigation.updateActiveTab({ title: `חיפוש: ${term || savedQuery}` })
  const fromCache = await loadCachedResults(term || savedQuery)
  if (!fromCache) handleSearch(term || savedQuery)
}

onMounted(async () => {
  await booksStore.ensureLoaded()

  const saved = await tabStore.getTabViewState(tabId)

  if (saved?.searchCheckedBookIds != null) {
    const validIds = new Set(booksStore.allBooks.map((b) => b.id))
    const restored = new Set(saved.searchCheckedBookIds.filter((id) => validIds.has(id)))
    // An empty restored set means the saved filter is stale or degenerate (e.g. it was
    // persisted before the book list loaded, or its ids no longer exist). Restoring it
    // as-is would filter out EVERY result ("לא נמצאו תוצאות") — default to all books.
    if (restored.size > 0) setCheckedBookIds(restored)
    else initCheckedBooks()
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

  // The localStorage mirror was already read synchronously at setup (see initialScrollIndex
  // declaration) — it's always at least as fresh as IDB, because every save writes the
  // mirror synchronously while the IDB write is async and dies on reload teardown (this was
  // the "reload restores the PREVIOUS position" bug). IDB is only the fallback for saves
  // that predate the mirror.
  if (_scrollMirror == null && saved?.searchScrollIndex != null) {
    initialScrollIndex.value = saved.searchScrollIndex
    initialScrollOffset.value = saved.searchScrollOffset ?? 0
    // Do NOT seed lastScrollIndex/lastScrollOffset from the saved state.
    // Those track the current session's live position (via onSaveScroll / captureScrollPos).
    // Seeding them here means onBeforeUnmount would fall back to the stale restored value
    // and overwrite whatever visibilitychange correctly saved for the current session.
  }

  // Restore search query and results from cache/session. The scroll position
  // is restored automatically by FullTextSearchResultsList's watcher when results arrive.
  await restoreFromTab()

  // Restore the per-tab sort AFTER restoreFromTab: executeSearch() resets sortOrder to
  // 'lineId' synchronously at its start, so setting it here wins. For cache-restored
  // results the sortOrder watch re-sorts immediately; for a live re-search the value is
  // read by finalizeSort() when that search completes.
  if (saved?.searchSortOrder) sortOrder.value = saved.searchSortOrder

  // Silent focus: place the cursor in the search field on restore without popping the
  // autofill bubble. The restored query is often a prefix of a longer recent search, so a
  // non-silent focus would open the suggestion dropdown unbidden the moment the tab restores.
  searchBarRef.value?.focus({ silent: true })
})

// Save filter state whenever the page goes hidden or is unmounted.
// /search is keyed by tabId so unmount = this tab's search instance is gone (tab switched or closed).
// Tab close triggers closeTab() which deletes the IDB key anyway, but saving first is harmless.
useEventListener(document, 'visibilitychange', () => {
  if (document.visibilityState === 'hidden') saveFilterState()
})
// Reload safety net: the synchronous localStorage mirror inside saveFilterState is the part
// that must land during teardown; running it from beforeunload guarantees it even if the
// visibilitychange ordering varies. Idempotent with the handler above.
useEventListener(window, 'beforeunload', () => { saveFilterState() })
onBeforeUnmount(() => {
  // Stop the backend search. This page is NOT kept alive — it unmounts on a tab switch,
  // on a same-tab navigation to another route, and on tab close. Without this, the C#
  // search thread (or the dev service stream) keeps grinding through a full-corpus query
  // for a page nobody is looking at, and its batch events land on a dead component.
  // Fire-and-forget: cancelSearch tears down the local listeners synchronously and only
  // the FtsSearchCancel bridge round-trip is async, which unmount need not await.
  if (isSearching.value) void cancelSearch()

  // If the tab no longer exists in the store, it was closed — clear its cache entry
  // since the results are no longer needed for session restore or tab switching.
  // If the tab still exists, the user just switched away — keep the cache for restore.
  const tabStillExists = tabStore.pane1Tabs.concat(tabStore.pane2Tabs).some((t) => t.id === tabId)
  if (!tabStillExists && executedQuery.value) {
    clearCachedResults(executedQuery.value)
    lsDelete(scrollMirrorKey) // tab closed — drop its scroll mirror too
  }
  saveFilterState()
  if (overlayHideTimer) clearTimeout(overlayHideTimer)
})
</script>

<template>
  <div class="search-page">
    <div class="search-bar-dock">
      <FullTextSearchBar
        ref="searchBarRef"
        v-model:search-query="searchQuery"
        :is-searching="isSearching"
        :result-count="filteredResults.length"
        :total-result-count="results.length"
        :filter-count="checkedBookIds.size"
        :at-filter-count="atFilters.length"
        :is-advanced-open="isAdvancedOpen"
        :is-advanced-active="isAdvancedActive"
        v-model:sort-order="sortOrder"
        @search="onSearch"
        @cancel="cancelSearch"
        @toggle-filter="isFilterOpen = !isFilterOpen"
        @toggle-advanced="isAdvancedOpen = !isAdvancedOpen"
        @clear="onClearSearch"
      />
      <FullTextSearchAdvancedPanel
        v-if="isAdvancedOpen"
        ref="advancedPanelRef"
        :max-word-distance="maxWordDistance"
        :require-ordered="requireOrdered"
        :context-words="settings.searchContextMarginWords"
        :expand-ketiv="expandKetiv"
        :expand-related="expandRelated"
        :grammar-wrap="grammarWrap"
        @update:max-word-distance="maxWordDistance = $event"
        @update:require-ordered="requireOrdered = $event"
        @update:context-words="settings.searchContextMarginWords = $event"
        @update:expand-ketiv="expandKetiv = $event"
        @update:expand-related="expandRelated = $event"
        @update:grammar-wrap="grammarWrap = $event"
        @close="isAdvancedOpen = false"
      />
    </div>

    <div class="results-area">
      <div class="results-list-wrap">
        <FullTextSearchResultsList
          ref="resultsListRef"
          :results="filteredResults"
          :total-results="results.length"
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
      </div>

      <div v-if="isFilterOpen" class="filter-shell" @click.self="isFilterOpen = false">
        <FullTextSearchFilterPanel
          ref="filterPanelRef"
          :checked-book-ids="checkedBookIds"
          :result-counts="resultCounts"
          :has-searched="hasSearched"
          :at-filters="atFilters"
          @toggle-book="toggleBook"
          @toggle-category="toggleCategory"
          @check-all="checkAll"
          @uncheck-all="uncheckAll"
          @check-visible="checkVisible"
          @close="isFilterOpen = false"
          @update:at-filters="setAtFilters"
          @navigate-to-book="onNavigateToBook"
        />
      </div>
    </div>

    <FullTextSearchIndexingOverlay v-if="showIndexingOverlay" :state="indexingState" />
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
.results-area {
  flex: 1;
  min-height: 0;
  position: relative;
  display: flex;
  flex-direction: column;
}
/* Anchor for the advanced panel: on wide screens it floats as a popup below
   the search bar, so it needs the bar's bottom edge as its containing block. */
.search-bar-dock {
  position: relative;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  min-height: 0;
  /* z-index without position:relative on the results area below means the dock's
     popups (advanced panel, sort dropdown) paint over the results list. */
  z-index: 5;
}
.results-list-wrap {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
/* Narrow overlay mode: shell covers the results area with a semitransparent backdrop */
.filter-shell {
  position: absolute;
  inset: 0;
  z-index: 10;
  background: rgba(0, 0, 0, 0.28);
}

/* Wide mode: shell is a transparent flex child; panel sits beside the results */
@media (min-width: 520px) {
  .results-area {
    flex-direction: row-reverse;
  }
  .results-list-wrap {
    min-width: 0;
  }
  /* Flex, so the panel inside can stretch to the shell's full height. As a plain block
     the panel had nothing to stretch against and collapsed to its content height. */
  .filter-shell {
    position: static;
    background: none;
    height: 100%;
    flex-shrink: 0;
    display: flex;
  }
  /* `height: 100%` would resolve against the shell and then ADD the panel's own margin,
     overflowing the area by that much at top and bottom. Stretching instead lets the
     margin be a true inset: the panel fills the height that's left over. */
  .results-area :deep(.panel) {
    position: static;
    align-self: stretch;
    flex-shrink: 0;
  }
}
</style>
