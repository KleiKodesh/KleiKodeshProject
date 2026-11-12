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
 */
import { ref, shallowRef, computed, watch, onMounted, onBeforeUnmount, inject } from 'vue'
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
import { useCommentaryNavigation } from './commentary/useCommentaryNavigation'
import { useCommentaryPanelSlot } from './commentary/useCommentaryPanelSlot'
import { serializeCommentaryCheckState } from './commentary/uncheckedCommentaryBooks'
import { useBookViewScrollSync } from './useBookViewScrollSync'
import { useBookViewSessionRestore } from './useBookViewSessionRestore'
import { useBookViewKeyboardShortcuts } from './useBookViewKeyboardShortcuts'
import { useBookViewLineSelection } from './useBookViewLineSelection'
import { useBookViewSidePanel } from './useBookViewSidePanel'
import { useBookViewSearchPanel } from './useBookViewSearchPanel'
import { useBookViewTocNavigation } from './useBookViewTocNavigation'
import { useBookViewCommentaryAnnotations } from './useBookViewCommentaryAnnotations'
import { COMMENTARY_SLOTS } from './bookViewTypes'
import type { CommentaryGroup } from './commentary/useCommentary'
import type { CommentaryPanel } from './commentary/useCommentaryPanelSlot'
import type {
  CommentaryPanelLiveStates,
  CommentarySlot,
  TocPersistState,
} from './bookViewTypes'
import type { ScrollToLineOptions } from './lines/useBookViewLinesNavigation'
import type { Highlight } from './lines/useBookViewHighlights'
import type { Note } from './lines/useBookViewNotes'
import type { LineNote, WordLinkAnchor } from '@/webview-host/queries.types'
export type { SearchMode } from './bookViewTypes'

// Component instance types — used only for ref typing
type ToolbarInstance = { tocBtnRef: HTMLElement | null; createBookmark: () => void }
type LinesContentInstance = {
  scrollToLine: (lineIndex: number, options?: ScrollToLineOptions) => void
  scrollToLineId: (lineId: number, lineIndex?: number, options?: ScrollToLineOptions) => void
  focusScroller: () => void
  scrollerElement: () => HTMLElement | null | undefined
  readCurrentPosition: () => { lineIndex: number; fullLineIndex: number } | null
  retainedLineRanges: () => ReadonlyArray<readonly [number, number]>
  onLinesTextEvicted: (lineIds: number[]) => void
  $el?: HTMLElement
}
type SearchBarInstance = { focus: (opts?: { selectAll?: boolean }) => void }
type CommentaryViewInstance = {
  topVisibleFlatIndex: number
  activeBookId: number | null
  activePinnedGroup: { bookId: number; sectionLabel: string; subSectionLabel: string } | null
  getFilterButtonEl?: () => HTMLElement | null
  scrollToGroup: (bookId: number, sectionLabel?: string, subSectionLabel?: string, reason?: string) => void
  scrollToFlatIndex: (index: number, occurrence?: number) => void
  captureScrollPos?: () => { scrollIndex: number; scrollOffset: number } | null
  $el?: HTMLElement
}

/** The TOC tree component, for the state it owns and the book view persists. */
type TocTreeInstance = {
  snapshotState: () => Omit<TocPersistState, 'visible'>
}

