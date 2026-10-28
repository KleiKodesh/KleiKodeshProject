/**
 * Syncs the active TOC entry, the tab's tocPath (the breadcrumb), and commentary
 * auto-select to the book's scroll position.
 *
 * Reader scrolls sync immediately. Programmatic jumps (TOC click, search match,
 * restore) sync once their scroll events go quiet — a jump is a landing and, for a
 * search match, the nudge that brings the mark into view, and only where it comes
 * to rest is the reader's position. See onLinesScrolled.
 */
import { onScopeDispose, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { Ref } from 'vue'
import type { LinesTable } from './lines/linesTable'
import type { TocEntry } from '@/webview-host/queries.types'
import type { CommentaryPinSnapshot } from './bookViewTypes'
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
  captureActivePins: () => CommentaryPinSnapshot,
  applyPendingPins: (snapshot: CommentaryPinSnapshot) => void,
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
  let autoSelectCommentaryTimer: ReturnType<typeof setTimeout> | null = null

  /**
   * Bumped every time the READER moves the view (not when the app jumps it).
   * The search panel watches this to re-anchor Enter/Shift+Enter to wherever the
   * reader has scrolled to — see useBookViewSearchPanel.
   */
  const userScrollTick = ref(0)

  /**
   * How long programmatic scroll events must go quiet before the position they
   * ended on is treated as THE position and synced (TOC entry, breadcrumb,
   * auto-select). Longer than the frame between a landing and its mark nudge, far
   * shorter than anything a reader would notice on top of a jump they just made.
   */
  const PROGRAMMATIC_SETTLE_MS = 200

  let settleTimer: ReturnType<typeof setTimeout> | null = null

  function cancelPendingSettle() {
    if (settleTimer) {
      clearTimeout(settleTimer)
      settleTimer = null
    }
  }

  /**
   * A position-derived sync must never move the highlight BETWEEN entries that
   * start on the same line — a parent header and its first child often do (a
   * parasha and its first daf, say), and the position alone cannot tell them
   * apart: getActiveTocEntry always resolves to the last of them. Overwriting
   * here would flip every click on such a parent to its child 200ms later. When
   * the reader's current entry shares the derived entry's line, their choice
   * stands.
   */
  function derivedEntrySupersedesActive(derived: TocEntry): boolean {
    if (activeTocEntryId.value == null) return true
    const active = getTocEntryById(activeTocEntryId.value)
    return !(active && active.lineIndex != null && active.lineIndex === derived.lineIndex)
  }

  /** Sync everything that tracks the reading position to this line index. */
  function applyPositionSync(lineIndex: number, fullLineIndex: number) {
    const entry = getActiveTocEntry(lineIndex)
    if (entry && entry.id !== activeTocEntryId.value && derivedEntrySupersedesActive(entry)) {
      activeTocEntryId.value = entry.id
      paneNavigation.updateActiveTab({ tocPath: getTocPath(entry) })
    }

    // The alt tree tracks the reader independently — it has its own entries, so
    // the main entry's id means nothing to it. The breadcrumb stays on the main TOC.
    const altEntry = getActiveAltTocEntry(lineIndex)
    if (altEntry && altEntry.id !== activeAltTocEntryId.value) {
      activeAltTocEntryId.value = altEntry.id
    }

    if (!autoSelectTopLine.value) return
    const line = lines().at(fullLineIndex)
    if (line && line.content !== null) {
      selectedLineId.value = line.id
      // Capture the active pinned group synchronously now — groups are still loaded
      // and activePinnedGroup is valid. By the time the timer fires and sets
      // commentaryLineId (triggering a load + groups clear), this value is gone.
      const capturedPins = captureActivePins()
      if (autoSelectCommentaryTimer) clearTimeout(autoSelectCommentaryTimer)
      autoSelectCommentaryTimer = setTimeout(() => {
        applyPendingPins(capturedPins)
        commentaryLineId.value = line.id
      }, 120)
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
      cancelPendingSettle()
      applyPositionSync(lineIndex, fullLineIndex)
      return
    }

    // A programmatic scroll is only a real position once it stops moving. A jump
    // can be more than one scroll event — the landing, then the nudge that brings
    // a search mark to the edge a frame later — and syncing each would flick the
    // TOC panel through the intermediate one. Waiting for the events to go quiet
    // needs no knowledge of target, direction, or who initiated the jump: it syncs
    // where the jump came to rest, which is the only position that matters.
    cancelPendingSettle()
    settleTimer = setTimeout(() => {
      settleTimer = null
      // Read the position FRESH rather than trusting the last event's indices.
      const live = readLivePosition()
      if (!live) return
      applyPositionSync(live.lineIndex, live.fullLineIndex)
    }, PROGRAMMATIC_SETTLE_MS)
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
    if (!enabled && autoSelectCommentaryTimer) {
      clearTimeout(autoSelectCommentaryTimer)
      autoSelectCommentaryTimer = null
    }
  })

  // The 120ms timer must not outlive the view: firing after teardown sets commentaryLineId
  // and starts a commentary fetch for a panel that no longer exists.
  onScopeDispose(() => {
    cancelPendingSettle()
    if (autoSelectCommentaryTimer) {
      clearTimeout(autoSelectCommentaryTimer)
      autoSelectCommentaryTimer = null
    }
  })

  return {
    currentScrollLineIndex,
    currentFullLineIndex,
    userScrollTick,
    onLinesScrolled,
    syncTocPathForLineIndex,
  }
}
