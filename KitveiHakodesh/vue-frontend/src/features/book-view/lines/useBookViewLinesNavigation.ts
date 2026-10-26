/**
 * The ONE way to scroll the book view lines scroller to a line.
 *
 * Every caller — TOC click, Ctrl+Arrow section navigation, a search match, a
 * commentary jump — goes through `scrollToLine`. They differ only in how they
 * name the target and whether they want a match centred afterwards, so those are
 * options on one function rather than separate scrollers.
 *
 * Positioning, in order:
 *   1. Resolve the target to a line index (by id, or given directly).
 *   2. scroller.scrollToIndex — exact, because the scroller mounts the target's rows
 *      before it sets the scroll from their real positions. No retry, no settling.
 *   3. Optional refinement: apply the `.current` mark for a search occurrence and
 *      nudge it into view. The line's text may still be loading (a placeholder
 *      row), so this polls for the mark rather than assuming.
 */
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import { SCROLL_LANDING_GAP_PX } from '@/utils/scrollToIndexWithRetry'
import { setCurrentMark } from './useBookViewLineRenderer'
import type { LineItem } from './useBookViewLinesTable'

/**
 * Height the search bar overlays at the top of the lines scroller. Landings
 * reserve this much extra so the target is not hidden under the bar, and the
 * scroll sync skips it when deriving the top visible line.
 */
export const SEARCH_BAR_INSET_PX = 44

/**
 * Clearance left below a match that lands at the bottom edge. Bigger than the
 * top gap on purpose: a match sitting flush on the bottom edge reads as cut off
 * and leaves nowhere for the eye to continue, so it stops short of the edge with
 * a line or so of the text that follows still showing.
 */
const BOTTOM_LANDING_CLEARANCE_PX = 48

/** Frames to wait for a match's line text to arrive and render its marks (~2s). */
const MARK_POLL_FRAMES = 120

export interface ScrollToLineOptions {
  /**
   * Which search occurrence within the line to mark `.current` and centre.
   * Omit for a plain jump to the line — a TOC click or section navigation has no
   * match to refine toward, and the mark polling is skipped entirely.
   */
  occurrence?: number
  /**
   * Scroll even when already within 2px of the target. Explicit section
   * navigation needs this: sub-pixel rounding can put scrollTop a hair from the
   * target while the view is not actually at the section start.
   */
  force?: boolean
  /**
   * Skip the scroll when the row is already fully visible. Used by jumps that
   * only need the line on screen (a commentary jump) rather than at the top, so
   * reading position is not disturbed for a line the reader can already see.
   */
  skipIfVisible?: boolean
  /**
   * Which way a search step is travelling, deciding WHERE an off-screen match
   * comes to rest.
   *
   * 'backward' (and the default) lands it at the TOP — you are heading back
   * through text you have already read, so the match plus what follows it is what
   * you want in front of you.
   *
   * 'forward' lands it at the BOTTOM, because reading advances downward: the
   * match arrives at the edge you are reading toward, and the lines above it —
   * the ones leading up to it — stay on screen. Pulling it to the top instead
   * would discard all of that context and skip the view past everything between.
   */
  direction?: 'forward' | 'backward'
}

