/**
 * Central composable for the book view page.
 * Owns all data loading, state, event handlers, and watchers.
 * BookViewPage.vue is a shell that calls this and passes results to the template.
 *
 * THE COMMENTARY PANELS
 * The book view hosts one panel per CommentarySlot — 'bottom' under the text, and
 * 'side' / 'side-left' on either side of it. All are anchored to the same clicked
 * line and share ONE useCommentary fetch — re-querying would be byte-identical work
 * on the app's heaviest payload — while everything downstream of the fetch (pin,
 * filter tree, scroll, search, render) is built per panel by useCommentaryPanelSlot.
 * Nothing here names a slot: add one to COMMENTARY_SLOTS and this file follows.
 *
 * Concerns extracted into focused composables:
 * - useBookViewKeyboardShortcuts  — Ctrl+zoom and Ctrl+arrow section navigation
 * - useBookViewLineSelection      — single-click and shift-click multi-select
 * - useBookViewSidePanel          — TOC / commentary-tree panel open/close
 * - useBookViewSearchPanel        — search panel state and match navigation
 * - useCommentaryPanelSlot        — everything one commentary panel owns
 * - useBookViewLinesBackfillGate  — full-book lines backfill yields to commentary loads
 */
import { ref, computed, watch, nextTick, onMounted, onBeforeUnmount, inject } from 'vue'
import { storeToRefs } from 'pinia'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { TocBridge } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useToc } from './toc/useBookViewToc'
import { useLines } from './lines/useBookViewLinesTable'
import { useCommentary } from './commentary/useCommentary'
import { ensureConnectionTypeNamesLoaded } from './commentary/commentaryConnectionTypes'
import { useBookViewSearch } from './useBookViewSearch'
import { useBookViewTocScrollTracking } from './toc/useBookViewTocScrollTracking'
import { useCommentaryNavigation } from './commentary/useCommentaryNavigation'
import { useCommentaryPanelSlot } from './commentary/useCommentaryPanelSlot'
import { useBookViewScrollSync } from './useBookViewScrollSync'
import { useBookViewSessionRestore } from './useBookViewSessionRestore'
import { useBookViewKeyboardShortcuts } from './useBookViewKeyboardShortcuts'
import { useBookViewLineSelection } from './useBookViewLineSelection'
import { useBookViewSidePanel } from './useBookViewSidePanel'
import { useBookViewSearchPanel } from './useBookViewSearchPanel'
import { useBookViewLinesBackfillGate } from './lines/useBookViewLinesBackfillGate'
import { useBookViewTocNavigation } from './useBookViewTocNavigation'
import { useBookViewCommentaryAnnotations } from './useBookViewCommentaryAnnotations'
import { COMMENTARY_SLOTS } from './bookViewTypes'
import type { CommentaryGroup } from './commentary/useCommentary'
import type { CommentaryPanel } from './commentary/useCommentaryPanelSlot'
import type {
  CommentaryPanelPersistStates,
  CommentaryPinSnapshot,
  CommentarySlot,
} from './bookViewTypes'
import type { Highlight } from './lines/useBookViewHighlights'
import type { Note } from './lines/useBookViewNotes'
import type { WordLinkAnchor } from '@/webview-host/queries.types'
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
  scrollToGroup: (bookId: number, sectionLabel?: string, subSectionLabel?: string, reason?: string) => void
  scrollToFlatIndex: (index: number, occurrence?: number) => void
  captureScrollPos?: () => { scrollIndex: number; scrollOffset: number } | null
  restoreCommentaryScrollPos: (index: number, offset: number) => Promise<void>
  claimRestoreIntent?: () => void
  $el?: HTMLElement
}

