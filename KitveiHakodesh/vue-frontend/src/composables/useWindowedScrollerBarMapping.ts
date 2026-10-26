/**
 * Maps between a WindowedScroller's viewport (which only holds the mounted window)
 * and the scrollbar drawn beside it (WindowedScrollerBar.vue), which represents the
 * ESTIMATED whole list.
 *
 * The bar's scroll range is: the rows above the window at their estimated heights,
 * then the window at its REAL height (the viewport's own scroll range), then the
 * rows below at their estimates. Inside the window the two positions differ by a
 * constant, so a drag, a wheel or an arrow click on the bar moves the viewport
 * live, pixel for pixel. Outside it, the bar position names a row, and the
 * scroller lands there.
 *
 * Two estimates, the first when the list provides it:
 *
 * - Per-row, from `rowWeights` (a text line's character count): a row's height is
 *   `lineHeight × max(1, ceil(weight / capacity)) + padding`, where capacity is the
 *   weight that fits on one visual line. The three parameters are FITTED to the
 *   mounted rows — their real heights against their weights — and re-fitted as the
 *   reader scrolls, smoothed so a window of odd rows cannot swing them. The weights
 *   themselves never change, so once the fit settles the estimate is static: the
 *   thumb stops creeping and a drag lands where the row really is. Prefix sums make
 *   both directions O(log n); they are rebuilt only when the fit has drifted.
 *
 * - One average, from the mounted rows, smoothed: the fallback while the weights
 *   have not arrived, when the list has none, or until a fit is possible (a window
 *   of equal-height rows cannot tell line height from padding).
 *
 * The same position space carries the scroller's landmarks (section starts, as row
 * indexes) to the bar, which gives each a minimum span of track.
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
  /** Row indexes of the list's section starts, coarse tier first. */
  landmarkTiers: () => readonly (readonly number[])[]
  /** A size weight per row, `count` long, or nothing — see the file comment. */
  rowWeights: () => ArrayLike<number> | null | undefined
}

/** How much of each fresh window average flows into the average estimate. */
const ESTIMATE_SMOOTHING = 0.1
/** How much of each fresh fit flows into the weight calibration. */
const CALIBRATION_SMOOTHING = 0.2
/** A calibration parameter must move this much (relative) before the prefix sums are rebuilt. */
const CALIBRATION_REBUILD_DRIFT = 0.01
/** Fewer mounted rows than this cannot be fitted. */
const MIN_ROWS_TO_FIT = 4

/** The weight-estimate's parameters, in viewport pixels and weight units. */
interface WeightCalibration {
  /** Weight that fits on one visual line. */
  capacity: number
  lineHeight: number
  padding: number
}

