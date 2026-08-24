/**
 * Syncs the active TOC entry, the tab's tocPath (the breadcrumb), and commentary
 * auto-select to the book's scroll position.
 *
 * Reader scrolls sync immediately. Programmatic jumps (TOC click, search match,
 * restore) sync once their event burst settles — the virtualizer reaches a far
 * target by hopping on estimated offsets and correcting, and only the settled
 * position is anywhere the reader actually is. See onLinesScrolled.
 */
import { onScopeDispose, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { Ref } from 'vue'
import type { LineItem } from './lines/useBookViewLinesTable'
import type { TocEntry } from '@/webview-host/queries.types'
import type { CommentaryPinSnapshot } from './bookViewTypes'
export function useBookViewScrollSync(
  lines: () => LineItem[],
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
   * must use this rather than the indices recorded from scroll events: a long
   * jump's event fires before the virtualizer re-renders, so the event-time index
   * describes the OLD position (see readCurrentPosition in useBookViewLinesScroll).
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
   * auto-select). Longer than a frame gap between correction hops, far shorter
   * than anything a reader would notice on top of a jump they just made.
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
    const line = lines().find((l) => l.lineIndex === fullLineIndex)
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
    // is not one scroll event but a burst — the virtualizer hops on ESTIMATED
    // offsets, overshoots, and corrects itself as measured heights replace the
    // estimates — and every event in that burst is somewhere the reader never
    // was. Syncing them live is what flickered the TOC panel through arbitrary
    // sections on some clicks and not others (whichever hops the estimates
    // happened to produce).
    //
    // The previous design tried to recognise the burst's LAST event by comparing
    // line indexes against the jump's target and direction, and lost every time
    // an estimate overshot: the hop past the target read as "arrived", the latch
    // released, and the corrections leaked through. Waiting for the burst to go
    // quiet needs no knowledge of target, direction, or who initiated the jump —
    // it syncs the settled position, which is the only one that matters.
    cancelPendingSettle()
    settleTimer = setTimeout(() => {
      settleTimer = null
      // Read the position FRESH — the virtualizer has re-rendered by now, so this
      // is where the book actually is. The event-time indices above can be stale
      // by an entire jump (they were derived from the pre-jump window).
      const live = readLivePosition()
      if (!live) return
      applyPositionSync(live.lineIndex, live.fullLineIndex)
    }, PROGRAMMATIC_SETTLE_MS)
  }

  /**
   * Forces a one-time TOC path sync for a known line index without waiting for
   * a scroll event. Used by session restore so the breadcrumb is populated
   * immediately when a book reloads to its saved position.
   */
  function syncTocPathForLineIndex(lineIndex: number) {
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