export function useBookView(
  toolbarRef: () => ToolbarInstance | null,
  linesContentRef: () => LinesContentInstance | null,
  searchBarRef: () => SearchBarInstance | null,
  /** One CommentaryView ref per panel, set by BookViewPage. */
  commentaryViewRefs: Record<CommentarySlot, () => CommentaryViewInstance | null>,
  /** Whichever TOC tree is rendered (docked or overlay), or null while closed. */
  tocTreeRef: () => TocTreeInstance | null = () => null,
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
  const openTocLineOffsetPx = paneNavigation.activeTab.openTocLineOffsetPx
  const flashOpenLine = paneNavigation.activeTab.flashOpenLine ?? false
  const searchHighlightLineIndex = paneNavigation.activeTab.searchHighlightLineIndex
  const searchHighlightQuery = paneNavigation.activeTab.searchHighlightQuery ?? ''
  const searchHighlightSnippet = paneNavigation.activeTab.searchHighlightSnippet
  const searchHighlightTerms = paneNavigation.activeTab.searchHighlightTerms
  const openNoteIndex = paneNavigation.activeTab.openNoteIndex
  // One-shot: a later remount of this tab must not pop the note open again.
  if (openNoteIndex != null) paneNavigation.updateActiveTab({ openNoteIndex: undefined })

  // Where to open is ONE navigation's argument: it is consumed here, whatever set it
  // (a TOC entry, a search result, a deep link, Back, a recents row). Left on the tab,
  // the next remount of this tab (a tab switch) would open at that line again
  // instead of where the reader has since got to.
  if (openTocEntryId != null || openTocLineIndex != null || flashOpenLine)
    paneNavigation.updateActiveTab({
      openTocEntryId: undefined,
      openTocLineIndex: undefined,
      openTocLineOffsetPx: undefined,
      flashOpenLine: undefined,
    })
  // The search highlight goes with a TOC or deep-link open (it belongs to an earlier
  // navigation); after a search-result open it stays until the reader scrolls.
  if (openTocEntryId != null || flashOpenLine)
    paneNavigation.updateActiveTab({
      searchHighlightLineIndex: undefined,
      searchHighlightQuery: undefined,
      searchHighlightSnippet: undefined,
      searchHighlightTerms: undefined,
    })

  // ── Data loading ─────────────────────────────────────────────────────────

  const {
    getActiveTocEntry, getActiveAltTocEntry, getTocPath,
    altTocSections, tocLabelSections, tocLabelPrintingNumbers, altTocPane,
    tocEntries, tocSearchTree,
    loading: tocLoading, error: tocError, tocLoaded,
  } = useToc(() => bookId, () => bookTitle)

  const {
    lines, linesRevision, prefetch, ensureLines, ensureLineIds, readLines,
    hasCommentaries, hasRelatedBooks, hasTeamim: bookHasTeamim, isCommentary: bookIsCommentary, lineCharacterCounts,
    versions, activeVersionId, setVersion,
    hebrewBooksEditions,
  } = useLines(() => bookId, {
    retainedRanges: () => linesContentRef()?.retainedLineRanges() ?? [],
    onTextEvicted: (lineIds) => linesContentRef()?.onLinesTextEvicted(lineIds),
  })

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
  const activeAltTocEntryId = ref<number | undefined>(undefined)
  const selectedLineId = ref<number | null>(null)
  const commentaryLineId = ref<number | null>(null)

  // ── Deferred wiring ────────────────────────────────────────────
  // A cycle has to be broken here — a holder whose members are replaced once the
  // real owner exists: panels need annotation getters <-> annotations need the
  // panels' groups.
  // Every call site runs after setup (a render, or a user action), so the holders are
  // always populated by the time they are read.

  const annotationFns = {
    getHighlightsForLine: (_lineId: number): Highlight[] => [],
    getNotesForLine: (_lineId: number): Note[] => [],
    getWordLinkAnchorsForLine: (_lineId: number): WordLinkAnchor[] => [],
    getLineNotesForLine: (_lineId: number): LineNote[] => [],
  }

  // ── Line selection ────────────────────────────────────────────────────────

  const {
    manualSelectionLineIds,
    selectedSectionLineIds, selectedSectionLineRange, clearManualSelection, onLineSelected: onLineSelectedRaw,
  } = useBookViewLineSelection(
    () => lines.value,
    () => tocEntries.value,
    commentaryLineId,
    selectedLineId,
    ensureLineIds,
  )

  // ── Commentary data (shared by both panels) ───────────────────────────────

  const {
    groups, filterGroups, staticFilterGroups,
    loading: commentaryLoading, loadError: commentaryLoadError,
    staticFilterGroupsLoaded, ensureStaticFilterGroupsLoaded, ensureLineContents, trimLineContents,
  } = useCommentary(
    () => commentaryLineId.value,
    () => selectedSectionLineIds.value,
    () => bookId ?? undefined,
    () => selectedSectionLineRange.value,
  )

  // ── The commentary panels (one per slot) ──────────────────────────────────

  const sharedCommentaryDeps = {
    bookId,
    groups,
    staticFilterGroups,
    staticFilterGroupsLoaded,
    loading: commentaryLoading,
    selectedLineId,
    commentaryLineId,
    hasCommentaries,
    lines: () => lines.value,
    ensureStaticFilterGroupsLoaded,
    getHighlightsForLine: (lineId: number) => annotationFns.getHighlightsForLine(lineId),
    getNotesForLine: (lineId: number) => annotationFns.getNotesForLine(lineId),
    getWordLinkAnchorsForLine: (lineId: number) => annotationFns.getWordLinkAnchorsForLine(lineId),
    getLineNotesForLine: (lineId: number) => annotationFns.getLineNotesForLine(lineId),
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

  /** The open panels in display order, for the search bar's mode menu. */
  const openCommentarySlots = computed(() =>
    COMMENTARY_SLOTS.filter((slot) => panels[slot].visible.value),
  )

  /**
   * An explicit line click voids every panel's saved commentary place: the new
   * line's list opens at the panel's commentator, and a place saved for another
   * line's list (a session-restored one included) must not be what its scroller is
   * created at. The place repopulates as soon as the new list is on screen — the
   * panel reports where it is on every scroll.
   */
  function onLineSelected(lineId: number, isShiftClick: boolean) {
    // Re-clicking the already-selected line is a no-op: the panels keep their
    // scroll positions instead of being reset to their pinned group.
    if (!onLineSelectedRaw(lineId, isShiftClick)) return
    // The invalidation runs after the raw handler but in the same tick, before
    // the commentary reload watcher flushes.
    for (const slot of COMMENTARY_SLOTS) {
      panels[slot].scrollIndex.value = null
      panels[slot].scrollOffset.value = null
    }
  }

  /**
   * Ctrl+Down / Ctrl+Up: selects the next / previous line, exactly as a click would,
   * and keeps it on screen. Needs an open commentary panel and an already selected
   * line; stops at the first and last line.
   */
  async function stepSelectedLine(delta: 1 | -1) {
    if (!anyCommentaryVisible.value || selectedLineId.value == null) return
    const current = lines.value.indexOfId(selectedLineId.value)
    if (current == null) return
    const target = current + delta
    if (target < 0 || target >= lines.value.length) return
    if ((lines.value.at(target)?.id ?? 0) <= 0) {
      await ensureLineIds(target, target + 1).catch(() => {})
    }
    const lineId = lines.value.at(target)?.id ?? 0
    if (lineId <= 0) return
    onLineSelected(lineId, false)
    linesContentRef()?.scrollToLineId(lineId, target, { skipIfVisible: true })
  }

  /**
   * Ctrl+Down / Ctrl+Up with no commentary open: brings the next / previous line to
   * the top. A line cut off at the top counts as the current one's neighbour: Down
   * lands on the first whole line, Up on the cut one.
   */
  function scrollOneLine(delta: 1 | -1) {
    const content = linesContentRef()
    const position = content?.readCurrentPosition()
    if (!content || !position) return
    const cut = position.lineIndex !== position.fullLineIndex
    const target = delta > 0
      ? (cut ? position.fullLineIndex : position.lineIndex + 1)
      : (cut ? position.lineIndex : position.lineIndex - 1)
    if (target < 0 || target >= lines.value.length) return
    content.scrollToLine(target, { force: true })
  }

  /**
   * Enter (caret browsing): selects the line the caret is in, as a click would.
   * Only with a commentary open and a collapsed caret inside the lines pane.
   */
  function selectLineAtCaret(): boolean {
    if (!anyCommentaryVisible.value) return false
    const scrollerElement = linesContentRef()?.scrollerElement()
    const selection = window.getSelection()
    if (!scrollerElement || !selection?.isCollapsed || !selection.focusNode) return false
    const node = selection.focusNode
    const element = node instanceof Element ? node : node.parentElement
    const row = element?.closest<HTMLElement>('[data-index]')
    if (!row || !scrollerElement.contains(row)) return false
    const lineId = lines.value.at(Number(row.dataset.index))?.id ?? 0
    if (lineId <= 0) return false
    onLineSelected(lineId, false)
    return true
  }

  // ── TOC ───────────────────────────────────────────────────────────────────

  const {
    currentScrollLineIndex,
    currentFullLineIndex,
    userScrollTick,
    onLinesScrolled,
    onLinesMoveEnd,
    syncTocPathForLineIndex,
  } = useBookViewScrollSync(
    () => lines.value,
    activeTocEntryId,
    activeAltTocEntryId,
    selectedLineId,
    commentaryLineId,
    getActiveTocEntry,
    (id) => tocEntries.value.find((e) => e.id === id) ?? null,
    getActiveAltTocEntry,
    getTocPath,
    () => linesContentRef()?.readCurrentPosition() ?? null,
  )

  // The alt sections finish loading after the reader is already scrolled, and no
  // scroll event follows, so resolve the active alt entry against the current position here.
  watch(altTocPane, () => {
    activeAltTocEntryId.value = getActiveAltTocEntry(currentScrollLineIndex.value)?.id
  })

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
    getLineNotesForLine: getCommentaryLineNotesForLine, scheduleLineNotesLoad,
    prepareForLines: prepareCommentaryExportData,
    prepareForRenderedHtml: prepareCommentaryExportTargets,
    resolveWordLinkTarget: resolveCommentaryWordLinkTarget,
    commentaryTocPaths,
  } = useBookViewCommentaryAnnotations(
    () => annotationGroups.value,
    () => selectedSectionLineIds.value,
  )

  annotationFns.getHighlightsForLine = getHighlightsForLine
  annotationFns.getNotesForLine = getNotesForLine
  annotationFns.getWordLinkAnchorsForLine = getWordLinkAnchorsForLine
  annotationFns.getLineNotesForLine = getCommentaryLineNotesForLine

  // ── Search panel ──────────────────────────────────────────────────────────

  const contentSearch = useBookViewSearch({
    bookId: () => bookId,
    activeVersionId: () => activeVersionId.value,
    lineCount: () => lines.value.length,
    currentLineIndex: () => currentFullLineIndex.value,
  })

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
    userScrollTick,
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

  const sidePanel = useBookViewSidePanel(toolbarRef)

  // ── TOC navigation + keyboard shortcuts ──────────────────────────────────

  const {
    onTocSelect, onAltTocSelect, navigateToAdjacentTocSection, altTocLabelMap,
  } = useBookViewTocNavigation(
    () => tocEntries.value,
    activeTocEntryId,
    activeAltTocEntryId,
    linesContentRef,
    getActiveTocEntry,
    getActiveAltTocEntry,
    getTocPath,
    tocLabelSections,
    tocLabelPrintingNumbers,
    openTocEntryId,
  )

  useBookViewKeyboardShortcuts(
    linesContentRef,
    COMMENTARY_SLOTS.map((slot) => commentaryViewRefs[slot]),
    () => hasToc.value,
    navigateToAdjacentTocSection,
    () => settingsStore.toggleCleanText(),
    () => toolbarRef()?.createBookmark(),
    (delta) => {
      if (anyCommentaryVisible.value) void stepSelectedLine(delta)
      else scrollOneLine(delta)
    },
    selectLineAtCaret,
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
      // A scroller not open yet takes it as where its next one is created (goTo queues it).
      commentaryViewRefs[slot]()?.scrollToGroup(commentaryBookId, undefined, undefined, 'nav-same-anchor')
      return
    }
    // Section nav steers this panel to this book; the other panels keep theirs.
    panels[slot].pinExplicitly(commentaryBookId)
  }

  function onNavigateSection(slot: CommentarySlot, direction: 'next' | 'prev', commentaryBookId: number) {
    return commentaryNavigation[slot].onNavigateSection(direction, commentaryBookId)
  }

  function openBookTarget(targetBookId: number, lineIndex: number | undefined) {
    // Resolve the title from the global books map, not the commentary groups:
    // a word-link citation usually targets a book that is NOT commenting on the
    // current line, and an empty title here is never rewritten — the tab and
    // breadcrumb keep a blank title with a stray chevron beside it.
    paneNavigation.openBookTarget({
      title: booksDataStore.allBooksMap.get(targetBookId)?.title
        ?? groups.value.find((group) => group.bookId === targetBookId)?.bookTitle
        ?? '',
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
  function commentaryPersistState(): CommentaryPanelLiveStates {
    const result: CommentaryPanelLiveStates = {}
    for (const slot of COMMENTARY_SLOTS) {
      const panel = panels[slot]
      const pinned = panel.pinnedCommentaryGroup.value
      result[slot] = {
        visible: panel.visible.value,
        scrollIndex: panel.scrollIndex.value,
        scrollOffset: panel.scrollOffset.value,
        filterState: panel.treeState,
        checkState: serializeCommentaryCheckState(panel.scopeKey),
        pinnedGroup: pinned ? { ...pinned } : null,
        fraction: panel.fraction.value,
        zoom: bookId != null ? bookViewStore.getCommentaryZoom(tabId, bookId, slot) : undefined,
      }
    }
    return result
  }

  /**
   * The TOC panel's persistable state.
   *
   * The tree component only exists while the panel is open, so its inner state is
   * re-snapshotted whenever it is reachable and remembered after it closes —
   * otherwise closing the panel (or saving while it is shut) would persist an
   * empty tree over a good one. `visible` always comes from the live flag.
   */
  let lastTocInnerState: Omit<TocPersistState, 'visible'> | undefined

  function tocPersistState(): TocPersistState {
    const tree = tocTreeRef()
    if (tree) lastTocInnerState = tree.snapshotState()
    return {
      ...lastTocInnerState,
      visible: sidePanel.tocVisible.value,
    }
  }

  // Snapshot on close: the panel's subtree is destroyed by then, so the state has
  // to be taken while the component is still mounted.
  /**
   * The saved TOC state the tree is CREATED with on a session restore - its trees open
   * at their saved rows instead of opening at the active entry and moving. Dropped
   * once the panel closes: a later reopen shows the tree as the reader left it.
   */
  const restoredTocState = shallowRef<TocPersistState | null>(null)

  watch(sidePanel.tocVisible, (visible, wasVisible) => {
    if (wasVisible && !visible) {
      restoredTocState.value = null
      const tree = tocTreeRef()
      if (tree) lastTocInnerState = tree.snapshotState()
    }
  }, { flush: 'sync' })

  // ── Session restore ───────────────────────────────────────────────────────

  const {
    initialLineIndex, initialScrollTop, initialScrollOffset,
    idbResolved, restoredVersionTitle, restoredSideEdition, restore: restoreSession,
  } = useBookViewSessionRestore(
    tabId, bookId, openTocLineIndex, openTocLineOffsetPx,
    panels,
    selectedLineId, commentaryLineId,
    {
      visible: sidePanel.tocVisible,
      stage: (saved) => { restoredTocState.value = saved },
    },
  )

  // ── Alternate versions ──────────────────────────────────────────────────────
  //
  // Persistence stores the version's TITLE, and the list to resolve it against loads
  // asynchronously after the text — so the saved title is applied by a watcher on the
  // list rather than at restore time, whichever of the two lands second.
  //
  // Once only: a `versions` list that arrives after the reader has already picked a
  // version (or deliberately gone back to the merged text) must not overwrite that.
  let versionRestoreDone = false

  watch(versions, (list) => {
    if (versionRestoreDone || list.length === 0) return
    versionRestoreDone = true
    const title = restoredVersionTitle.value
    if (title == null) return
    // A title that no longer resolves — the library changed underneath the saved
    // state — leaves the book in its merged text rather than guessing at a substitute.
    const match = list.find((v) => v.versionTitle === title)
    if (match) setVersion(match.id)
  }, { immediate: true })

  /** The active version's title, for persistence. null = the merged text. */
  function versionPersistState(): string | null {
    return versions.value.find((v) => v.id === activeVersionId.value)?.versionTitle ?? null
  }

  /** Toolbar handler — switch the text to a version, or back to the merged text. */
  function selectVersion(versionId: number | null) {
    // The reader's own choice from here on; a late-arriving list must not override it.
    versionRestoreDone = true
    setVersion(versionId)
  }

  watch(() => bookId, () => {
    selectedLineId.value = null
    commentaryLineId.value = null
    clearManualSelection()
    // A new book brings its own versions and its own saved title.
    versionRestoreDone = false
    groups.value = []
  })

  // The first text fetch is the window the view will open on — never line 0 on
  // spec. Fired the moment the position is known, alongside the book metadata the
  // view is still waiting for, so the opening render has its text.
  watch(
    () => idbResolved.value,
    (resolved) => { if (resolved) prefetch(initialLineIndex.value ?? initialScrollTop.value ?? 0) },
    { immediate: true },
  )

  // Stop handle for the post-restore TOC-sync watch created inside onMounted (below).
  // `unmounted` matters as much as the handle: restoreSession() awaits IndexedDB, so a tab
  // closed during that await runs onBeforeUnmount FIRST — with the handle still null — and
  // the continuation then creates a watch nothing owns. Check the flag after the await.
  let stopTocSyncWatch: (() => void) | null = null
  let unmounted = false

  onMounted(async () => {
    groups.value = []
    await restoreSession()
    if (unmounted) return // tab closed while restore awaited IndexedDB

    // After session restore the lines scroller opens on the saved line index, but
    // onLinesScrolled has not fired yet, so tocPath is empty and the breadcrumb
    // shows nothing. Sync it now using the restored scroll index.
    // If TOC entries are already loaded (common case), this is synchronous.
    // If not, watch for them to arrive (they load in parallel with session restore).
    const restoredLineIndex = initialScrollTop.value
    if (restoredLineIndex != null) {
      if (tocEntries.value.length > 0) {
        syncTocPathForLineIndex(restoredLineIndex)
      } else {
        // Created after `await restoreSession()`, so this is outside the component's effect
        // scope and Vue will never dispose it. It self-stops once entries arrive, but a book
        // with no TOC leaves the count at 0 forever — hence the explicit handle, cleared by
        // onBeforeUnmount below.
        stopTocSyncWatch = watch(
          () => tocEntries.value.length,
          (count) => {
            if (count === 0) return
            stopTocSyncWatch?.()
            stopTocSyncWatch = null
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
    // The page's own hook runs before its children unmount: the TOC tree is still
    // there. The lines pane saves the TOC in ITS unmount, after the tree has gone -
    // without this snapshot a tab switch saved only `visible`.
    const tocTree = tocTreeRef()
    if (tocTree) lastTocInnerState = tocTree.snapshotState()

    // Detached (created after an await), so nothing else will dispose it. The flag covers
    // the other half: if we unmount BEFORE the await resumes, there is no handle yet and
    // the continuation must not create the watch at all.
    unmounted = true
    stopTocSyncWatch?.()
    stopTocSyncWatch = null

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
    openNoteIndex,
    // book metadata
    bookHasTeamim,
    bookIsCommentary,
    // alternate versions (empty for most books — the toolbar control hides)
    versions, activeVersionId, selectVersion, versionPersistState,
    // scanned editions on HebrewBooks (empty for most books — the toolbar control hides)
    hebrewBooksEditions,
    // UI state
    selectedLineId,
    searchMode: searchPanel.searchMode,
    activeTocEntryId,
    restoredTocState,
    activeAltTocEntryId,
    tocVisible: sidePanel.tocVisible,
    sidePanelVisible: sidePanel.sidePanelVisible,
    sidePanelToggleButtonEl: sidePanel.sidePanelToggleButtonEl,
    searchVisible: searchPanel.searchVisible,
    // the two commentary panels
    panels,
    anyCommentaryVisible,
    openCommentarySlots,
    commentaryPersistState,
    tocPersistState,
    // data
    tabId, bookId,
    lines, linesRevision, ensureLines, readLines, lineCharacterCounts, hasCommentaries, hasRelatedBooks, hasToc,
    groups, filterGroups, commentaryLoading, commentaryLoadError, ensureLineContents, trimLineContents,
    tocEntries, tocSearchTree, altTocSections, altTocPane, tocLoading, tocError,
    altTocLabelMap, selectedSectionLineIds, manualSelectionLineIds,
    // commentary annotation (hoisted — survive v-if toggle)
    getHighlightsForLine, applyHighlight, clearHighlight,
    getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
    scheduleWordLinkAnchorsLoad,
    getCommentaryLineNotesForLine, scheduleLineNotesLoad,
    prepareCommentaryExportData, prepareCommentaryExportTargets, resolveCommentaryWordLinkTarget,
    commentaryTocPaths,
    // export
    bookTitle,
    // scroll / search state
    currentScrollLineIndex,
    idbResolved, restoredSideEdition, initialLineIndex, initialScrollTop, initialScrollOffset,
    flashOpenLine,
    activeMatchCount: searchPanel.activeMatchCount,
    activeMatchIdx: searchPanel.activeMatchIdx,
    contentSearch,
    // handlers
    onLinesScrolled, onLinesMoveEnd, onTocSelect, onAltTocSelect,
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
    getActiveTocEntry, getActiveAltTocEntry, getTocPath,
  }
}
