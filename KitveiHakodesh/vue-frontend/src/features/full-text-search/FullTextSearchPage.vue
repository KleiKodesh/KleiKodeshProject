<script setup lang="ts">
import { t } from '@/i18n'
import PageCard from '@/components/PageCard.vue'
import { ref, computed, inject, watch, onMounted, onBeforeUnmount, onScopeDispose, nextTick } from 'vue'
import { useElementSize, useEventListener, useMediaQuery } from '@vueuse/core'
import { registerPositionCapture } from '@/webview-host/persistenceFlush'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useZoomHandler, ZOOM_CONFIG } from '@/composables/useZoom'
import { useFullTextSearch } from './useFullTextSearch'
import { useFullTextSearchFilters, parseSearchQuery } from './useFullTextSearchFilters'
import { useFullTextSearchIndexingStatus } from './useFullTextSearchIndexingStatus'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useTabStore } from '@/stores/tabStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { lsGet, lsSet, lsDelete } from '@/utils/persistence'
import FullTextSearchBar from './FullTextSearchBar.vue'
import FullTextSearchResultsList from './FullTextSearchResultsList.vue'
import FullTextSearchFilterPanel from './FullTextSearchFilterPanel.vue'
import FullTextSearchOptionsPanel from './FullTextSearchOptionsPanel.vue'
import { WORD_OPTIONS } from './queryWordSyntax'
import FullTextSearchIndexingOverlay from './FullTextSearchIndexingOverlay.vue'

const paneNavigation = usePaneNavigation()
// For the page's own keyboard shortcuts: which pane this is, and whether it is the
// focused one while split view is open (see the window handler below).
const paneId = inject<1 | 2>('paneId', 1)
const bookViewStore = useBookViewStore()
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
  resultsVersion,
  resultCount,
  matchedTermsOf,
  isSearching,
  stageLabel,
  hasSearched,
  executedQuery,
  searchError,
  maxWordDistance,
  requireOrdered,
  allWords,
  setAllWords,
  perWord,
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
  filteredVersion,
  filteredCount,
  filteredGeneration,
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
  () => resultsVersion.value,
  () => executedQuery.value,
  matchedTermsOf,
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
const isOptionsOpen = ref(false)
const optionsPanelRef = ref<HTMLElement | null>(null)
// The options dropdown fills the whole height under the bar, i.e. the results area's.
const resultsAreaRef = ref<HTMLElement | null>(null)
const { height: resultsAreaHeight } = useElementSize(resultsAreaRef)
// Scroll position owned here — updated by SearchResultsList via saveScroll emit
// Set for one tick while the searchRestore watcher is applying a history position, so the
// new-search reset can recognize that navigation no matter which watcher runs first.
let restoringNonce: number | null = null
let lastScrollIndex: number | undefined
let lastScrollOffset: number | undefined

// Clean page (nothing searched yet, no popup open): offer the search options up front
// as a centered card so the user can set them before launching the first search.
const showOptionsCard = computed(
  () => !hasSearched.value && !isSearching.value && !executedQuery.value && !isOptionsOpen.value
     && !indexingState.value.dbNotFound && !searchError.value
     // A tab returning to a saved search restores it asynchronously (onMounted); the saved
     // query is known synchronously, so gate on it or the card flashes before the restore.
     && !paneNavigation.tabs.find((t) => t.id === tabId)?.searchQuery,
)

const cardBarSlotId = `fts-card-bar-${String(tabId).replace(/[^\w-]/g, '_')}`

const isOptionsActive = computed(
  () => maxWordDistance.value !== 10 || requireOrdered.value
     || WORD_OPTIONS.some((option) => allWords.value[option] !== (option === 'ketiv'))
     || Object.values(perWord.value).some((words) => words?.length)
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
    // A full-height side panel, not a popup: the viewport fit would cap it 8px short
    // of the bottom edge (same opt-out as BookViewSidePanel).
    fitToViewport: false,
  },
)

// The options panel is a popup over the results in every size, so unlike the filter
// panel it closes on outside-click unconditionally — no overlay-mode gate.
//
// The toggle button has to be named, or clicking it while the panel is open would close
// it and then reopen it: onClickOutside runs on the CLICK, capture-phase on window, so
// it fires before the button's own bubble-phase handler (@click.stop can't cancel a
// capture listener that already ran). Naming the button makes the composable return
// without closing, leaving the single close to the button's toggle. Nothing reads the
// returned `justClosed` — that guard is for toggles that set the flag themselves.
useDropdownClose(
  optionsPanelRef,
  () => {
    if (isOptionsOpen.value) isOptionsOpen.value = false
  },
  { toggleButton: computed(() => searchBarRef.value?.optionsBtnRef ?? null) },
)

