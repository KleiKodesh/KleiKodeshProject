/**
 * Tracks whether a programmatic TOC scroll is in progress so that
 * onLinesScrolled does not overwrite activeTocEntryId while the virtualizer
 * is still animating to the target line.
 */
import type { TocEntry } from './useToc'

export function useTocScrollTracking() {
  let tocScrolling = false
  let tocScrollTargetLineIndex: number | null = null
  let tocScrollTimer: ReturnType<typeof setTimeout> | null = null

  /** Call before programmatically scrolling to a TOC entry. */
  function beginTocScroll(entry: TocEntry): void {
    tocScrolling = true
    tocScrollTargetLineIndex = entry.lineIndex ?? null
    if (tocScrollTimer) clearTimeout(tocScrollTimer)
    tocScrollTimer = setTimeout(() => {
      tocScrolling = false
      tocScrollTargetLineIndex = null
      tocScrollTimer = null
    }, 300)
  }

  /**
   * Call on every scroll event. Returns true if a programmatic TOC scroll is
   * still in progress and the caller should skip updating activeTocEntryId.
   */
  function checkTocScrollProgress(lineIndex: number): boolean {
    if (!tocScrolling) return false
    const reached =
      tocScrollTargetLineIndex == null
        ? false
        : tocScrollTargetLineIndex === 0
          ? lineIndex === 0
          : lineIndex >= tocScrollTargetLineIndex
    if (reached) {
      tocScrolling = false
      tocScrollTargetLineIndex = null
      if (tocScrollTimer) {
        clearTimeout(tocScrollTimer)
        tocScrollTimer = null
      }
    }
    return true // still in a programmatic scroll — caller should not update active entry
  }

  return { beginTocScroll, checkTocScrollProgress }
}
