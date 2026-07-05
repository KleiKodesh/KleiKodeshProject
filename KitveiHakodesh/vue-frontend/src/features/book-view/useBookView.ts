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
import { ref, reactive, computed, watch, onMounted, onBeforeUnmount } from 'vue'
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
  const { toolbarPosition } = storeToRefs(bookViewStore)

  // ── Tab state captured at mount (stable for component lifetime) ──────────
  // Read from paneNavigation so pane 2 gets its own active tab, not pane 1's.

  const tabId = paneNavigation.activeTabId
  const bookId = paneNavigation.activeTab.bookId
  const bookTitle = paneNavigation.activeTab.title

  const openTocEntryId = paneNavigation.activeTab.openTocEntryId
  const openTocLineIndex = paneNavigation.activeTab.openTocLineIndex
  const searchHighlightLineIndex = paneNavigation.activeTab.searchHighlightLineIndex
  const searchHighlightQuery = paneNavigation.activeTab.searchHighlightQuery ?? ''
  const searchHighlightSnippet = paneNavigation.activeTab.searchHighlightSnippet
  const searchHighlightTerms = paneNavigation.activeTab.searchHighlightTerms

  if (openTocEntryId != null)
    paneNavigation.updateActiveTab({
      openTocEntryId: undefined,
      openTocLineIndex: undefined,
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
    selectedSectionLineIds, clearManualSelection, onLineSelected,
  } = useBookViewLineSelection(
    () => lines.value,
    () => tocEntries.value,
    commentaryLineId,
    selectedLineId,
    (group) => pendingPinFns.setPendingPin(group),
    () => pendingPinFns.getActivePinnedGroup(),
  )

  // ── Commentary data ───────────────────────────────────────────────────────

  const { groups, groupsForDisplay, filterGroups, staticFilterGroups, loading: commentaryLoading, staticFilterGroupsLoaded, ensureStaticFilterGroupsLoaded } = useCommentary(
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

  const { currentScrollLineIndex, currentFullLineIndex, onLinesScrolled } = useBookViewScrollSync(
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
    commentaryTreeState, commentaryLoading, commentaryViewRef,
    () => groups.value,
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
  })

  onBeforeUnmount(() => paneNavigation.updateActiveTab({ tocPath: undefined }))

  watch(searchPanel.searchVisible, (visible) => {
    if (!visible) { contentSearch.clear(); commentarySearch.clear() }
  })

  // ── Public API ────────────────────────────────────────────────────────────

  return {
    // store state
    toolbarPosition,
    toolbarVisible: computed(() => bookViewStore.toolbarVisible),
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
    groups, groupsForDisplay, filterGroups, staticFilterGroups, commentaryLoading,
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
