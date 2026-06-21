/**
 * Central composable for the book view page.
 * Owns all data loading, state, event handlers, and watchers.
 * BookViewPage.vue is a shell that calls this and passes results to the template.
 */
import { ref, reactive, computed, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { useEventListener } from '@vueuse/core'
import {
  ZOOM_CONFIG,
  calculateZoom,
  zoomIn as zoomInUtil,
  zoomOut as zoomOutUtil,
  resetZoom as resetZoomUtil,
} from '@/composables/useZoom'
import { useToc } from './toc/useBookViewToc'
import { useLines } from './lines/useBookViewLinesTable'
import { useCommentary } from './commentary/useCommentary'
import { useBookViewSearch } from './useBookViewSearch'
import { useCommentarySearch } from './commentary/useCommentarySearch'
import { useBookViewTocScrollTracking } from './toc/useBookViewTocScrollTracking'
import { usePinnedCommentary } from './useBookViewPinnedCommentary'
import { useCommentaryNavigation } from './commentary/useCommentaryNavigation'
import { useCommentaryHighlights } from './commentary/useCommentaryHighlights'
import { useCommentaryNotes } from './commentary/useCommentaryNotes'
import { useCommentaryRender } from './commentary/useCommentaryRender'
import { useCommentaryTocPaths } from './commentary/useCommentaryTocPaths'
import { useBookViewScrollSync } from './useBookViewScrollSync'
import { useBookViewSessionRestore } from './useBookViewSessionRestore'
import { useBookViewLineRenderer } from './lines/useBookViewLineRenderer'
import { buildBookExportHtml } from './lines/useBookViewLineCopyMenu'
import { useSettingsStore } from '@/stores/settingsStore'
import type { TocEntry } from './toc/useBookViewToc'
import type { SearchMode, SidePanelMode, CommentaryTreeState } from './bookViewTypes'
export type { SearchMode } from './bookViewTypes'

// Component instance types — used only for ref typing
type ToolbarInstance = { tocBtnRef: HTMLElement | null }
type LinesContentInstance = {
  scrollToLineId: (lineId: number, lineIndex?: number) => void
  scrollToLineIndex: (lineIndex: number, occurrence?: number) => void
  focusScroller: () => void
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
  const { zoom, isBookViewActive, toolbarPosition } = storeToRefs(bookViewStore)

  // ── Keyboard zoom interceptor ─────────────────────────────────────────────
  // Ctrl+±/0 on the keyboard must be intercepted at the window level to prevent
  // the browser from applying its own page zoom. We route to the correct panel
  // based on which scroller contains the currently focused element.
  // Trackpad (wheel) and pinch are handled per-scroller inside each component.
  useEventListener(window, 'keydown', (event: KeyboardEvent) => {
    if (!isBookViewActive.value) return
    const ctrl = event.ctrlKey || event.metaKey
    if (!ctrl) return
    const isZoomIn = event.code === 'Equal' || event.code === 'NumpadAdd'
    const isZoomOut = event.code === 'Minus' || event.code === 'NumpadSubtract'
    const isReset = event.code === 'Digit0' || event.code === 'Numpad0'
    if (!isZoomIn && !isZoomOut && !isReset) return

    event.preventDefault()

    const focused = document.activeElement
    const linesEl = linesContentRef()
    const commentaryEl = commentaryViewRef()

    // Determine which scroller contains focus. We check by walking up from the
    // focused element and seeing if it lives inside the lines or commentary DOM.
    // Fall back to zooming both if focus is elsewhere (e.g. toolbar button).
    const linesRoot = (linesEl as unknown as { $el?: HTMLElement } | null)?.$el
    const commentaryRoot = (commentaryEl as unknown as { $el?: HTMLElement } | null)?.$el

    const focusInLines = linesRoot != null && focused != null && linesRoot.contains(focused)
    const focusInCommentary = commentaryRoot != null && focused != null && commentaryRoot.contains(focused)

    const tab = tabStore.activeTab
    if (tab.route !== '/book-view' || tab.bookId == null) return
    const tabId = tab.id
    const bookId = tab.bookId

    function applyToLines() {
      const current = bookViewStore.getLinesZoom(tabId, bookId)
      bookViewStore.setLinesZoom(tabId, bookId,
        isZoomIn ? zoomInUtil(current) : isZoomOut ? zoomOutUtil(current) : resetZoomUtil())
    }
    function applyToCommentary() {
      const current = bookViewStore.getCommentaryZoom(tabId, bookId)
      bookViewStore.setCommentaryZoom(tabId, bookId,
        isZoomIn ? zoomInUtil(current) : isZoomOut ? zoomOutUtil(current) : resetZoomUtil())
    }

    if (focusInLines) {
      applyToLines()
    } else if (focusInCommentary) {
      applyToCommentary()
    } else {
      // Focus is on toolbar or elsewhere — zoom both panels together
      applyToLines()
      applyToCommentary()
    }
  })

  // ── Tab state captured at mount (stable for component lifetime) ───────────

  const tabId = tabStore.activeTabId
  const bookId = tabStore.activeTab.bookId
  const bookTitle = tabStore.activeTab.title

  const openTocEntryId = tabStore.activeTab.openTocEntryId
  const openTocLineIndex = tabStore.activeTab.openTocLineIndex
  const searchHighlightLineIndex = tabStore.activeTab.searchHighlightLineIndex
  const searchHighlightQuery = tabStore.activeTab.searchHighlightQuery ?? ''
  const searchHighlightSnippet = tabStore.activeTab.searchHighlightSnippet
  const searchHighlightTerms = tabStore.activeTab.searchHighlightTerms

  if (openTocEntryId != null)
    tabStore.updateActiveTab({
      openTocEntryId: undefined,
      openTocLineIndex: undefined,
      searchHighlightLineIndex: undefined,
      searchHighlightQuery: undefined,
      searchHighlightSnippet: undefined,
      searchHighlightTerms: undefined,
    })

  // ── UI state ──────────────────────────────────────────────────────────────

  const commentaryVisible = ref(false)
  const searchVisible = ref(false)
  const restoredCommentaryMode = ref<'off' | 'bottom' | 'side' | undefined>(undefined)
  const restoredCommentaryFraction = ref<number | undefined>(undefined)
  const restoredStackedCommentaryFraction = ref<number | undefined>(undefined)
  const sidePanelMode = ref<SidePanelMode | null>(null)
  const selectedLineId = ref<number | null>(null)
  const commentaryLineId = ref<number | null>(null)
  const commentaryTreeState = reactive<CommentaryTreeState>({ searchQuery: '', tokens: [], visibilityList: [] })
  const searchMode = ref<SearchMode>('content')
  const activeTocEntryId = ref<number | undefined>(undefined)
  const commentaryScrollIndex = ref<number | null>(null)
  const commentaryScrollOffset = ref<number | null>(null)
  let lastRestoredCommentaryKey: string | null = null

  const tocVisible = computed(() => sidePanelMode.value === 'toc')
  const commentaryTreeVisible = computed(() => sidePanelMode.value === 'commentary-tree')
  const sidePanelVisible = computed(() => sidePanelMode.value !== null)
  const sidePanelToggleButtonEl = computed(() =>
    sidePanelMode.value === 'commentary-tree'
      ? commentaryViewRef()?.getFilterButtonEl?.() ?? null
      : toolbarRef()?.tocBtnRef ?? null,
  )

  // ── Data loading ──────────────────────────────────────────────────────────

  const {
    getActiveTocEntry, getTocPath,
    altTocSections, selectedAltTocSection,
    tocEntries, tocSearchTree,
    loading: tocLoading, error: tocError, tocLoaded,
    loadAltTocSections,
  } = useToc(() => bookId, () => bookTitle)

  // Lines load immediately in parallel with TOC — scrollStateReady is always true,
  // BookViewLinesContent mounts immediately and its scroll watcher handles late IDB restore.
  const { lines, prioritise, hasCommentaries, hasRelatedBooks, hasTeamim: bookHasTeamim } = useLines(() => bookId)

  const hasToc = computed(() => tocLoaded.value && tocEntries.value.length > 0)

  const selectedSectionLineIds = computed<number[] | null>(() => {
    if (commentaryLineId.value == null || !tocEntries.value.length || !lines.value.length) return null
    const tocEntry = tocEntries.value.find((e) => e.lineId === commentaryLineId.value)
    if (!tocEntry || tocEntry.lineIndex == null) return null
    const idx = tocEntries.value.indexOf(tocEntry)
    const nextEntry = tocEntries.value.slice(idx + 1).find((e) => e.lineIndex != null && e.level <= tocEntry.level)
    const fromIndex = tocEntry.lineIndex
    const toIndex = nextEntry?.lineIndex ?? lines.value.length
    // Exclude placeholder lines (content === null) — they haven't loaded from DB yet.
    // Return null instead of a partial list so useCommentary waits for real IDs.
    const ids = lines.value
      .filter((l) => l.lineIndex >= fromIndex && l.lineIndex < toIndex && l.content !== null)
      .map((l) => l.id)
    return ids.length > 0 ? ids : null
  })

  const pinnedCommentaryGroupForDisplay = ref<import('./bookViewTypes').PinnedCommentaryGroup | null>(null)

  const { groups, groupsForDisplay, filterGroups, staticFilterGroups, loading: commentaryLoading, staticFilterGroupsLoaded, ensureStaticFilterGroupsLoaded } = useCommentary(
    () => commentaryLineId.value,
    () => selectedSectionLineIds.value,
    () => bookId ?? undefined,
    () => commentaryTreeVisible.value,
    () => pinnedCommentaryGroupForDisplay.value?.bookId ?? null,
  )

  // ── TOC ───────────────────────────────────────────────────────────────────

  const altTocLabelMap = computed(() => {
    const map = new Map<number, string>()
    const section = selectedAltTocSection.value
    if (!section) return map
    for (const entry of section.entries) {
      if (entry.lineIndex == null) continue
      map.set(entry.lineIndex, entry.text)
    }
    return map
  })

  if (openTocEntryId != null) {
    const stop = watch(tocEntries, (entries) => {
      if (!entries.length) return
      const entry = entries.find((e) => e.id === openTocEntryId)
      if (entry != null) {
        activeTocEntryId.value = entry.id
        tabStore.updateActiveTab({ tocPath: getTocPath(entry) })
      }
      stop()
    })
  }

  const { beginTocScroll, checkTocScrollProgress } = useBookViewTocScrollTracking()

  const { pinnedCommentaryGroup, restorePin, pinExplicitly, setPendingPin } = usePinnedCommentary(
    bookId, () => commentaryLineId.value, () => groups.value,
  )

  // Keep the display ref in sync so useCommentary can inject the placeholder
  watch(pinnedCommentaryGroup, (g) => { pinnedCommentaryGroupForDisplay.value = g }, { immediate: true })

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

  function onTocSelect(entry: TocEntry) {
    if (entry.lineId == null) return
    activeTocEntryId.value = entry.id
    tabStore.updateActiveTab({ tocPath: getTocPath(entry) })
    beginTocScroll(entry)
    linesContentRef()?.scrollToLineId(entry.lineId, entry.lineIndex ?? undefined)
  }

  function onAltTocSelect(entry: TocEntry) {
    if (entry.lineId == null) return
    linesContentRef()?.scrollToLineId(entry.lineId)
    if (entry.lineIndex != null) {
      const mainEntry = getActiveTocEntry(entry.lineIndex)
      if (mainEntry) {
        activeTocEntryId.value = mainEntry.id
        tabStore.updateActiveTab({ tocPath: getTocPath(mainEntry) })
      }
    }
  }

  // ── Commentary annotation & rendering composables (hoisted above CommentaryView lifecycle) ──
  // These are driven by groups/settings — not by the component being mounted.
  // Hoisting them here means they survive the v-if toggle on CommentaryView and
  // don't re-initialize (with their immediate/sync watchers) on every open.

  const { getHighlightsForLine, applyHighlight, clearHighlight } = useCommentaryHighlights(
    () => groupsForDisplay.value,
  )
  const { getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote } =
    useCommentaryNotes(() => groupsForDisplay.value)
  const { commentaryFontPx, renderContent, setCurrentMark } = useCommentaryRender(
    () => groupsForDisplay.value,
    getHighlightsForLine,
    getNotesForLine,
  )
  const { commentaryTocPaths } = useCommentaryTocPaths(() => groupsForDisplay.value)

  // ── Book line renderer (for export) ──────────────────────────────────────
  // A dedicated renderer instance used only for export — keeps caching separate
  // from the virtual scroller renderer in BookViewLinesContent.

  const diacriticsStateForExport = computed(() => settingsStore.diacriticsState)
  const { lineContent: renderLineForExport } = useBookViewLineRenderer(
    settingsStore,
    diacriticsStateForExport,
    () => ({
      getHighlightsForLine: undefined,
      getNotesForLine: (lineId: number) =>
        getNotesForLine(lineId), // reuse hoisted notes map
    }),
  )

  function buildExportHtml(): string {
    return buildBookExportHtml(
      lines.value,
      bookTitle ?? '',
      renderLineForExport,
      getNotesForLine,
    )
  }

  // ── Search ────────────────────────────────────────────────────────────────

  const contentSearch = useBookViewSearch(() => lines.value, () => currentFullLineIndex.value)
  const commentarySearch = useCommentarySearch(
    () => groups.value,
    () => commentaryViewRef()?.topVisibleFlatIndex ?? 0,
  )

  const activeSearch = computed(() => searchMode.value === 'content' ? contentSearch : commentarySearch)
  const activeMatchCount = computed(() => activeSearch.value.matchCount.value)
  const activeMatchIdx = computed(() => activeSearch.value.currentMatchIdx.value)

  const searchNavigationState = {
    content: false,
    commentary: false,
  }

  function scrollContentMatch() {
    if (searchMode.value === 'content') {
      if (contentSearch.currentMatchLineIndex.value === -1) return
      linesContentRef()?.scrollToLineIndex(
        contentSearch.currentMatchLineIndex.value,
        contentSearch.currentMatchOccurrence.value,
      )
    } else {
      if (commentarySearch.currentMatchFlatIndex.value === -1) return
      commentaryViewRef()?.scrollToFlatIndex(
        commentarySearch.currentMatchFlatIndex.value,
        commentarySearch.currentMatchOccurrence.value,
      )
    }
  }

  function openContentSearch() {
    if (searchVisible.value && searchMode.value === 'content') {
      searchVisible.value = false
      nextTick(() => linesContentRef()?.focusScroller())
      return
    }
    searchVisible.value = true
    searchMode.value = 'content'
    searchNavigationState.content = false
    nextTick(() => searchBarRef()?.focus())
  }

  function openCommentarySearch() {
    if (searchVisible.value && searchMode.value === 'commentary') {
      searchVisible.value = false
      return
    }
    searchVisible.value = true
    searchMode.value = 'commentary'
    searchNavigationState.commentary = false
    nextTick(() => searchBarRef()?.focus())
  }

  function onModeChange(mode: SearchMode) {
    const currentQuery = activeSearch.value.query.value
    contentSearch.clear()
    commentarySearch.clear()
    searchMode.value = mode
    searchNavigationState[mode] = false
    if (!currentQuery) return
    const target = mode === 'content' ? contentSearch : commentarySearch
    target.query.value = currentQuery
  }

  function onQueryChange(q: string) {
    activeSearch.value.query.value = q
    searchNavigationState[searchMode.value] = false
  }

  function onSearchNext() {
    const search = activeSearch.value
    if (search.matchCount.value === 0) return
    if (!searchNavigationState[searchMode.value]) {
      searchNavigationState[searchMode.value] = true
      search.gotoNearestMatch?.()
      scrollContentMatch()
      return
    }
    search.next()
    scrollContentMatch()
  }

  function onSearchPrev() {
    const search = activeSearch.value
    if (search.matchCount.value === 0) return
    if (!searchNavigationState[searchMode.value]) {
      searchNavigationState[searchMode.value] = true
      search.gotoNearestMatch?.()
      scrollContentMatch()
      return
    }
    search.prev()
    scrollContentMatch()
  }

  // ── Side panel ────────────────────────────────────────────────────────────

  function toggleTocPanel() {
    sidePanelMode.value = sidePanelMode.value === 'toc' ? null : 'toc'
    if (sidePanelMode.value === 'toc') loadAltTocSections()
  }

  function toggleCommentaryTreePanel() {
    if (!commentaryVisible.value) return
    sidePanelMode.value = sidePanelMode.value === 'commentary-tree' ? null : 'commentary-tree'
    if (sidePanelMode.value === 'commentary-tree') ensureStaticFilterGroupsLoaded()
  }

  function closeSidePanel() { sidePanelMode.value = null }

  // ── Commentary ────────────────────────────────────────────────────────────

  // Preserve commentary scroll position when visibility changes (items toggled or search changes)
  async function onCommentaryTreeChanged() {
    const savedPos = commentaryViewRef()?.captureScrollPos?.()
    await nextTick()
    if (savedPos)
      commentaryViewRef()?.restoreCommentaryScrollPos(savedPos.scrollIndex, savedPos.scrollOffset)
  }

  function openBookInTab(targetBookId: number, lineIndex: number | undefined) {
    tabStore.openTab({
      title: groups.value.find((g) => g.bookId === targetBookId)?.bookTitle ?? '',
      route: '/book-view',
      bookId: targetBookId,
      openTocLineIndex: lineIndex,
    })
  }

  function onLineSelected(lineId: number) {
    // Capture synchronously before any reactive state changes — activePinnedGroup
    // is still valid here (groups haven't been cleared yet).
    setPendingPin(commentaryViewRef()?.activePinnedGroup ?? null)
    selectedLineId.value = lineId
    commentaryLineId.value = lineId
  }

  function onCommentaryScroll(si: number, so: number) {
    commentaryScrollIndex.value = si
    commentaryScrollOffset.value = so
  }

  const { onNavigateSection: navigateSection } = useCommentaryNavigation(
    bookId, selectedLineId, commentaryLineId, commentaryVisible,
    () => lines.value, () => tocEntries.value, linesContentRef,
  )

  function onNavigateSection(direction: 'next' | 'prev', commentaryBookId: number) {
    const group = groups.value.find((g) => g.bookId === commentaryBookId)
    setPendingPin(group
      ? { bookId: commentaryBookId, sectionLabel: group.sectionLabel ?? '', subSectionLabel: group.subSectionLabel ?? '' }
      : { bookId: commentaryBookId, sectionLabel: '', subSectionLabel: '' })
    return navigateSection(direction, commentaryBookId)
  }

  // ── Session restore ───────────────────────────────────────────────────────

  const {
    initialLineIndex, initialScrollTop, initialScrollOffset,
    scrollStateReady, idbResolved, restore: restoreSession,
  } = useBookViewSessionRestore(
    tabId, bookId, openTocLineIndex,
    commentaryVisible, selectedLineId, commentaryLineId,
    commentaryTreeState, commentaryLoading, commentaryViewRef,
    () => groups.value,
  )

  // Reset selectedLineId and commentaryLineId when the book changes so old commentary
  // doesn't load while waiting for the new book's lines to finish loading.
  watch(() => bookId, () => {
    selectedLineId.value = null
    commentaryLineId.value = null
    // Also clear groups directly to ensure loading animation shows immediately
    groups.value = []
  })

  onMounted(async () => {
    // Clear groups before session restore so loading animation shows immediately
    groups.value = []
    // Warm up commentary metadata in the background so the first toggle is instant.
    void ensureStaticFilterGroupsLoaded()
    const result = await restoreSession()
    if (result?.commentaryMode) restoredCommentaryMode.value = result.commentaryMode
    if (result?.commentaryFraction != null) restoredCommentaryFraction.value = result.commentaryFraction
    if (result?.stackedCommentaryFraction != null) restoredStackedCommentaryFraction.value = result.stackedCommentaryFraction
    if (result?.pinnedCommentaryGroup != null) {
      restorePin(result.pinnedCommentaryGroup)
    }
  })
  onBeforeUnmount(() => tabStore.updateActiveTab({ tocPath: undefined }))

  // ── Watchers ──────────────────────────────────────────────────────────────

  // Runs all open-side effects when the commentary panel becomes active —
  // either by toggling visible, or by switching layout mode (bottom ↔ side).
  // In both cases CommentaryView is freshly mounted and needs commentaryLineId
  // initialised and scroll position restored.
  function onCommentaryPanelMounted() {
    if (!commentaryVisible.value) return
    void ensureStaticFilterGroupsLoaded()
    // Sync commentaryLineId from selectedLineId when the commentary panel first opens
    // after session restore (commentaryVisible=true but commentaryLineId still null).
    if (selectedLineId.value != null && commentaryLineId.value == null) {
      let stop: (() => void) | undefined
      stop = watch(
        () => lines.value.some((l) => l.content !== null),
        (hasContent) => {
          if (!hasContent) return
          stop?.()
          if (commentaryVisible.value && selectedLineId.value != null && commentaryLineId.value == null)
            commentaryLineId.value = selectedLineId.value
        },
        { immediate: true },
      )
    }
    // Scroll to pinned group when groups are already loaded (mode switch with live data)
    // or restore the saved scroll position.
    if (commentaryScrollIndex.value != null && commentaryScrollOffset.value != null) {
      const si = commentaryScrollIndex.value
      const so = commentaryScrollOffset.value
      const restoreKey = `${si}:${so}`
      if (restoreKey === lastRestoredCommentaryKey) {
        // No position to restore — but still scroll to pinned group if groups are loaded
        if (groups.value.length > 0 && pinnedCommentaryGroup.value) {
          nextTick(() => {
            const pinned = pinnedCommentaryGroup.value
            if (pinned) commentaryViewRef()?.scrollToGroup(pinned.bookId)
          })
        }
        return
      }

      let stopLoading: (() => void) | undefined
      let stopViewRef: (() => void) | undefined
      const cancelRestore = () => { stopLoading?.(); stopViewRef?.() }
      const stopVisibleGuard = watch(commentaryVisible, (v) => {
        if (!v) { cancelRestore(); lastRestoredCommentaryKey = null; stopVisibleGuard() }
      })
      stopLoading = watch(
        () => !commentaryLoading.value && groups.value.length > 0,
        (ready) => {
          if (!ready) return
          stopLoading?.()
          const viewRef = commentaryViewRef()
          if (viewRef) {
            nextTick(async () => {
              await viewRef.restoreCommentaryScrollPos(si, so)
              lastRestoredCommentaryKey = restoreKey
            })
          } else {
            stopViewRef = watch(
              () => commentaryViewRef(),
              (newRef) => {
                if (!newRef) return
                stopViewRef?.()
                nextTick(async () => {
                  await newRef.restoreCommentaryScrollPos(si, so)
                  lastRestoredCommentaryKey = restoreKey
                })
              },
            )
          }
        },
        { flush: 'post', immediate: true },
      )
    } else if (groups.value.length > 0 && pinnedCommentaryGroup.value) {
      // No saved scroll position — scroll to pinned group (e.g. mode switch)
      nextTick(() => {
        const pinned = pinnedCommentaryGroup.value
        if (pinned) commentaryViewRef()?.scrollToGroup(pinned.bookId)
      })
    }
  }

  // flush: 'post' — runs after Vue has flushed the DOM so the commentary panel is
  // painted before any reactive side-effects (metadata load, commentaryLineId set,
  // scroll restore) begin. Without this the default 'pre' flush meant everything
  // ran before the SplitPane bottom slot appeared, causing a visible hang.
  watch(commentaryVisible, (visible) => {
    if (!visible && sidePanelMode.value === 'commentary-tree') closeSidePanel()
    if (!visible) {
      lastRestoredCommentaryKey = null
      return
    }
    setTimeout(() => onCommentaryPanelMounted(), 0)
  }, { flush: 'post' })
  watch(hasCommentaries, (has) => {
    if (!has) { commentaryVisible.value = false; if (sidePanelMode.value === 'commentary-tree') closeSidePanel() }
  })
  watch(searchVisible, (v) => { if (!v) { contentSearch.clear(); commentarySearch.clear() } })

  // ── Public API ────────────────────────────────────────────────────────────

  return {
    // store state needed by template
    toolbarPosition,
    toolbarVisible: computed(() => bookViewStore.toolbarVisible),
    // tab data
    searchHighlightLineIndex, searchHighlightQuery, searchHighlightSnippet, searchHighlightTerms,
    // book metadata
    bookHasTeamim,
    // UI state
    commentaryVisible, searchVisible, sidePanelMode,
    selectedLineId, commentaryTreeState, searchMode,
    activeTocEntryId, commentaryScrollIndex, commentaryScrollOffset,
    tocVisible, commentaryTreeVisible, sidePanelVisible, sidePanelToggleButtonEl,
    // data
    bookId,
    lines, prioritise, hasCommentaries, hasRelatedBooks, hasToc,
    groups, groupsForDisplay, filterGroups, staticFilterGroups, commentaryLoading,
    tocEntries, tocSearchTree, altTocSections, selectedAltTocSection, tocLoading, tocError,
    altTocLabelMap, pinnedCommentaryGroup, selectedSectionLineIds,
    // commentary annotation & render (hoisted — survive v-if toggle)
    getHighlightsForLine, applyHighlight, clearHighlight,
    getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
    commentaryFontPx, renderContent, setCurrentMark, commentaryTocPaths,
    // export
    buildExportHtml,
    // scroll / search state
    currentScrollLineIndex,
    scrollStateReady, idbResolved, initialLineIndex, initialScrollTop, initialScrollOffset,
    restoredCommentaryMode, restoredCommentaryFraction, restoredStackedCommentaryFraction,
    activeMatchCount, activeMatchIdx, contentSearch, commentarySearch,
    // handlers
    onLinesScrolled, onTocSelect, onAltTocSelect,
    onLineSelected, onNavigateSection, onCommentaryScroll,
    onCommentaryTreeChanged, openBookInTab,
    openContentSearch, openCommentarySearch,
    onQueryChange, onSearchNext, onSearchPrev, onModeChange,
    toggleTocPanel, toggleCommentaryTreePanel, closeSidePanel,
    ensureStaticFilterGroupsLoaded, staticFilterGroupsLoaded,
    onCommentaryPanelMounted,
    // toc lookup for copy-with-source
    getActiveTocEntry, getTocPath,
  }
}