// A search on screen re-runs when an option changes (its results are stale), once per user action.
let rerunScheduled = false
function scheduleRerun() {
  if (rerunScheduled) return
  rerunScheduled = true
  void nextTick(() => {
    rerunScheduled = false
    if (hasSearched.value && executedQuery.value) handleSearch(executedQuery.value)
  })
}

watch(
  [
    maxWordDistance,
    requireOrdered,
    allWords,
    perWord,
    () => settings.searchContextMarginWords,
  ],
  () => scheduleRerun(),
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

// A NEW search starts at the top of its OWN result list. The saved scroll position is
// per-tab (not per-query), so without clearing it here the previous search's index would be
// restored into the new results -- the list would open mid-way instead of at the first hit.
// Only user-initiated NEW searches call this: the filter/sort and index-ready watchers above
// re-run the SAME query and must keep the user where they were.
function resetScrollForNewSearch() {
  // Back/Forward (and picking a search row from the address-bar dropdown) patches
  // `searchRestore` and `searchQuery` in the SAME patch, so this runs for that navigation
  // too -- and its sibling watcher below is deliberately restoring a position. Order between
  // the two watchers is not guaranteed, so defer to the explicit intent whenever the field is
  // still pending: that navigation is a RESTORE, not a new search.
  if (paneNavigation.tabs.find((t) => t.id === tabId)?.searchRestore || restoringNonce != null) return
  initialScrollIndex.value = undefined
  initialScrollOffset.value = undefined
  lastScrollIndex = undefined
  lastScrollOffset = undefined
  lsDelete(scrollMirrorKey)
  // Clear the IDB copy too, not just the mirror. The mirror is only PREFERRED over IDB on
  // boot -- when it is absent the IDB value is still used as the legacy fallback, so leaving
  // it behind means a reload right after a new search would restore the stale index anyway.
  // Go through saveFilterState: setTabViewState REPLACES the whole record, so writing the
  // scroll scalars alone would drop the tab's checked books, filters, zoom and sort order.
  // lastScroll* are already cleared above, so this persists them as absent.
  void saveFilterState(true)
  resultsListRef.value?.cancelRestore()
}

function onSearch(q: string) {
  const { term, atFilters: tokens } = parseSearchQuery(q)
  setAtFilters(tokens)
  paneNavigation.updateActiveTab({ searchQuery: q, title: t('fullTextSearch.tabTitleQuery', { q: term || q }) })
  if (term) {
    resetScrollForNewSearch()
    handleSearch(term)
  }
}
function onClearSearch() {
  paneNavigation.updateActiveTab({ searchQuery: undefined, title: t('fullTextSearch.tabTitle') })
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
    paneNavigation.updateActiveTab({ title: t('fullTextSearch.tabTitleQuery', { q: term || q }) })
    if (term) {
      resetScrollForNewSearch()
      handleSearch(term)
    }
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
    // Claim the navigation BEFORE consuming the field: resetScrollForNewSearch checks
    // `searchRestore` to detect a restore-navigation, but consuming it below makes that
    // check fail if this watcher happens to run FIRST. The nonce marks which navigation is
    // spoken for; the flag is cleared on the next tick, once both watchers have run.
    restoringNonce = restore.nonce
    nextTick(() => { restoringNonce = null })
    initialScrollIndex.value = restore.index
    initialScrollOffset.value = restore.offset
    // Consume it, like openTocLineIndex: the field describes ONE navigation, and leaving it
    // set would make an unrelated later patch (a title refresh) re-trigger this restore.
    paneNavigation.updateActiveTab({ searchRestore: undefined })
    // The query this navigation shows — the term the list's `searchQuery` will carry.
    const tabQuery = paneNavigation.tabs.find((t) => t.id === tabId)?.searchQuery ?? ''
    resultsListRef.value?.armRestore(parseSearchQuery(tabQuery).term || tabQuery)
  },
)

// Combobox model: the arrows move the results highlight and Enter opens the
// highlighted result — through the same handleResultClick a click goes through. The
// bar forwards its keydown here, and the window handler below covers the rest of the
// page; the bar is a sibling of the list, so either hop goes through this parent.
function onResultsKeydown(event: KeyboardEvent): boolean {
  return resultsListRef.value?.onNavigationKeydown(event) ?? false
}

