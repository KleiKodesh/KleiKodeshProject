/**
 * Central composable for the book view page.
 * Owns all data loading, state, event handlers, and watchers.
 * BookViewPage.vue is a shell that calls this and passes results to the template.
 *
 * Concerns extracted into focused composables:
 * - useBookViewKeyboardShortcuts  — Ctrl+zoom and Ctrl+arrow section navigation
 * - useBookViewLineSelection      — single-click and shift-click multi-select
 * - useBookViewSidePanel          — TOC / commentary-tree panel open/close
 * - useBookViewSearchPanel        — search panel state and match navigation
 * - useBookViewCommentaryPanel    — commentary panel visibility and scroll restore
 */
import { ref, reactive, computed, watch, nextTick, onMounted, onBeforeUnmount, inject } from 'vue'
import { storeToRefs } from 'pinia'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useToc } from './toc/useBookViewToc'
import { useLines } from './lines/useBookViewLinesTable'
import { useCommentary } from './commentary/useCommentary'
import { ensureConnectionTypeNamesLoaded } from './commentary/commentaryConnectionTypes'
import { useBookViewSearch } from './useBookViewSearch'
import { useCommentarySearch } from './commentary/useCommentarySearch'
import { useBookViewTocScrollTracking } from './toc/useBookViewTocScrollTracking'
import { usePinnedCommentary } from './useBookViewPinnedCommentary'
import { useCommentaryNavigation } from './commentary/useCommentaryNavigation'
import { useBookViewScrollSync } from './useBookViewScrollSync'
import { useBookViewSessionRestore } from './useBookViewSessionRestore'
import { useBookViewKeyboardShortcuts } from './useBookViewKeyboardShortcuts'
import { useBookViewLineSelection } from './useBookViewLineSelection'
import { useBookViewSidePanel } from './useBookViewSidePanel'
import { useBookViewSearchPanel } from './useBookViewSearchPanel'
import { useBookViewCommentaryPanel } from './useBookViewCommentaryPanel'
import { useBookViewTocNavigation } from './useBookViewTocNavigation'
import { useBookViewCommentaryAnnotations } from './useBookViewCommentaryAnnotations'
import type { CommentaryTreeState } from './bookViewTypes'
export type { SearchMode } from './bookViewTypes'

// Component instance types — used only for ref typing
type ToolbarInstance = { tocBtnRef: HTMLElement | null }
type LinesContentInstance = {
  scrollToLineId: (lineId: number, lineIndex?: number) => void
  scrollToLineIndex: (lineIndex: number, occurrence?: number, forceScroll?: boolean) => void
  focusScroller: () => void
  $el?: HTMLElement
}
type SearchBarInstance = { focus: () => void }
type CommentaryViewInstance = {
  topVisibleFlatIndex: number
  activeBookId: number | null
  activePinnedGroup: { bookId: number; sectionLabel: string; subSectionLabel: string } | null
  getFilterButtonEl?: () => HTMLElement | null
  scrollToGroup: (bookId: number) => void
  scrollToFlatIndex: (index: number, occurrence?: number) => void
  captureScrollPos?: () => { scrollIndex: number; scrollOffset: number } | null
  restoreCommentaryScrollPos: (index: number, offset: number) => Promise<void>
  $el?: HTMLElement
}