export function useWindowedScrollerBarMapping(options: WindowedScrollerBarMappingOptions) {
  /** The estimated height of the whole list. */
  const totalHeight = ref(0)
  /** Where the bar should be scrolled to for the viewport's current position. */
  const position = ref(0)
  /** The landmark tiers as positions, for the bar's track map. */
  const landmarkTiers = ref<number[][]>([])

  let estimatedRowHeight = 0
  /** The seek target while a landing is in flight — reported as the position until it lands. */
  let pendingPosition: number | null = null

  // ── Weight estimate ────────────────────────────────────────────────────────────

  let calibration: WeightCalibration | null = null
  /** The calibration the prefix sums were built with. */
  let appliedCalibration: WeightCalibration | null = null
  /** `prefixSums[i]` = estimated height of rows [0, i); `count + 1` long. */
  let prefixSums: Float64Array | null = null
  let prefixSumsWeights: ArrayLike<number> | null = null
  /** The window the last fit was taken from — a fit per window, not per scroll tick. */
  let fittedWindowStart = -1
  let fittedWindowEnd = -1

  function usableWeights(): ArrayLike<number> | null {
    const weights = options.rowWeights()
    return weights && weights.length === options.count() ? weights : null
  }

  function visualLines(weight: number, capacity: number) {
    return Math.max(1, Math.ceil(weight / capacity))
  }

  /**
   * Least squares of the mounted rows' real extents against their weights, over a
   * geometric grid of capacities (the one non-linear parameter). Null when the rows
   * cannot tell the parameters apart.
   */
  function fitCalibration(weights: ArrayLike<number>): WeightCalibration | null {
    const start = options.windowStart.value
    const end = options.windowEnd.value
    const rowWeights: number[] = []
    const rowExtents: number[] = []
    for (let index = start; index < end - 1; index++) {
      const row = options.rowElement(index)
      const next = options.rowElement(index + 1)
      if (!row || !next) continue
      // Top to next top, so the gap between rows is in the extent.
      rowWeights.push(weights[index]!)
      rowExtents.push(next.offsetTop - row.offsetTop)
    }
    const n = rowWeights.length
    if (n < MIN_ROWS_TO_FIT) return null
    let best: WeightCalibration | null = null
    let bestError = Infinity
    for (let capacity = 10; capacity <= 400; capacity = Math.ceil(capacity * 1.06)) {
      let sumX = 0, sumY = 0, sumXX = 0, sumXY = 0
      for (let k = 0; k < n; k++) {
        const x = visualLines(rowWeights[k]!, capacity)
        const y = rowExtents[k]!
        sumX += x; sumY += y; sumXX += x * x; sumXY += x * y
      }
      const determinant = n * sumXX - sumX * sumX
      if (determinant === 0) continue
      const lineHeight = (n * sumXY - sumX * sumY) / determinant
      const padding = (sumY - lineHeight * sumX) / n
      if (!(lineHeight > 0)) continue
      let error = 0
      for (let k = 0; k < n; k++) {
        const residual = lineHeight * visualLines(rowWeights[k]!, capacity) + padding - rowExtents[k]!
        error += residual * residual
      }
      if (error < bestError) {
        bestError = error
        best = { capacity, lineHeight, padding }
      }
    }
    return best
  }

  function driftExceeds(previous: WeightCalibration, next: WeightCalibration) {
    const relative = (before: number, after: number) => Math.abs(after - before) / Math.max(1, Math.abs(before))
    return (
      relative(previous.capacity, next.capacity) > CALIBRATION_REBUILD_DRIFT ||
      relative(previous.lineHeight, next.lineHeight) > CALIBRATION_REBUILD_DRIFT ||
      relative(previous.padding, next.padding) > CALIBRATION_REBUILD_DRIFT
    )
  }

  function rebuildPrefixSums(weights: ArrayLike<number>, applied: WeightCalibration) {
    const count = weights.length
    const sums = new Float64Array(count + 1)
    let total = 0
    for (let index = 0; index < count; index++) {
      sums[index] = total
      total += visualLines(weights[index]!, applied.capacity) * applied.lineHeight + applied.padding
    }
    sums[count] = total
    prefixSums = sums
    prefixSumsWeights = weights
    appliedCalibration = applied
  }

  /** Fits on a new window, smooths, and rebuilds the sums once the fit has drifted enough. */
  function updateWeightEstimate() {
    const weights = usableWeights()
    if (!weights) {
      prefixSums = null
      prefixSumsWeights = null
      appliedCalibration = null
      return
    }
    const start = options.windowStart.value
    const end = options.windowEnd.value
    if (start !== fittedWindowStart || end !== fittedWindowEnd) {
      fittedWindowStart = start
      fittedWindowEnd = end
      const fitted = fitCalibration(weights)
      if (fitted) {
        calibration = calibration
          ? {
              capacity: calibration.capacity + (fitted.capacity - calibration.capacity) * CALIBRATION_SMOOTHING,
              lineHeight: calibration.lineHeight + (fitted.lineHeight - calibration.lineHeight) * CALIBRATION_SMOOTHING,
              padding: calibration.padding + (fitted.padding - calibration.padding) * CALIBRATION_SMOOTHING,
            }
          : fitted
      }
    }
    if (!calibration) return
    if (!prefixSums || prefixSumsWeights !== weights || !appliedCalibration || driftExceeds(appliedCalibration, calibration)) {
      rebuildPrefixSums(weights, calibration)
    }
  }

  /** Largest row index whose estimated top is at or above `estimatedTop`. */
  function rowAtEstimatedTop(sums: Float64Array, estimatedTop: number): number {
    let low = 0
    let high = options.count() - 1
    while (low < high) {
      const middle = (low + high + 1) >> 1
      if (sums[middle]! <= estimatedTop) low = middle
      else high = middle - 1
    }
    return low
  }

  // ── Average estimate (fallback) ────────────────────────────────────────────────

  function updateAverageEstimate() {
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

  // ── Positions, in the bar's space ──────────────────────────────────────────────

  /** Estimated height of the rows [0, index). */
  function estimatedHeightBefore(index: number): number {
    return prefixSums ? prefixSums[index]! : index * estimatedRowHeight
  }

  /** Estimated height of the rows [from, to). */
  function estimatedHeightBetween(from: number, to: number): number {
    return prefixSums ? prefixSums[to]! - prefixSums[from]! : (to - from) * estimatedRowHeight
  }

  function pixelsAboveWindow() {
    return estimatedHeightBefore(options.windowStart.value)
  }

  /** A row's position: real inside the window, estimated outside it. */
  function positionForIndex(index: number): number {
    const viewport = options.viewport()
    const above = pixelsAboveWindow()
    if (index < options.windowStart.value || !viewport) return estimatedHeightBefore(index)
    if (index < options.windowEnd.value) {
      const row = options.rowElement(index)
      if (row) return above + row.offsetTop
    }
    return above + viewport.scrollHeight + estimatedHeightBetween(options.windowEnd.value, index)
  }

  /** The row whose estimated top is at `estimatedTop`, above or below the window. */
  function indexAtEstimatedTop(estimatedTop: number): number {
    return prefixSums ? rowAtEstimatedTop(prefixSums, estimatedTop) : Math.round(estimatedTop / estimatedRowHeight)
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
      index = indexAtEstimatedTop(target)
    } else if (target < above + viewport.scrollHeight) {
      index = options.findRowIndexAt(target - above, false) ?? options.windowStart.value
    } else {
      const end = options.windowEnd.value
      const belowWindow = target - above - viewport.scrollHeight
      index = prefixSums
        ? rowAtEstimatedTop(prefixSums, prefixSums[end]! + belowWindow)
        : end + Math.round(belowWindow / estimatedRowHeight)
    }
    return Math.max(0, Math.min(count - 1, index))
  }

  // ── Sync ───────────────────────────────────────────────────────────────────────
  //
  // Only the position depends on scrollTop. The estimates, the total and the
  // landmark positions depend on the window (which rows, at what real height), the
  // weights and the landmark list — so they are recomputed only when one of those
  // changes. A plain scroll tick within the window allocates nothing and leaves the
  // bar's track map (built from these by the bar) untouched; the estimates also
  // smooth once per window rather than once per tick, so a long pause on one window
  // cannot drain the smoothing.

  /** The window the estimates and landmark positions were last computed for. */
  let syncedWindowStart = -1
  let syncedWindowEnd = -1
  let syncedScrollHeight = -1
  let syncedCount = -1
  let syncedPrefixSums: Float64Array | null = null
  let syncedTiers: readonly (readonly number[])[] | null = null

  /** The caller may hand over a fresh empty array each time; two empties are the same tiers. */
  function sameTiers(tiers: readonly (readonly number[])[]) {
    return tiers === syncedTiers || (tiers.length === 0 && syncedTiers?.length === 0)
  }

  /** Call whenever the viewport scrolled or its rows changed. */
  function syncFromViewport() {
    const viewport = options.viewport()
    if (!viewport) return
    const start = options.windowStart.value
    const end = options.windowEnd.value
    const scrollHeight = viewport.scrollHeight
    const windowChanged = start !== syncedWindowStart || end !== syncedWindowEnd || scrollHeight !== syncedScrollHeight
    if (windowChanged) {
      updateAverageEstimate()
      updateWeightEstimate()
    }
    if (!estimatedRowHeight && !prefixSums) {
      // Nothing to estimate from yet (no rows mounted): try again on the next call.
      syncedWindowStart = -1
      return
    }
    const above = pixelsAboveWindow()
    const count = options.count()
    const tiers = options.landmarkTiers()
    if (windowChanged || count !== syncedCount || prefixSums !== syncedPrefixSums || !sameTiers(tiers)) {
      syncedWindowStart = start
      syncedWindowEnd = end
      syncedScrollHeight = scrollHeight
      syncedCount = count
      syncedPrefixSums = prefixSums
      syncedTiers = tiers
      totalHeight.value = Math.round(above + scrollHeight + estimatedHeightBetween(end, count))
      landmarkTiers.value = tiers.map((tier) => tier.map(positionForIndex))
    }
    position.value = pendingPosition ?? Math.round(above + viewport.scrollTop)
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
    if (!viewport || (!estimatedRowHeight && !prefixSums)) return
    const above = pixelsAboveWindow()
    const liveMax = above + viewport.scrollHeight - viewport.clientHeight
    if (barTop >= above && barTop <= liveMax) {
      // A landing queued by an earlier seek outside the window is stale now: the
      // drag came back inside, and landing it afterwards would yank the view away.
      pendingIndex = null
      pendingPosition = null
      viewport.scrollTop = barTop - above
      return
    }
    pendingIndex = indexForPosition(barTop)
    pendingPosition = Math.round(barTop)
    position.value = pendingPosition
    void landPending()
  }

  return { totalHeight, position, landmarkTiers, syncFromViewport, seek, indexForPosition }
}