/**
 * A focused field that owns EVERY one of these keys: the search field, where the
 * forwarding above runs anyway with the recent-searches dropdown ahead of it, any
 * other text input on the page, and `select`, whose options the arrows pick.
 */
function focusedElementOwnsTheseKeys(element: Element | null): boolean {
  if (!(element instanceof HTMLElement)) return false
  return element.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(element.tagName)
}

/**
 * A focused control that Enter ACTIVATES: taking Enter there would open a result
 * instead of pressing the filter button. The arrows mean nothing to these, so they
 * keep only the one key — deliberately, and not as tidiness.
 *
 * Caret browsing is the reason. With it on, Chromium moves DOM focus to focusable
 * elements as the caret travels through the text, and every result row carries two
 * icon buttons. Were these to keep the arrows too, the list would stop responding
 * to them for as long as the caret happened to rest on a button: the bug this whole
 * change is about, back intermittently and nearly impossible to describe.
 */
function focusedElementOwnsEnter(element: Element | null): boolean {
  if (!(element instanceof HTMLElement)) return false
  return element.getAttribute('role') === 'button' || ['BUTTON', 'A'].includes(element.tagName)
}

// The same keys from ANYWHERE on this page, not only from the search field.
//
// Bound to the field alone the feature was unreachable in practice, because focus is
// usually not in the field by the time the reader wants the arrows:
//
//   · Running the first search teleports the bar out of the empty page's card into
//     the dock. A Teleport MOVES the DOM node, and Chromium blurs a focused element
//     that leaves the document even for an instant — so focus is on <body> exactly
//     when the results appear.
//   · The results scroller carries tabindex="0", so a click anywhere in the list —
//     to scroll it, to select a word — focuses the scroller.
//
// From either state the field's keydown never fired and the arrows fell through to
// whatever Chromium does with them on the focused scroller. For this app's readers
// that is usually CARET BROWSING (F7): it is their normal working mode, not an edge
// case — it is how they walk a book line by line — so with it on the arrows drag the
// document caret through the result text while the highlight never moves and Enter
// never arrives. That is the Word task pane's normal state, which is where it was
// reported.
//
// Guarded like the title bar's and the book view's shortcuts: this pane focused, and
// this pane's active tab actually being the search page. That guard IS the rule for
// where the caret keeps the arrows and where the list takes them: the book view's
// lines pane is another route, where this page is not mounted at all and the caret
// goes on being driven by the arrows exactly as before. Caret browsing is never
// turned off and no key is taken outside this page.
//
// CAPTURE phase, as those two also use: arrow scrolling, the caret-browsing caret
// move and the scroller's own Home/End are all DEFAULT actions, which Blink runs
// after dispatch and only for a key nobody cancelled — so preventDefault settles
// the precedence, and capture gets there before any of the page's own handlers.
// stopPropagation as well, so a key taken here never reaches the scroller's
// viewport: a keydown there is what marks a scroll as the reader's own for the
// scrollbar's position label, and the landing this handler performs would otherwise
// raise that label's bubble over the bar for a keystroke that scrolled nothing by
// hand. (The label is upstream's and is untouched; it still shows for real
// scrolling — wheel, drag, hover.)
useEventListener(
  window,
  'keydown',
  (event: KeyboardEvent) => {
    if (event.defaultPrevented) return
    if (bookViewStore.splitViewEnabled && bookViewStore.focusedPaneId !== paneId) return
    if (!isSearchActive.value) return
    if (focusedElementOwnsTheseKeys(document.activeElement)) return
    if (event.code === 'Enter' && focusedElementOwnsEnter(document.activeElement)) return
    // A dialog sits above the whole page: its own Enter must not open a result too.
    if (document.querySelector('.confirm-backdrop, .alert-backdrop')) return
    if (!onResultsKeydown(event)) return
    event.preventDefault()
    event.stopPropagation()
  },
  { capture: true },
)

// `searchQuery` is the field's LIVE text (v-model), so this fires on every keystroke:
// the results on screen are the previous query's until a search is actually run, and a
// highlight left over from them would swallow the Enter meant to search what was just
// typed. Editing the query therefore drops the highlight, and Enter searches again.
watch(searchQuery, () => resultsListRef.value?.clearHighlight())

