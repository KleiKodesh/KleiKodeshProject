/**
 * Central composable for the book view page.
 * Owns all data loading, state, event handlers, and watchers.
 * BookViewPage.vue is a shell that calls this and passes results to the template.
 */
import { ref, reactive, computed, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { useZoomHandler } from '@/composables/useZoom'
import { useToc } from './useBookViewToc'
import { useLines } from './useBookViewLinesTable'
import { useCommentary } from './useCommentary'
import { useBookViewSearch } from './useBookViewSearch'
import { useCommentarySearch } from './useCommentarySearch'
import { useBookViewTocScrollTracking } from './useBookViewTocScrollTracking'
import { usePinnedCommentary } from './useBookViewPinnedCommentary'
import { useCommentaryNavigation } from './useCommentaryNavigation'
import { useBookViewScrollSync } from './useBookViewScrollSync'
import { useBookViewSessionRestore } from './useBookViewSessionRestore'
import type { TocEntry } from './useBookViewToc'
import type { SearchMode, SidePanelMode, CommentaryTreeState } from './bookViewTypes'
export type { SearchMode } from './bookViewTypes'

// Component instance types — used only for ref typing
type ToolbarInstance = { tocBtnRef: HTMLElement | null }
type LinesContentInstance = {
  scrollToLineId: (lineId: number, lineIndex?: number) => void
  scrollToLineIndex: (lineIndex: number) => void
  focusScroller: () => void
}
type SearchBarInstance = { focus: () => void }
type CommentaryViewInstance = {
  topVisibleFlatIndex: number
  activeBookId: number | null
  getFilterButtonEl?: () => HTMLElement | null
  scrollToGroup: (bookId: number) => void
  scrollToFlatIndex: (index: number) => void
  captureScrollPos?: () => { scrollIndex: number; scrollOffset: number } | null
  restoreCommentaryScrollPos: (index: number, offset: number) => void
}

export function useBookView(
  toolbarRef: () => ToolbarInstance | null,
  linesContentRef: () => LinesContentInstance | null,
  searchBarRef: () => SearchBarInstance | null,
  commentaryViewRef: () => CommentaryViewInstance | null,
) {
  const bookViewStore = useBookViewStore()
  const tabStore = useTabStore()
  const { zoom, isBookViewActive, toolbarPosition } = storeToRefs(bookViewStore)

  useZoomHandler({ zoom, enabled: isBookViewActive })

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
  const sidePanelMode = ref<SidePanelMode | null>(null)
  const selectedLineId = ref<number | null>(null)
  const commentaryLineId = ref<number | null>(null)
  const commentaryTreeState = reactive<CommentaryTreeState>({ searchQuery: '', tokens: [], visibilityList: [] })
  const searchMode = ref<SearchMode>('content')
  const activeTocEntryId = ref<number | undefined>(undefined)
  const commentaryScrollIndex = ref<number | null>(null)
  const commentaryScrollOffset = ref<number | null>(null)

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
  const { lines, prioritise, hasCommentaries, hasRelatedBooks } = useLines(() => bookId)

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

  const pinnedCommentaryBookIdForDisplay = ref<number | null>(null)

  const { groups, groupsForDisplay, filterGroups, staticFilterGroups, loading: commentaryLoading, staticFilterGroupsLoaded, ensureStaticFilterGroupsLoaded } = useCommentary(
    () => commentaryLineId.value,
    () => selectedSectionLineIds.value,
    () => bookId ?? undefined,
    () => commentaryTreeVisible.value,
    () => pinnedCommentaryBookIdForDisplay.value,
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

  const { currentScrollLineIndex, onLinesScrolled } = useBookViewScrollSync(
    () => lines.value,
    activeTocEntryId,
    selectedLineId,
    commentaryLineId,
    checkTocScrollProgress,
    getActiveTocEntry,
    getTocPath,
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

  // ── Search ────────────────────────────────────────────────────────────────

  const contentSearch = useBookViewSearch(() => lines.value, () => currentScrollLineIndex.value)
  const commentarySearch = useCommentarySearch(
    () => groups.value,
    () => commentaryViewRef()?.topVisibleFlatIndex ?? 0,
  )

  const activeSearch = computed(() => searchMode.value === 'content' ? contentSearch : commentarySearch)
  const activeMatchCount = computed(() => activeSearch.value.matchCount.value)
  const activeMatchIdx = computed(() => activeSearch.value.currentMatchIdx.value)

  function scrollContentMatch() {
    if (searchMode.value === 'content') {
      if (contentSearch.currentMatchLineIndex.value === -1) return
      linesContentRef()?.scrollToLineIndex(contentSearch.currentMatchLineIndex.value)
    } else {
      if (commentarySearch.currentMatchFlatIndex.value === -1) return
      commentaryViewRef()?.scrollToFlatIndex(commentarySearch.currentMatchFlatIndex.value)
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
    nextTick(() => searchBarRef()?.focus())
  }

  function openCommentarySearch() {
    searchVisible.value = true
    searchMode.value = 'commentary'
    nextTick(() => searchBarRef()?.focus())
  }

  function onModeChange(mode: SearchMode) {
    const currentQuery = activeSearch.value.query.value
    contentSearch.clear()
    commentarySearch.clear()
    searchMode.value = mode
    if (!currentQuery) return
    const target = mode === 'content' ? contentSearch : commentarySearch
    target.query.value = currentQuery
    nextTick(() => scrollContentMatch())
  }

  function onQueryChange(q: string) { activeSearch.value.query.value = q; scrollContentMatch() }
  function onSearchNext() { activeSearch.value.next(); scrollContentMatch() }
  function onSearchPrev() { activeSearch.value.prev(); scrollContentMatch() }

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
    selectedLineId.value = lineId
    commentaryLineId.value = lineId
  }

  function onCommentaryScroll(si: number, so: number) {
    commentaryScrollIndex.value = si
    commentaryScrollOffset.value = so
  }

  const { onNavigateSection } = useCommentaryNavigation(
    bookId, selectedLineId, commentaryLineId, commentaryVisible, commentaryLoading,
    () => lines.value, () => tocEntries.value, linesContentRef, commentaryViewRef,
  )

  const { pinnedCommentaryBookId, restorePin } = usePinnedCommentary(
    bookId, () => commentaryLineId.value, () => groups.value, commentaryViewRef,
  )

  // Keep the display ref in sync so useCommentary can inject the placeholder
  watch(pinnedCommentaryBookId, (id) => { pinnedCommentaryBookIdForDisplay.value = id }, { immediate: true })

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

  onMounted(async () => {
    const result = await restoreSession()
    if (result?.commentaryMode) restoredCommentaryMode.value = result.commentaryMode
    if (result?.commentaryFraction != null) restoredCommentaryFraction.value = result.commentaryFraction
    if (result?.pinnedCommentaryBookId != null) {
      restorePin(result.pinnedCommentaryBookId)
    }
  })
  onBeforeUnmount(() => tabStore.updateActiveTab({ tocPath: undefined }))

  // ── Watchers ──────────────────────────────────────────────────────────────

  watch(() => bookViewStore.toggleBottomPanelSignal, () => { commentaryVisible.value = !commentaryVisible.value })
  watch(commentaryVisible, (visible) => {
    if (!visible && sidePanelMode.value === 'commentary-tree') closeSidePanel()
    // Pre-load static filter groups as soon as commentary is visible so the
    // placeholder insertion in groupsForDisplay has the correct order data.
    if (visible) void ensureStaticFilterGroupsLoaded()
    // Sync commentaryLineId from selectedLineId when the commentary panel first opens
    // after session restore (commentaryVisible=true but commentaryLineId still null).
    // Wait until the first chunk has real content so the commentary query doesn't
    // compete with line chunk fetches.
    if (visible && selectedLineId.value != null && commentaryLineId.value == null) {
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
    // Restore commentary scroll position when the panel is toggled back on.
    // CommentaryView is fully unmounted when commentaryVisible is false (v-if in SplitPane),
    // so the one-time restore() at mount is not enough — we must re-apply the saved
    // position every time the panel reopens.
    if (visible && commentaryScrollIndex.value != null && commentaryScrollOffset.value != null) {
      const si = commentaryScrollIndex.value
      const so = commentaryScrollOffset.value
      let stopLoading: (() => void) | undefined
      stopLoading = watch(
        // Wait for loading to finish AND real groups to arrive — the placeholder alone
        // (groups.length === 0, groupsForDisplay.length === 1) gives the virtualizer
        // wrong measurements and the scroll lands in the wrong place.
        () => !commentaryLoading.value && groups.value.length > 0,
        async (ready) => {
          if (!ready) return
          stopLoading?.()
          await nextTick()
          const viewRef = commentaryViewRef()
          if (viewRef) {
            viewRef.restoreCommentaryScrollPos(si, so)
          } else {
            const stopRef = watch(
              () => commentaryViewRef(),
              (newRef) => {
                if (!newRef) return
                stopRef()
                newRef.restoreCommentaryScrollPos(si, so)
              },
            )
          }
        },
        { flush: 'sync', immediate: true },
      )
    }
  })
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
    altTocLabelMap, pinnedCommentaryBookId,
    // scroll / search state
    currentScrollLineIndex,
    scrollStateReady, idbResolved, initialLineIndex, initialScrollTop, initialScrollOffset,
    restoredCommentaryMode, restoredCommentaryFraction,
    activeMatchCount, activeMatchIdx, contentSearch, commentarySearch,
    // handlers
    onLinesScrolled, onTocSelect, onAltTocSelect,
    onLineSelected, onNavigateSection, onCommentaryScroll,
    onCommentaryTreeChanged, openBookInTab,
    openContentSearch, openCommentarySearch,
    onQueryChange, onSearchNext, onSearchPrev, onModeChange,
    toggleTocPanel, toggleCommentaryTreePanel, closeSidePanel,
    ensureStaticFilterGroupsLoaded, staticFilterGroupsLoaded,
    // toc lookup for copy-with-source
    getActiveTocEntry, getTocPath,
  }
}