export function useBookView(
  toolbarRef: () => ToolbarInstance | null,
  linesContentRef: () => LinesContentInstance | null,
  searchBarRef: () => SearchBarInstance | null,
  /** One CommentaryView ref per panel, set by BookViewPage. */
  commentaryViewRefs: Record<CommentarySlot, () => CommentaryViewInstance | null>,
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

  const { lines, prioritise, prefetch, holdBackfill, releaseBackfill, hasCommentaries, hasRelatedBooks, hasTeamim: bookHasTeamim } = useLines(() => bookId)

  // Warm up the connection type ID table immediately — single tiny query that must
  // resolve before any line-tap commentary load fires its reverse queries.
  void ensureConnectionTypeNamesLoaded()

  // Warm the catalog + commentary metadata in the background too. The first
  // commentary toggle otherwise pays for both (the app's biggest payloads)
  // serialized before anything renders — the "first toggle hangs" report.
  // Both are cached globally, so this is a no-op when already loaded.
  const booksDataStore = useBooksDataStore()
  void booksDataStore.ensureLoaded().catch(() => {})
  void booksDataStore.ensureCommentaryMetadataLoaded().catch(() => {})

  const hasToc = computed(() => tocLoaded.value && tocEntries.value.length > 0)

  // ── Core reactive state ───────────────────────────────────────────────────

  const activeTocEntryId = ref<number | undefined>(undefined)
  const selectedLineId = ref<number | null>(null)
  const commentaryLineId = ref<number | null>(null)

  // ── Deferred wiring ────────────────────────────────────────────
  // Several cycles have to be broken here, all the same way — a holder whose members
  // are replaced once the real owner exists:
  //   line selection needs pin capture <-> pins live on the panels
  //   panels need annotation getters   <-> annotations need the panels' groups
  // Every call site runs after setup (a render, or a user action), so the holders are
  // always populated by the time they are read.

  const pinFns = {
    captureActivePins: (): CommentaryPinSnapshot => ({}),
    applyPendingPins: (_snapshot: CommentaryPinSnapshot) => {},
  }
  const annotationFns = {
    getHighlightsForLine: (_lineId: number): Highlight[] => [],
    getNotesForLine: (_lineId: number): Note[] => [],
    getWordLinkAnchorsForLine: (_lineId: number): WordLinkAnchor[] => [],
  }

  // ── Line selection ────────────────────────────────────────────────────────

  const {
    manualSelectionLineIds,
    selectedSectionLineIds, clearManualSelection, onLineSelected: onLineSelectedRaw,
  } = useBookViewLineSelection(
    () => lines.value,
    () => tocEntries.value,
    commentaryLineId,
    selectedLineId,
    () => pinFns.captureActivePins(),
    (snapshot) => pinFns.applyPendingPins(snapshot),
  )

  // ── Commentary data (shared by both panels) ───────────────────────────────

  const {
    groups, filterGroups, staticFilterGroups,
    loading: commentaryLoading, loadError: commentaryLoadError,
    staticFilterGroupsLoaded, ensureStaticFilterGroupsLoaded, requestContentPriority,
  } = useCommentary(
    () => commentaryLineId.value,
    () => selectedSectionLineIds.value,
    () => bookId ?? undefined,
  )

  // ── The commentary panels (one per slot) ──────────────────────────────────

  const sharedCommentaryDeps = {
    bookId,
    groups,
    staticFilterGroups,
    loading: commentaryLoading,
    selectedLineId,
    commentaryLineId,
    hasCommentaries,
    lines: () => lines.value,
    ensureStaticFilterGroupsLoaded,
    getHighlightsForLine: (lineId: number) => annotationFns.getHighlightsForLine(lineId),
    getNotesForLine: (lineId: number) => annotationFns.getNotesForLine(lineId),
    getWordLinkAnchorsForLine: (lineId: number) => annotationFns.getWordLinkAnchorsForLine(lineId),
  }

  const panels = Object.fromEntries(
    COMMENTARY_SLOTS.map((slot) => [
      slot,
      useCommentaryPanelSlot(slot, tabId, commentaryViewRefs[slot], sharedCommentaryDeps),
    ]),
  ) as Record<CommentarySlot, CommentaryPanel>

  /** True when any panel is open — gates line selection and the backfill hold. */
  const anyCommentaryVisible = computed(() =>
    COMMENTARY_SLOTS.some((slot) => panels[slot].visible.value),
  )

  /** The open panels in display order, for the search bar's mode cycle. */
  const openCommentarySlots = computed(() =>
    COMMENTARY_SLOTS.filter((slot) => panels[slot].visible.value),
  )

  // ── Pin capture across both panels ────────────────────────────────────────

  /**
   * Each panel's currently-shown group, captured synchronously at the moment of a
   * user action — groups are still loaded and activePinnedGroup is still valid. A
   * snapshot rather than one value because the panels sit on different books.
   */
  function captureActivePins(): CommentaryPinSnapshot {
    const snapshot: CommentaryPinSnapshot = {}
    for (const slot of COMMENTARY_SLOTS) {
      // Three-step fallback, because a null here does NOT mean "no preference":
      //   1. what the panel is actually showing right now (its sticky header)
      //   2. the pin it already holds - a panel that is mid-load has an empty list
      //      and reports no active group, and staging null for it would make the
      //      pin watcher fall back to the DEFAULT commentator, silently throwing
      //      away the commentator the reader had chosen. Under hosted bridge
      //      latency, consecutive section-nav clicks hit this on every click.
      //   3. genuinely nothing - the very first load, where the default is right.
      snapshot[slot] =
        commentaryViewRefs[slot]()?.activePinnedGroup ??
        panels[slot].pinnedCommentaryGroup.value ??
        null
    }
    return snapshot
  }

  /** Stage a captured snapshot so each panel's commentaryLineId watcher applies it. */
  function applyPendingPins(snapshot: CommentaryPinSnapshot) {
    for (const slot of COMMENTARY_SLOTS) {
      panels[slot].setPendingPin(snapshot[slot] ?? null)
    }
  }

  pinFns.captureActivePins = captureActivePins
  pinFns.applyPendingPins = applyPendingPins

  /**
   * An explicit line click invalidates every panel's saved commentary scroll
   * position: the pinned-group jump owns positioning for fresh navigation, and a
   * stale session-restored position must not be applied to the new line's
   * commentary (it would land on an arbitrary commentator and suppress the pinned
   * jump). The position refs repopulate naturally — the pinned jump's programmatic
   * scroll fires a scroll event that re-captures them.
   */
  function onLineSelected(lineId: number, isShiftClick: boolean) {
    for (const slot of COMMENTARY_SLOTS) {
      panels[slot].scrollIndex.value = null
      panels[slot].scrollOffset.value = null
    }
    // Re-clicking the already-selected line changes no reactive state, so no
    // commentary reload fires and setupGroupReloadScroll never wakes — jump each
    // panel to its own pinned group explicitly (e.g. the session-restored line:
    // restore put the panels at their saved positions; a deliberate click should
    // still show each panel's default commentator).
    const isSameLineReclick = !isShiftClick && commentaryLineId.value === lineId
    onLineSelectedRaw(lineId, isShiftClick)
    if (!isSameLineReclick) return
    void nextTick(() => {
      for (const slot of COMMENTARY_SLOTS) {
        const pinned = panels[slot].pinnedCommentaryGroup.value
        if (pinned) commentaryViewRefs[slot]()?.scrollToGroup(pinned.bookId, undefined, undefined, 'same-line-reclick')
      }
    })
  }

  // ── TOC ───────────────────────────────────────────────────────────────────

  const { beginTocScroll, checkTocScrollProgress } = useBookViewTocScrollTracking()

  const { currentScrollLineIndex, currentFullLineIndex, onLinesScrolled, syncTocPathForLineIndex } = useBookViewScrollSync(
    () => lines.value,
    activeTocEntryId,
    selectedLineId,
    commentaryLineId,
    checkTocScrollProgress,
    getActiveTocEntry,
    getTocPath,
    captureActivePins,
    applyPendingPins,
  )

  // ── Commentary annotations (shared — hoisted above the panels' v-if) ─────

  /**
   * Every group any panel displays. The lists agree on the real groups and differ
   * only by each panel's pinned placeholder, so union them by identity: notes,
   * highlights and TOC paths must resolve for every pin, and running the fetchers
   * once per panel would multiply every query over the same rows.
   */
  const annotationGroups = computed<CommentaryGroup[]>(() => {
    const lists = COMMENTARY_SLOTS.map((slot) => panels[slot].groupsForDisplay.value)
    // Overwhelmingly the common case: no panel holds a placeholder the others lack,
    // so every list is the same array and the union is a no-op.
    if (lists.every((list) => list === lists[0])) return lists[0]!
    const seen = new Set<CommentaryGroup>()
    const union: CommentaryGroup[] = []
    for (const list of lists) {
      for (const group of list) {
        if (seen.has(group)) continue
        seen.add(group)
        union.push(group)
      }
    }
    return union
  })

  const {
    getHighlightsForLine, applyHighlight, clearHighlight,
    getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
    getWordLinkAnchorsForLine, scheduleWordLinkAnchorsLoad,
    commentaryTocPaths, buildExportHtml,
  } = useBookViewCommentaryAnnotations(
    () => annotationGroups.value,
    () => selectedSectionLineIds.value,
    () => lines.value,
    bookTitle,
    settingsStore,
  )

  annotationFns.getHighlightsForLine = getHighlightsForLine
  annotationFns.getNotesForLine = getNotesForLine
  annotationFns.getWordLinkAnchorsForLine = getWordLinkAnchorsForLine

  // ── Search panel ──────────────────────────────────────────────────────────

  const contentSearch = useBookViewSearch(() => lines.value, () => currentFullLineIndex.value)

  const searchPanel = useBookViewSearchPanel(
    contentSearch,
    Object.fromEntries(
      COMMENTARY_SLOTS.map((slot) => [slot, panels[slot].search]),
    ) as Record<CommentarySlot, CommentaryPanel['search']>,
    linesContentRef,
    commentaryViewRefs,
    searchBarRef,
    () => {
      paneNavigation.updateActiveTab({
        searchHighlightLineIndex: undefined,
        searchHighlightQuery: undefined,
        searchHighlightSnippet: undefined,
        searchHighlightTerms: undefined,
      })
    },
  )

  // In-session search query: restore the last query for this tab (kept in the
  // bookViewStore, never persisted across sessions) so reopening the search bar
  // shows it again, then keep the store in sync as the user types. Cleared on
  // tab close by the store's prune watch. Results are only *shown* while the
  // search bar is visible — see the searchVisible-gated props in BookViewPage.
  // The commentary query is stored per panel for the same reason the panels are
  // separate at all: each keeps its own place.
  contentSearch.query.value = bookViewStore.getSearchQuery(tabId)
  watch(contentSearch.query, (q) => bookViewStore.setSearchQuery(tabId, q))
  for (const slot of COMMENTARY_SLOTS) {
    const search = panels[slot].search
    search.query.value = bookViewStore.getCommentarySearchQuery(tabId, slot)
    watch(search.query, (q) => bookViewStore.setCommentarySearchQuery(tabId, slot, q))
  }

  // ── Side panel (the TOC; each panel owns its own filter tree) ─────────────

  const sidePanel = useBookViewSidePanel(toolbarRef, loadAltTocSections)

  // Make the full-book lines backfill yield to commentary loading — commentary
  // queries must never queue behind ~100 large chunk fetches.
  const backfillGate = useBookViewLinesBackfillGate(
    holdBackfill, releaseBackfill,
    anyCommentaryVisible, commentaryLoading,
  )

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
    linesContentRef,
    COMMENTARY_SLOTS.map((slot) => commentaryViewRefs[slot]),
    () => hasToc.value,
    navigateToAdjacentTocSection,
  )

  // ── Commentary section navigation ─────────────────────────────────────────
  // One instance per panel: the next/prev buttons live inside a panel's header, so
  // the navigation must reopen and re-pin THAT panel.

  const commentaryNavigation = Object.fromEntries(
    COMMENTARY_SLOTS.map((slot) => [
      slot,
      useCommentaryNavigation(
        bookId, selectedLineId, commentaryLineId, panels[slot].visible,
        () => lines.value, () => tocEntries.value, linesContentRef,
        () => manualSelectionLineIds.value,
        clearManualSelection,
        (_targetLineId, commentaryBookId, anchorChanges) =>
          stagePinsForNavigation(slot, commentaryBookId, anchorChanges),
      ),
    ]),
  ) as Record<CommentarySlot, ReturnType<typeof useCommentaryNavigation>>

  /**
   * Staged by useCommentaryNavigation at the instant the anchor line changes.
   *
   * The navigation changes commentaryLineId, which fires EVERY panel's pin watcher
   * - and a panel with no pending pin falls back to its default commentator. So
   * snapshot all panels (as a line tap does), then override the navigating slot: it
   * re-pins to the book it navigated, the others keep whatever they were showing.
   */
  function stagePinsForNavigation(
    slot: CommentarySlot,
    commentaryBookId: number,
    anchorChanges: boolean,
  ) {
    // The anchor is already on the target line, so no pin watcher will fire and a
    // staged pin would sit unclaimed until some later change picked it up. Apply
    // this panel's pin directly and scroll it, leaving the other panel alone.
    if (!anchorChanges) {
      panels[slot].pinExplicitly(commentaryBookId)
      void nextTick(() => commentaryViewRefs[slot]()?.scrollToGroup(
        commentaryBookId, undefined, undefined, 'nav-same-anchor',
      ))
      return
    }
    applyPendingPins(captureActivePins())
    const group = groups.value.find((group) => group.bookId === commentaryBookId)
    panels[slot].setPendingPin(group
      ? { bookId: commentaryBookId, sectionLabel: group.sectionLabel ?? '', subSectionLabel: group.subSectionLabel ?? '' }
      : { bookId: commentaryBookId, sectionLabel: '', subSectionLabel: '' })
  }

  function onNavigateSection(slot: CommentarySlot, direction: 'next' | 'prev', commentaryBookId: number) {
    return commentaryNavigation[slot].onNavigateSection(direction, commentaryBookId)
  }

  function openBookTarget(targetBookId: number, lineIndex: number | undefined) {
    paneNavigation.openBookTarget({
      title: groups.value.find((group) => group.bookId === targetBookId)?.bookTitle ?? '',
      route: '/book-view',
      bookId: targetBookId,
      openTocLineIndex: lineIndex,
    })
  }

  // ── Persistence ───────────────────────────────────────────────────────────

  /**
   * Every panel's persistable state, read at save time by BookViewLinesContent (which
   * owns the scroll-position save this rides along with). Plain objects only — the
   * values end up in IndexedDB, which cannot clone a reactive proxy.
   */
  function commentaryPersistState(): CommentaryPanelPersistStates {
    const result: CommentaryPanelPersistStates = {}
    for (const slot of COMMENTARY_SLOTS) {
      const panel = panels[slot]
      const pinned = panel.pinnedCommentaryGroup.value
      result[slot] = {
        visible: panel.visible.value,
        scrollIndex: panel.scrollIndex.value,
        scrollOffset: panel.scrollOffset.value,
        filterState: panel.treeState,
        pinnedGroup: pinned ? { ...pinned } : null,
        fraction: panel.fraction.value,
        zoom: bookId != null ? bookViewStore.getCommentaryZoom(tabId, bookId, slot) : undefined,
      }
    }
    return result
  }

  // ── Session restore ───────────────────────────────────────────────────────

  const {
    initialLineIndex, initialScrollTop, initialScrollOffset,
    scrollStateReady, idbResolved, restore: restoreSession,
  } = useBookViewSessionRestore(
    tabId, bookId, openTocLineIndex,
    panels,
    selectedLineId, commentaryLineId,
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
    await restoreSession()

    // Panel visibility is final for this restore — release the lines backfill
    // right away when no commentary panel is reopening.
    backfillGate.onSessionRestoreSettled()

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
  const tocBridge: TocBridge = {
    get tocEntries() { return tocEntries.value },
    navigateToEntry: (entry) => onTocSelect(entry),
  }
  bookViewStore.registerTocBridge(tabId, tocBridge)

  onBeforeUnmount(() => {
    // Both the bridge and tocPath are keyed by tab, and an in-place navigation
    // mounts the next book before this one tears down — so only clear what we
    // still own, or we wipe the incoming book's breadcrumb state.
    const stillOurs = bookViewStore.getTocBridge(tabId) === tocBridge
    if (stillOurs) paneNavigation.updateActiveTab({ tocPath: undefined })
    bookViewStore.unregisterTocBridge(tabId, tocBridge)
  })

  // The search query intentionally survives closing the search bar (in-session,
  // per-tab) so reopening restores it. Results are hidden while the bar is closed
  // via the searchVisible-gated props in BookViewPage, so no clear() here.

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
    selectedLineId,
    searchMode: searchPanel.searchMode,
    activeTocEntryId,
    tocVisible: sidePanel.tocVisible,
    sidePanelVisible: sidePanel.sidePanelVisible,
    sidePanelToggleButtonEl: sidePanel.sidePanelToggleButtonEl,
    searchVisible: searchPanel.searchVisible,
    // the two commentary panels
    panels,
    anyCommentaryVisible,
    openCommentarySlots,
    commentaryPersistState,
    // data
    tabId, bookId,
    lines, prioritise, hasCommentaries, hasRelatedBooks, hasToc,
    groups, filterGroups, staticFilterGroups, commentaryLoading, commentaryLoadError, requestContentPriority,
    tocEntries, tocSearchTree, altTocSections, selectedAltTocSection, tocLoading, tocError,
    altTocLabelMap, selectedSectionLineIds, manualSelectionLineIds,
    // commentary annotation (hoisted — survive v-if toggle)
    getHighlightsForLine, applyHighlight, clearHighlight,
    getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
    scheduleWordLinkAnchorsLoad,
    commentaryTocPaths,
    // export
    buildExportHtml, bookTitle,
    // scroll / search state
    currentScrollLineIndex,
    scrollStateReady, idbResolved, initialLineIndex, initialScrollTop, initialScrollOffset,
    flashOpenLine,
    activeMatchCount: searchPanel.activeMatchCount,
    activeMatchIdx: searchPanel.activeMatchIdx,
    contentSearch,
    // handlers
    onLinesScrolled, onTocSelect, onAltTocSelect,
    onLineSelected, onNavigateSection, navigateToAdjacentTocSection,
    openBookTarget,
    openContentSearch: searchPanel.openContentSearch,
    openCommentarySearch: searchPanel.openCommentarySearch,
    toggleSearch: searchPanel.toggleSearch,
    onQueryChange: searchPanel.onQueryChange,
    onSearchNext: searchPanel.onSearchNext,
    onSearchPrev: searchPanel.onSearchPrev,
    onModeChange: searchPanel.onModeChange,
    toggleTocPanel: sidePanel.toggleTocPanel,
    closeSidePanel: sidePanel.closeSidePanel,
    ensureStaticFilterGroupsLoaded, staticFilterGroupsLoaded,
    getActiveTocEntry, getTocPath,
  }
}