function onSaveScroll(pos: { scrollIndex: number; scrollOffset: number }) {
  lastScrollIndex = pos.scrollIndex
  lastScrollOffset = pos.scrollOffset
}

function onNavigateToBook(bookId: number) {
  resultsListRef.value?.scrollToBook(bookId)
}

// `keepScrollCleared` is for the new-search reset: it has just nulled the scroll scalars on
// purpose, and recapturing the live position here would write the OUTGOING search's index
// straight back into both the mirror and IDB.
async function saveFilterState(keepScrollCleared = false) {
  const captured = keepScrollCleared ? null : resultsListRef.value?.captureScrollPos()
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
    // Persist the sort only when it's not the default — 'catalogOrder' is the
    // reset-on-new-search default, so there's nothing to remember for it.
    searchSortOrder: sortOrder.value !== 'catalogOrder' ? sortOrder.value : undefined,
    searchPerWord: Object.keys(perWord.value).length ? perWord.value : undefined,
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
  paneNavigation.updateActiveTab({ title: t('fullTextSearch.tabTitleQuery', { q: term || savedQuery }) })
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

  if (saved?.searchPerWord) perWord.value = saved.searchPerWord

  if (saved?.searchAtFilters?.length) {
    setAtFilters(saved.searchAtFilters)
  }

  // Restore zoom BEFORE restoring scroll — zoom changes the rows' heights, so a scroll
  // restored before the zoom applies lands the list in the wrong place.
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
  // 'catalogOrder' synchronously at its start, so setting it here wins. For cache-restored
  // results the sortOrder watch re-sorts immediately; for a live re-search the value is
  // read by finalizeSort() when that search completes.
  if (saved?.searchSortOrder) sortOrder.value = saved.searchSortOrder

  // Silent focus: place the cursor in the search field on restore without popping the
  // recent-searches popup. The restored query is often a prefix of a longer recent search, so a
  // non-silent focus would open the suggestion dropdown unbidden the moment the tab restores.
  // selectAll: the restored term is one the user has already searched, so the likely next
  // move is a NEW search — pre-selecting it means typing replaces it instead of appending.
  // Editing the old term is still one click or arrow key away.
  searchBarRef.value?.focus({ silent: true, selectAll: true })
})

