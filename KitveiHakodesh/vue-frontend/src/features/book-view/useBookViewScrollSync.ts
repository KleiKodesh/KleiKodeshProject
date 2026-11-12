/**
 * Syncs the active TOC entry, the tab's tocPath (the breadcrumb), and commentary
 * auto-select to the book's scroll position.
 *
 * Reader scrolls sync immediately. Programmatic jumps (TOC click, search match,
 * restore) sync where they come to rest: the lines scroller's moveend.
 *
 * The auto-selected commentary (not the line highlight) loads once per scroll:
 * where a reader scroll stops (the lines scroller's moveend) or where a jump settles.
 */
import { onScopeDispose, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { Ref } from 'vue'
import type { LinesTable } from './lines/linesTable'
import type { TocEntry } from '@/webview-host/queries.types'
export function useBookViewScrollSync(
  lines: () => LinesTable,
  activeTocEntryId: Ref<number | undefined>,
  activeAltTocEntryId: Ref<number | undefined>,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  getActiveTocEntry: (lineIndex: number) => TocEntry | null,
  getTocEntryById: (id: number) => TocEntry | null,
  getActiveAltTocEntry: (lineIndex: number) => TocEntry | null,
  getTocPath: (entry: TocEntry) => string,
  /**
   * Fresh read of which line is at the top of the book view RIGHT NOW, straight
   * from the scroller — null while it has no rendered rows. The settle pass below
   * reads it rather than the indices recorded from the last event, so it syncs
   * where the jump came to rest (see readCurrentPosition in useBookViewLinesScroll).
   */
  readLivePosition: () => { lineIndex: number; fullLineIndex: number } | null,
) {
  const paneNavigation = usePaneNavigation()
  const bookViewStore = useBookViewStore()
  const { autoSelectTopLine } = storeToRefs(bookViewStore)

  const currentScrollLineIndex = ref(0)
  const currentFullLineIndex = ref(0)
  /**
   * The line whose commentary auto-select owes, once the reader's scroll stops.
   * Loading on every scroll step reloaded the panel ~30 times for 30 wheel steps;
   * the reader only reads the commentary where the scroll comes to rest.
   */
  let pendingCommentaryLineId: number | null = null

  function commitPendingCommentary() {
    const lineId = pendingCommentaryLineId
    pendingCommentaryLineId = null
    if (lineId == null || !autoSelectTopLine.value) return
    commentaryLineId.value = lineId
  }

  function cancelPendingCommentary() {
    pendingCommentaryLineId = null
  }

  /**
   * The lines scroller stopped moving (its moveend). A jump syncs where it came to
   * rest, read fresh from the scroller; a reader scroll has synced as it went, and
   * auto-select now loads the commentary where they stopped.
   */
  function onLinesMoveEnd() {
    if (isJumpPending) {
      isJumpPending = false
      const live = readLivePosition()
      if (live) applyPositionSync(live.lineIndex, live.fullLineIndex, false)
    }
    commitPendingCommentary()
  }

  /**
   * Bumped every time the READER moves the view (not when the app jumps it).
   * The search panel watches this to re-anchor Enter/Shift+Enter to wherever the
   * reader has scrolled to — see useBookViewSearchPanel.
   */
  const userScrollTick = ref(0)

  /** A programmatic jump moved the view and its resting place is not synced yet. */
  let isJumpPending = false

  /**
   * A position-derived sync must never move the highlight BETWEEN entries that
   * start on the same line — a parent header and its first child often do (a
   * parasha and its first daf, say), and the position alone cannot tell them
   * apart: getActiveTocEntry always resolves to the last of them. Overwriting
   * here would flip every click on such a parent to its child once the jump rests. When
   * the reader's current entry shares the derived entry's line, their choice
   * stands.
   */
  function derivedEntrySupersedesActive(derived: TocEntry): boolean {
    if (activeTocEntryId.value == null) return true
    const active = getTocEntryById(activeTocEntryId.value)
    return !(active && active.lineIndex != null && active.lineIndex === derived.lineIndex)
  }

  /** Sync everything that tracks the reading position to this line index. */
  function applyPositionSync(lineIndex: number, fullLineIndex: number, isUserScroll: boolean) {
    const entry = getActiveTocEntry(lineIndex)
    if (entry && entry.id !== activeTocEntryId.value && derivedEntrySupersedesActive(entry)) {
      activeTocEntryId.value = entry.id
      paneNavigation.updateActiveTab({ tocPath: getTocPath(entry) })
    } else if (!entry && activeTocEntryId.value != null) {
      // Above the first heading (Home on a book with an intro): no entry covers
      // this line, so the old highlight must go rather than linger.
      activeTocEntryId.value = undefined
      paneNavigation.updateActiveTab({ tocPath: undefined })
    }

    // The alt tree tracks the reader independently — it has its own entries, so
    // the main entry's id means nothing to it. The breadcrumb stays on the main TOC.
    const altEntry = getActiveAltTocEntry(lineIndex)
    if (altEntry?.id !== activeAltTocEntryId.value) {
      activeAltTocEntryId.value = altEntry?.id
    }

    if (!autoSelectTopLine.value) return
    const line = lines().at(fullLineIndex)
    if (line && line.content !== null) {
      selectedLineId.value = line.id
      pendingCommentaryLineId = line.id
      // A reader scroll loads where it stops (onLinesMoveEnd); a programmatic
      // jump reaches here only once it has settled, so it IS the stop.
      if (!isUserScroll) commitPendingCommentary()
    }
  }

  function onLinesScrolled(lineIndex: number, fullLineIndex: number, isUserScroll = false) {
    currentScrollLineIndex.value = lineIndex
    currentFullLineIndex.value = fullLineIndex

    // A reader scroll is a real position the moment it happens — sync now, and
    // drop any pending programmatic settle so a stale jump position cannot land
    // on top of where the reader just moved.
    if (isUserScroll) {
      userScrollTick.value++
      isJumpPending = false
      applyPositionSync(lineIndex, fullLineIndex, true)
      return
    }

    // A programmatic jump is only a real position where it comes to rest (a landing,
    // then perhaps the nudge that brings a search mark into view): synced at the
    // scroller's moveend, so the TOC never flicks through an intermediate place.
    isJumpPending = true
  }

  /**
   * Forces a one-time TOC path sync for a known line index without waiting for
   * a scroll event. Used by session restore so the breadcrumb is populated
   * immediately when a book reloads to its saved position.
   *
   * The saved index is the first row with ANY pixel showing, while the live
   * derivation (readCurrentPosition) skips a top sliver up to the landing gap.
   * Left at a section boundary with a sliver of the previous line above, the
   * saved index is that previous line — whose entry is the adjacent section (the
   * other amud). So when the view has already landed on the saved row, the fresh
   * read decides, exactly as the settle pass would; it is accepted only at the
   * saved row or the one after it (the sliver case), never a pre-landing read.
   */
  function syncTocPathForLineIndex(savedLineIndex: number) {
    const live = readLivePosition()
    const landed = live != null && live.lineIndex >= savedLineIndex && live.lineIndex <= savedLineIndex + 1
    const lineIndex = landed ? live.lineIndex : savedLineIndex
    const entry = getActiveTocEntry(lineIndex)
    if (entry) {
      activeTocEntryId.value = entry.id
      paneNavigation.updateActiveTab({ tocPath: getTocPath(entry) })
    }
    activeAltTocEntryId.value = getActiveAltTocEntry(lineIndex)?.id
  }

  watch(autoSelectTopLine, (enabled) => {
    if (!enabled) cancelPendingCommentary()
  })

  // The owed load must not outlive the view: firing after teardown sets commentaryLineId
  // and starts a commentary fetch for a panel that no longer exists.
  onScopeDispose(cancelPendingCommentary)

  return {
    currentScrollLineIndex,
    currentFullLineIndex,
    userScrollTick,
    onLinesScrolled,
    onLinesMoveEnd,
    syncTocPathForLineIndex,
  }
}