export function useBookViewLinesNavigation(
  scroller: () => WindowedScrollerApi | null,
  lines: () => LineItem[],
  searchBarVisible: () => boolean,
  suppressPositionSave: () => void,
  /** Ends the session-restore correction — a jump supersedes the restore. */
  cancelRestoreCorrection: () => void,
  prioritise: (lineIndex: number) => void,
) {
  // Bumped at the start of every navigation so the mark poll of a previous call
  // cancels instead of stomping the newer call's mark and scroll.
  let scrollGeneration = 0

  /** Resolve a line id to its index, falling back when the line is not loaded yet. */
  function lineIndexForId(lineId: number, fallbackLineIndex?: number): number | undefined {
    return lines().find((line) => line.id === lineId)?.lineIndex ?? fallbackLineIndex
  }

  async function scrollToLine(lineIndex: number, options: ScrollToLineOptions = {}) {
    const api = scroller()
    const scrollerElement = api?.scrollerElement
    if (!api || !scrollerElement) return
    const generation = ++scrollGeneration
    const isStale = () => generation !== scrollGeneration
    prioritise(lineIndex)

    if (options.skipIfVisible && api.isRowFullyVisible(lineIndex)) return

    // The reader is going somewhere else: session restore must not re-land its
    // own target when that chunk arrives.
    cancelRestoreCorrection()

    const { occurrence, force = false, direction = 'backward' } = options
    // No occurrence means no match to refine toward — a plain jump to the line.
    const wantsMark = occurrence != null
    const reserved = searchBarVisible() ? SEARCH_BAR_INSET_PX : 0

    // Applies the .current class and reports whether the mark exists in the DOM yet.
    function applyCurrentMark(): boolean {
      setCurrentMark(scrollerElement!, lineIndex, occurrence ?? 0)
      return currentMark() != null
    }

    function currentMark(): HTMLElement | null {
      return scrollerElement!.querySelector(`[data-index="${lineIndex}"] mark.search-match.current`)
    }

    /** Moves the view only if the mark is not comfortably on screen. False when there is no mark. */
    function adjustToMark(): boolean {
      const mark = currentMark()
      if (!mark) return false
      const markRect = mark.getBoundingClientRect()
      const scrollerRect = scrollerElement!.getBoundingClientRect()
      const relativeTop = markRect.top - scrollerRect.top
      const relativeBottom = markRect.bottom - scrollerRect.top
      const alreadyVisible =
        relativeTop >= reserved + 4 &&
        relativeBottom <= scrollerRect.height - BOTTOM_LANDING_CLEARANCE_PX
      if (!alreadyVisible) {
        suppressPositionSave()
        // Land the mark at the edge the reader is travelling toward: bottom when
        // stepping forward, top when stepping back. See `direction` above.
        scrollerElement!.scrollTop +=
          direction === 'forward'
            ? relativeBottom - scrollerRect.height + BOTTOM_LANDING_CLEARANCE_PX
            : relativeTop - reserved - SCROLL_LANDING_GAP_PX
      }
      return true
    }

    // A match jump moves the view only if the match is not already on screen. The
    // target is the MARK, not the line: hauling the line to the top to reach a
    // match the reader can already see throws away their position for nothing —
    // stepping through matches within one screenful leaves the page still, as a
    // browser's find does. `force` overrides: section navigation is about the
    // line's position, not a match's visibility.
    if (wantsMark && !force && api.isRowRendered(lineIndex)) {
      if (applyCurrentMark()) {
        adjustToMark()
        return
      }
      // The mark is not in the DOM after all (text still loading) — fall through.
    }

    suppressPositionSave()
    await api.scrollToIndex(lineIndex, { offsetPx: -(reserved + SCROLL_LANDING_GAP_PX) })
    if (isStale() || !wantsMark) return

    // The row is mounted; its text may not be. Poll for the mark, then refine.
    let attempts = 0
    function tryApplyMark() {
      if (isStale()) return
      suppressPositionSave()
      if (applyCurrentMark()) {
        adjustToMark()
        return
      }
      if (++attempts < MARK_POLL_FRAMES) requestAnimationFrame(tryApplyMark)
    }
    tryApplyMark()
  }

  /**
   * Same scroll, addressed by line id — for callers holding a TOC entry or a
   * commentary target rather than an index. `fallbackLineIndex` covers the line
   * not being loaded into the lines array yet.
   */
  function scrollToLineId(
    lineId: number,
    fallbackLineIndex?: number,
    options: ScrollToLineOptions = {},
  ) {
    const lineIndex = lineIndexForId(lineId, fallbackLineIndex)
    if (lineIndex == null) return
    void scrollToLine(lineIndex, options)
  }

  return { scrollToLine, scrollToLineId }
}