// Save filter state whenever the page goes hidden, the host closes, or the page unmounts.
// The shared flush (persistenceFlush) covers browser hide/unload AND the WebView2 host's
// close, which fires no browser event; unmount is handled below.
// /search is keyed by tabId so unmount = this tab's search instance is gone (tab switched or closed).
onScopeDispose(registerPositionCapture(() => saveFilterState(), tabId))
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
    <!-- Hidden while the clean-page card holds the bar, so the empty dock takes no room. -->
    <div v-show="!showOptionsCard" class="search-bar-dock" :style="{ '--options-panel-height': `${resultsAreaHeight}px` }">
      <!-- On the clean page the bar lives inside the centered card; Teleport moves the SAME
           instance (no remount), so typing, focus and refs survive the hop back to the top. -->
      <Teleport defer :to="`#${cardBarSlotId}`" :disabled="!showOptionsCard">
      <FullTextSearchBar
        ref="searchBarRef"
        v-model:search-query="searchQuery"
        :is-searching="isSearching"
        :result-count="filteredCount"
        :total-result-count="resultCount"
        :filter-count="checkedBookIds.size"
        :at-filter-count="atFilters.length"
        :is-options-open="isOptionsOpen"
        :is-options-active="isOptionsActive"
        :show-options-toggle="!showOptionsCard"
        :results-keydown="onResultsKeydown"
        v-model:sort-order="sortOrder"
        @search="onSearch"
        @cancel="cancelSearch"
        @toggle-filter="isFilterOpen = !isFilterOpen"
        @toggle-options="isOptionsOpen = !isOptionsOpen"
        @clear="onClearSearch"
      />
      </Teleport>
      <FullTextSearchOptionsPanel
        v-if="isOptionsOpen"
        ref="optionsPanelRef"
        :max-word-distance="maxWordDistance"
        :require-ordered="requireOrdered"
        :context-words="settings.searchContextMarginWords"
        :all-words="allWords"
        :per-word="perWord"
        :query="searchQuery"
        @update:max-word-distance="maxWordDistance = $event"
        @update:require-ordered="requireOrdered = $event"
        @update:context-words="settings.searchContextMarginWords = $event"
        @set-all-words="setAllWords"
        @update:per-word="perWord = $event"
        @close="isOptionsOpen = false"
      />
    </div>

    <div ref="resultsAreaRef" class="results-area">
      <!-- v-show, not v-if: the Teleport target must stay in the DOM while the bar moves out. -->
      <PageCard v-show="showOptionsCard">
        <div :id="cardBarSlotId" class="card-bar-slot" />
        <FullTextSearchOptionsPanel
          as-card
          :max-word-distance="maxWordDistance"
          :require-ordered="requireOrdered"
          :context-words="settings.searchContextMarginWords"
          :all-words="allWords"
          :per-word="perWord"
          :query="searchQuery"
          @update:max-word-distance="maxWordDistance = $event"
          @update:require-ordered="requireOrdered = $event"
          @update:context-words="settings.searchContextMarginWords = $event"
          @set-all-words="setAllWords"
          @update:per-word="perWord = $event"
        />
      </PageCard>
      <div v-if="!showOptionsCard" class="results-list-wrap">
        <!-- Inside the list column (not the top dock) so the filter side panel, a sibling
             column, runs the full height beside it, and the options dropdown covers it. -->
        <!-- Always reserved (fixed height) so the results never jump when the text hides. -->
        <div class="search-stage-row" aria-live="polite">
          <template v-if="isSearching">
            {{ stageLabel }}
            <span v-if="filteredCount" class="search-stage-count">{{ filteredCount.toLocaleString() }}</span>
          </template>
          <span v-else-if="resultCount" dir="auto">
            <span class="search-stage-count">{{ filteredCount.toLocaleString() }}<template v-if="filteredCount < resultCount"> / {{ resultCount.toLocaleString() }}</template></span>
            {{ $t('fullTextSearch.results') }}
          </span>
        </div>
        <FullTextSearchResultsList
          ref="resultsListRef"
          :results="filteredResults"
          :results-version="filteredVersion"
          :results-generation="filteredGeneration"
          :search-query="executedQuery"
          :is-searching="isSearching"
          :has-searched="hasSearched"
          :search-error="searchError"
          :db-not-found="indexingState.dbNotFound"
          :index-build-failed="indexingState.buildFailed"
          :initial-scroll-index="initialScrollIndex"
          :initial-scroll-offset="initialScrollOffset"
          :zoom="zoom"
          @result-click="handleResultClick"
          @save-scroll="onSaveScroll"
        />
      </div>

      <div v-if="isFilterOpen" class="filter-shell" :class="{ floating: showOptionsCard }" @click.self="isFilterOpen = false">
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
.search-stage-row {
  /* Text sits at the left edge (the row is RTL, so `end` = left). */
  text-align: end;
  height: 14px;
  flex-shrink: 0;
  line-height: 14px;
  /* Physical: extra inset on the left nudges the left-aligned text rightward. */
  padding: 0 12px 0 18px;
  font-size: 11px;
  color: var(--text-secondary);
  opacity: 0.75;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.search-stage-count {
  unicode-bidi: isolate;
  direction: ltr;
}
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
/* Anchor for the options panel: on wide screens it floats as a popup below
   the search bar, so it needs the bar's bottom edge as its containing block. */
.search-bar-dock {
  position: relative;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  min-height: 0;
  /* The results area has no stacking context, so its filter shell (z-index 10) competes
     with this dock directly: stay above it so the dock's popups (options panel, sort
     dropdown) paint over both the results list and the filter side panel. */
  z-index: 11;
}
/* Room around the bar inside the card (TopSearchBar's own 8px/6px is tuned for the
   page edge, too tight in the card). */
.card-bar-slot {
  flex-shrink: 0;
  padding: 4px 0;
}
/* The card owns the frame; the panel inside is just its lower section. */
.results-area :deep(.options-panel.as-card) {
  width: 100%;
  max-width: 100%;
  min-height: 0;
  flex: 1 1 auto;
  border: none;
  border-radius: 0;
  box-shadow: none;
}
/* Full-width card (PageCard <=600px) takes the app background; the panel must
   not paint its own bg-secondary over it. Breakpoint mirrors PageCard.vue. */
@media (max-width: 600px) {
  .results-area :deep(.options-panel.as-card),
  .results-area :deep(.options-panel.as-card .panel-header) {
    background: none;
  }
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
  /* Over the clean-page card the panel floats instead of taking a column, so the
     centered card is not pushed aside. */
  .filter-shell.floating {
    position: absolute;
    inset-block: 0;
    inset-inline-start: 0;
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
