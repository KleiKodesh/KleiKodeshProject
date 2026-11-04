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
 *   2. scroller.scrollToIndex — exact, because the scroller fetches and mounts the
 *      target's rows before it sets the scroll from their real positions. No retry.
 *   3. Optional refinement: apply the `.current` mark for a search occurrence and
 *      nudge it into view.
 */
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import { getLineIndexFromLineId } from '@/webview-host/seforimApi'
import type { LinesTable } from './linesTable'

export { SCROLL_LANDING_GAP_PX, SEARCH_BAR_INSET_PX } from '@/composables/useWindowedRowLanding'
export type { ScrollToLineOptions } from '@/composables/useWindowedRowLanding'
import { useWindowedRowLanding } from '@/composables/useWindowedRowLanding'
import type { ScrollToLineOptions } from '@/composables/useWindowedRowLanding'

export function useBookViewLinesNavigation(
  scroller: () => WindowedScrollerApi | null,
  lines: () => LinesTable,
  searchBarVisible: () => boolean,
  suppressPositionSave: () => void,
  /** Ends the opening's follow-ups — a jump supersedes them. */
  cancelRestore: () => void,
) {
  // The landing itself (search-bar inset, match refinement) is the shared one.
  const { scrollToLine, currentScrollGeneration } = useWindowedRowLanding(scroller, searchBarVisible, suppressPositionSave, cancelRestore)

  /**
   * Resolve a line id to its index: from the loaded lines, else the caller's own index,
   * else the database (lines are fetched on demand, so most of a book is not loaded).
   */
  async function lineIndexForId(lineId: number, fallbackLineIndex?: number): Promise<number | undefined> {
    const loaded = lines().indexOfId(lineId) ?? fallbackLineIndex
    if (loaded != null) return loaded
    try {
      return (await getLineIndexFromLineId(lineId))[0]?.lineIndex
    } catch {
      return undefined
    }
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
    void lineIndexForId(lineId, fallbackLineIndex).then((lineIndex) => {
      if (lineIndex != null) void scrollToLine(lineIndex, options)
    })
  }

  return { scrollToLine, scrollToLineId, lineIndexForId, currentScrollGeneration }
}
