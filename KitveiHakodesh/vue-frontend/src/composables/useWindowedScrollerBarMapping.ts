/**
 * Maps between a WindowedScroller's viewport (which only holds the mounted window)
 * and the scrollbar drawn beside it (WindowedScrollerBar.vue), which represents the
 * ESTIMATED whole list.
 *
 * The bar's scroll range is: the rows above the window at the estimated row height,
 * then the window at its REAL height (the viewport's own scroll range), then the
 * rows below at the estimate. Inside the window the two positions differ by a
 * constant, so a drag, a wheel or an arrow click on the bar moves the viewport
 * live, pixel for pixel. Outside it, the bar position names a row, and the
 * scroller lands there.
 *
 * The estimate is the average height of the mounted rows, smoothed, so the bar's
 * thumb does not twitch as rows of different heights pass through the window.
 */
import { ref, type Ref } from 'vue'

export interface WindowedScrollerBarMappingOptions {
  count: () => number
  viewport: () => HTMLElement | null
  windowStart: Ref<number>
  windowEnd: Ref<number>
  rowElement: (index: number) => HTMLElement | null
  /** The mounted row at a viewport-content offset — the window's own lookup. */
  findRowIndexAt: (top: number, fully: boolean) => number | null
  landAt: (index: number) => Promise<void>
}

/** How much of each fresh window average flows into the estimate. */
const ESTIMATE_SMOOTHING = 0.1

export function useWindowedScrollerBarMapping(options: WindowedScrollerBarMappingOptions) {
  /** The estimated height of the whole list. */
  const totalHeight = ref(0)
  /** Where the bar should be scrolled to for the viewport's current position. */
  const position = ref(0)

  let estimatedRowHeight = 0

  function updateEstimate() {
    const rows = options.windowEnd.value - options.windowStart.value
    if (rows <= 0) return
    const first = options.rowElement(options.windowStart.value)
    const last = options.rowElement(options.windowEnd.value - 1)
    if (!first || !last) return
    const average = (last.offsetTop + last.offsetHeight - first.offsetTop) / rows
    if (!(average > 0)) return
    estimatedRowHeight = estimatedRowHeight
      ? estimatedRowHeight + (average - estimatedRowHeight) * ESTIMATE_SMOOTHING
      : average
  }

  function pixelsAboveWindow() {
    return options.windowStart.value * estimatedRowHeight
  }

  /**
   * The row at a bar position, in the bar's own space: estimated above the window,
   * the window at its real height, estimated again below it. Below the window the
   * real height matters — the estimate lags the rows just landed on, and dividing
   * the whole position by it could resolve to a row still inside the window.
   */
  function indexForPosition(target: number): number {
    const count = options.count()
    const viewport = options.viewport()
    const above = pixelsAboveWindow()
    let index: number
    if (!viewport || target < above) {
      index = Math.round(target / estimatedRowHeight)
    } else if (target < above + viewport.scrollHeight) {
      index = options.findRowIndexAt(target - above, false) ?? options.windowStart.value
    } else {
      index = options.windowEnd.value + Math.round((target - above - viewport.scrollHeight) / estimatedRowHeight)
    }
    return Math.max(0, Math.min(count - 1, index))
  }

  /** Call whenever the viewport scrolled or its rows changed. */
  function syncFromViewport() {
    const viewport = options.viewport()
    if (!viewport) return
    updateEstimate()
    if (!estimatedRowHeight) return
    const above = pixelsAboveWindow()
    const below = (options.count() - options.windowEnd.value) * estimatedRowHeight
    totalHeight.value = Math.round(above + viewport.scrollHeight + below)
    position.value = pendingPosition ?? Math.round(above + viewport.scrollTop)
    landmarkTiers.value = options.landmarkTiers().map((tier) => tier.map(positionForIndex))
  }

  // Seeks outside the window land the scroller, which is asynchronous (rows are
  // fetched first). A drag produces many per second: only the latest matters, so
  // they coalesce into one landing at a time. While one is in flight the reported
  // position is the seek's TARGET, not the viewport's — a patch landing meanwhile
  // (an extension finishing) must not pull the thumb back to where the view still
  // is, only for the landing to throw it forward again.
  let isLanding = false
  let pendingIndex: number | null = null

  async function landPending() {
    if (isLanding) return
    isLanding = true
    try {
      while (pendingIndex != null) {
        const index = pendingIndex
        pendingIndex = null
        await options.landAt(index)
      }
    } finally {
      isLanding = false
      pendingPosition = null
      syncFromViewport()
    }
  }

  /** The user scrolled the bar to `barTop`. */
  function seek(barTop: number) {
    const viewport = options.viewport()
    if (!viewport || !estimatedRowHeight) return
    const above = pixelsAboveWindow()
    const liveMax = above + viewport.scrollHeight - viewport.clientHeight
    if (barTop >= above && barTop <= liveMax) {
      viewport.scrollTop = barTop - above
      return
    }
    pendingIndex = indexForPosition(barTop)
    void landPending()
  }

  return { totalHeight, position, syncFromViewport, seek, indexForPosition }
}