export function useBookView(
  toolbarRef: () => ToolbarInstance | null,
  linesContentRef: () => LinesContentInstance | null,
  searchBarRef: () => SearchBarInstance | null,
  commentaryViewRef: () => CommentaryViewInstance | null,
) {
  const bookViewStore = useBookViewStore()
  const tabStore = useTabStore()
  const settingsStore = useSettingsStore()
  const paneNavigation = usePaneNavigation()
  const paneId = inject<1 | 2>('paneId', 1)
  const { toolbarPosition } = storeToRefs(bookViewStore)

  // ── Tab state captured at mount (stable for component lifetime) ──────────
  // Read from paneNavigation so pane 2 gets its own active tab, not pane 1's.

  const tabId = paneNavigation.activeTabId
  const bookId = paneNavigation.activeTab.bookId
  const bookTitle = paneNavigation.activeTab.title

  const openTocEntryId = paneNavigation.activeTab.openTocEntryId
  const openTocLineIndex = paneNavigation.activeTab.openTocLineIndex
  const flashOpenLine = paneNavigation.activeTab.flashOpenLine ?? false
  const searchHighlightLineIndex = paneNavigation.activeTab.searchHighlightLineIndex
  const searchHighlightQuery = paneNavigation.activeTab.searchHighlightQuery ?? ''
  const searchHighlightSnippet = paneNavigation.activeTab.searchHighlightSnippet
  const searchHighlightTerms = paneNavigation.activeTab.searchHighlightTerms

  // A deep-link open (flashOpenLine) sets openTocLineIndex without openTocEntryId, so
  // include it in the clear gate — otherwise the flash flag would survive a remount.
  if (openTocEntryId != null || flashOpenLine)
    paneNavigation.updateActiveTab({
      openTocEntryId: undefined,
      openTocLineIndex: undefined,
      flashOpenLine: undefined,
      searchHighlightLineIndex: undefined,
      searchHighlightQuery: undefined,
      searchHighlightSnippet: undefined,
      searchHighlightTerms: undefined,
    })

  // ── Data loading ─────────────────────────────────────────────────────────

  const {
    getActiveTocEntry, getTocPath,
    altTocSections, selectedAltTocSection,
    tocEntries, tocSearchTree,
    loading: tocLoading, error: tocError, tocLoaded,
    loadAltTocSections,
  } = useToc(() => bookId, () => bookTitle)

  const { lines, prioritise, prefetch, hasCommentaries, hasRelatedBooks, hasTeamim: bookHasTeamim } = useLines(() => bookId)

  // Warm up the connection type ID table immediately — single tiny query that must
  // resolve before any line-tap commentary load fires its reverse queries.
  void ensureConnectionTypeNamesLoaded()

  const hasToc = computed(() => tocLoaded.value && tocEntries.value.length > 0)

  // ── Core reactive state ───────────────────────────────────────────────────

  const activeTocEntryId = ref<number | undefined>(undefined)
  const commentaryTreeState = reactive<CommentaryTreeState>({ searchQuery: '', tokens: [], visibilityList: [] })
  const restoredCommentaryMode = ref<'off' | 'bottom' | 'side' | undefined>(undefined)
  const restoredCommentaryFraction = ref<number | undefined>(undefined)
  const restoredStackedCommentaryFraction = ref<number | undefined>(undefined)

  const selectedLineId = ref<number | null>(null)
  const commentaryLineId = ref<number | null>(null)

  // Placeholder ref updated once pinnedCommentary is set up below, so that
  // useCommentary can read the pinned group without a circular dependency.
  const pinnedCommentaryGroupForDisplay = ref<import('./bookViewTypes').PinnedCommentaryGroup | null>(null)

  // ── Line selection ────────────────────────────────────────────────────────
  // setPendingPin is resolved after usePinnedCommentary is set up; we pass a
  // closure so the dependency is only evaluated at call time, not at setup time.
  const pendingPinFns = {
    setPendingPin: (_group: { bookId: number; sectionLabel: string; subSectionLabel: string } | null) => {},
    getActivePinnedGroup: (): { bookId: number; sectionLabel: string; subSectionLabel: string } | null => null,
  }

  const {
    manualSelectionLineIds,
    selectedSectionLineIds, clearManualSelection, onLineSelected: onLineSelectedRaw,
  } = useBookViewLineSelection(
    () => lines.value,
    () => tocEntries.value,
    commentaryLineId,
    selectedLineId,
    (group) => pendingPinFns.setPendingPin(group),
    () => pendingPinFns.getActivePinnedGroup(),
  )

  /**
   * An explicit line click invalidates any saved commentary scroll position:
   * the pinned-group jump owns positioning for fresh navigation, and a stale
   * session-restored position must not be applied to the new line's commentary
   * (it would land on an arbitrary commentator and suppress the pinned jump).
   * The position refs repopulate naturally — the pinned jump's programmatic
   * scroll fires a scroll event that re-captures them.
   * (commentaryPanel is initialized below; clicks can only happen after setup.)
   */
  function onLineSelected(lineId: number, isShiftClick: boolean) {
    commentaryPanel.commentaryScrollIndex.value = null
    commentaryPanel.commentaryScrollOffset.value = null
    // Re-clicking the already-selected line changes no reactive state, so no
    // commentary reload fires and setupGroupReloadScroll never wakes — jump to
    // the pinned group explicitly (e.g. the session-restored line: restore put
    // the panel at the saved position; a deliberate click should still show the
    // default commentator).
    const isSameLineReclick = !isShiftClick && commentaryLineId.value === lineId
    onLineSelectedRaw(lineId, isShiftClick)
    if (isSameLineReclick) {
      const pinned = pinnedCommentaryGroupForDisplay.value
      if (pinned) {
        void nextTick(() =>
          commentaryViewRef()?.scrollToGroup(pinned.bookId),
        )
      }
    }
  }

  // ── Commentary data ───────────────────────────────────────────────────────

  const { groups, groupsForDisplay, filterGroups, staticFilterGroups, loading: commentaryLoading, staticFilterGroupsLoaded, ensureStaticFilterGroupsLoaded, requestContentPriority } = useCommentary(
    () => commentaryLineId.value,
    () => selectedSectionLineIds.value,
    () => bookId ?? undefined,
    () => false, // commentaryTreeVisible injected post-setup via sidePanel
    () => pinnedCommentaryGroupForDisplay.value?.bookId ?? null,
  )

  // ── TOC ───────────────────────────────────────────────────────────────────

  const { beginTocScroll, checkTocScrollProgress } = useBookViewTocScrollTracking()

  const { pinnedCommentaryGroup, restorePin, setPendingPin } = usePinnedCommentary(
    bookId, () => commentaryLineId.value, () => groups.value,
  )

  // Wire the deferred pin callbacks now that pinnedCommentary is available.
  pendingPinFns.setPendingPin = setPendingPin
  pendingPinFns.getActivePinnedGroup = () => commentaryViewRef()?.activePinnedGroup ?? null

  watch(pinnedCommentaryGroup, (group) => { pinnedCommentaryGroupForDisplay.value = group }, { immediate: true })

  const { currentScrollLineIndex, currentFullLineIndex, onLinesScrolled, syncTocPathForLineIndex } = useBookViewScrollSync(
    () => lines.value,
    activeTocEntryId,
    selectedLineId,
    commentaryLineId,
    checkTocScrollProgress,
    getActiveTocEntry,
    getTocPath,
    setPendingPin,
    () => commentaryViewRef()?.activePinnedGroup ?? null,
  )

  // ── Commentary annotation & rendering (hoisted — survive v-if toggle) ────

  const {
    getHighlightsForLine, applyHighlight, clearHighlight,
    getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
    commentaryFontPx, renderContent, setCurrentMark,
    commentaryTocPaths, buildExportHtml,
  } = useBookViewCommentaryAnnotations(
    () => groupsForDisplay.value,
    () => selectedSectionLineIds.value,
    () => lines.value,
    bookTitle,
    settingsStore,
    tabId,
    bookId ?? undefined,
  )

  // ── Search panel ──────────────────────────────────────────────────────────

  const contentSearch = useBookViewSearch(() => lines.value, () => currentFullLineIndex.value)
  const commentarySearch = useCommentarySearch(
    () => groups.value,
    () => commentaryViewRef()?.topVisibleFlatIndex ?? 0,
  )

  const searchPanel = useBookViewSearchPanel(
    contentSearch, commentarySearch,
    linesContentRef, commentaryViewRef, searchBarRef,
    () => {
      paneNavigation.updateActiveTab({
        searchHighlightLineIndex: undefined,
        searchHighlightQuery: undefined,
        searchHighlightSnippet: undefined,
        searchHighlightTerms: undefined,
      })
    },
  )

  // ── Side panel + commentary panel ─────────────────────────────────────────
  // commentaryPanel is instantiated first because sidePanel takes commentaryVisible
  // as a parameter. The closeSidePanel callback is deferred via a wrapper object.

  const sidePanelCloseFn = { close: () => {} }

  const commentaryPanel = useBookViewCommentaryPanel(
    commentaryViewRef,
    groups,
    commentaryLoading,
    pinnedCommentaryGroup,
    selectedLineId,
    commentaryLineId,
    () => lines.value,
    hasCommentaries,
    { value: null } as import('vue').Ref<string | null>,
    () => sidePanelCloseFn.close(),
    ensureStaticFilterGroupsLoaded,
  )

  const sidePanel = useBookViewSidePanel(
    toolbarRef,
    commentaryViewRef,
    commentaryPanel.commentaryVisible,
    loadAltTocSections,
    ensureStaticFilterGroupsLoaded,
  )

  sidePanelCloseFn.close = sidePanel.closeSidePanel

  // ── TOC navigation + keyboard shortcuts ──────────────────────────────────

  const {
    onTocSelect, onAltTocSelect, navigateToAdjacentTocSection, altTocLabelMap,
  } = useBookViewTocNavigation(
    () => tocEntries.value,
    activeTocEntryId,
    linesContentRef,
    getActiveTocEntry,
    getTocPath,
    beginTocScroll,
    selectedAltTocSection,
    openTocEntryId,
  )

  useBookViewKeyboardShortcuts(
    linesContentRef, commentaryViewRef,
    () => hasToc.value,
    navigateToAdjacentTocSection,
  )

  // ── Commentary navigation ─────────────────────────────────────────────────

  const { onNavigateSection: navigateSection } = useCommentaryNavigation(
    bookId, selectedLineId, commentaryLineId, commentaryPanel.commentaryVisible,
    () => lines.value, () => tocEntries.value, linesContentRef,
    () => manualSelectionLineIds.value,
    clearManualSelection,
  )

  function onNavigateSection(direction: 'next' | 'prev', commentaryBookId: number) {
    const group = groups.value.find((group) => group.bookId === commentaryBookId)
    setPendingPin(group
      ? { bookId: commentaryBookId, sectionLabel: group.sectionLabel ?? '', subSectionLabel: group.subSectionLabel ?? '' }
      : { bookId: commentaryBookId, sectionLabel: '', subSectionLabel: '' })
    return navigateSection(direction, commentaryBookId)
  }

  function openBookInTab(targetBookId: number, lineIndex: number | undefined) {
    paneNavigation.openTab({
      title: groups.value.find((group) => group.bookId === targetBookId)?.bookTitle ?? '',
      route: '/book-view',
      bookId: targetBookId,
      openTocLineIndex: lineIndex,
    })
  }

  // ── Session restore ───────────────────────────────────────────────────────

  const {
    initialLineIndex, initialScrollTop, initialScrollOffset,
    scrollStateReady, idbResolved, restore: restoreSession,
  } = useBookViewSessionRestore(
    tabId, bookId, openTocLineIndex,
    commentaryPanel.commentaryVisible, selectedLineId, commentaryLineId,
    commentaryTreeState,
    (index, offset) => {
      // A user line-click before the IDB read resolves sets commentaryLineId and
      // invalidates any saved position — don't overwrite that with stale values.
      if (commentaryLineId.value != null) return
      commentaryPanel.commentaryScrollIndex.value = index
      commentaryPanel.commentaryScrollOffset.value = offset
    },
  )

  watch(() => bookId, () => {
    selectedLineId.value = null
    commentaryLineId.value = null
    clearManualSelection()
    groups.value = []
  })

  watch(
    () => idbResolved.value && initialScrollTop.value != null,
    (ready) => { if (ready) prefetch(initialScrollTop.value!) },
    { immediate: true },
  )

  onMounted(async () => {
    groups.value = []
    const result = await restoreSession()
    if (result?.commentaryMode) restoredCommentaryMode.value = result.commentaryMode
    if (result?.commentaryFraction != null) restoredCommentaryFraction.value = result.commentaryFraction
    if (result?.stackedCommentaryFraction != null) restoredStackedCommentaryFraction.value = result.stackedCommentaryFraction
    if (result?.pinnedCommentaryGroup != null) restorePin(result.pinnedCommentaryGroup)

    // After session restore, the virtualizer has scrolled to the saved line index
    // but onLinesScrolled has not fired yet, so tocPath is empty and the breadcrumb
    // shows nothing. Sync it now using the restored scroll index.
    // If TOC entries are already loaded (common case), this is synchronous.
    // If not, watch for them to arrive (they load in parallel with session restore).
    const restoredLineIndex = initialScrollTop.value
    if (restoredLineIndex != null) {
      if (tocEntries.value.length > 0) {
        syncTocPathForLineIndex(restoredLineIndex)
      } else {
        const stopWatch = watch(
          () => tocEntries.value.length,
          (count) => {
            if (count === 0) return
            stopWatch()
            syncTocPathForLineIndex(restoredLineIndex)
          },
        )
      }
    }
  })

  // Register the TOC bridge synchronously at setup so the title bar breadcrumb
  // can read tocEntries immediately when the tab becomes active, without waiting
  // for onMounted (which runs after an async restoreSession await).
  bookViewStore.registerTocBridge(tabId, {
    get tocEntries() { return tocEntries.value },
    navigateToEntry: (entry) => onTocSelect(entry),
  })

  onBeforeUnmount(() => {
    paneNavigation.updateActiveTab({ tocPath: undefined })
    bookViewStore.unregisterTocBridge(tabId)
  })

  watch(searchPanel.searchVisible, (visible) => {
    if (!visible) { contentSearch.clear(); commentarySearch.clear() }
  })

  // ── Public API ────────────────────────────────────────────────────────────

  return {
    // store state
    toolbarPosition,
    toolbarVisible: computed(() => bookViewStore.getToolbarVisible(paneId)),
    // tab data
    searchHighlightLineIndex, searchHighlightQuery, searchHighlightSnippet, searchHighlightTerms,
    // book metadata
    bookHasTeamim,
    // UI state
    commentaryTreeState,
    selectedLineId,
    searchMode: searchPanel.searchMode,
    activeTocEntryId,
    tocVisible: sidePanel.tocVisible,
    commentaryTreeVisible: sidePanel.commentaryTreeVisible,
    sidePanelVisible: sidePanel.sidePanelVisible,
    sidePanelMode: sidePanel.sidePanelMode,
    sidePanelToggleButtonEl: sidePanel.sidePanelToggleButtonEl,
    commentaryVisible: commentaryPanel.commentaryVisible,
    searchVisible: searchPanel.searchVisible,
    commentaryScrollIndex: commentaryPanel.commentaryScrollIndex,
    commentaryScrollOffset: commentaryPanel.commentaryScrollOffset,
    // data
    bookId,
    lines, prioritise, hasCommentaries, hasRelatedBooks, hasToc,
    groups, groupsForDisplay, filterGroups, staticFilterGroups, commentaryLoading, requestContentPriority,
    tocEntries, tocSearchTree, altTocSections, selectedAltTocSection, tocLoading, tocError,
    altTocLabelMap, pinnedCommentaryGroup, selectedSectionLineIds, manualSelectionLineIds,
    // commentary annotation & render (hoisted — survive v-if toggle)
    getHighlightsForLine, applyHighlight, clearHighlight,
    getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
    commentaryFontPx, renderContent, setCurrentMark, commentaryTocPaths,
    // export
    buildExportHtml, bookTitle,
    // scroll / search state
    currentScrollLineIndex,
    scrollStateReady, idbResolved, initialLineIndex, initialScrollTop, initialScrollOffset,
    flashOpenLine,
    restoredCommentaryMode, restoredCommentaryFraction, restoredStackedCommentaryFraction,
    activeMatchCount: searchPanel.activeMatchCount,
    activeMatchIdx: searchPanel.activeMatchIdx,
    contentSearch, commentarySearch,
    // handlers
    onLinesScrolled, onTocSelect, onAltTocSelect,
    onLineSelected, onNavigateSection, navigateToAdjacentTocSection,
    onCommentaryScroll: commentaryPanel.onCommentaryScroll,
    onCommentaryTreeChanged: commentaryPanel.onCommentaryTreeChanged,
    openBookInTab,
    openContentSearch: searchPanel.openContentSearch,
    openCommentarySearch: searchPanel.openCommentarySearch,
    toggleSearch: searchPanel.toggleSearch,
    onQueryChange: searchPanel.onQueryChange,
    onSearchNext: searchPanel.onSearchNext,
    onSearchPrev: searchPanel.onSearchPrev,
    onModeChange: searchPanel.onModeChange,
    toggleTocPanel: sidePanel.toggleTocPanel,
    toggleCommentaryTreePanel: sidePanel.toggleCommentaryTreePanel,
    closeSidePanel: sidePanel.closeSidePanel,
    ensureStaticFilterGroupsLoaded, staticFilterGroupsLoaded,
    onCommentaryPanelMounted: commentaryPanel.onCommentaryPanelMounted,
    getActiveTocEntry, getTocPath,
  }
}
