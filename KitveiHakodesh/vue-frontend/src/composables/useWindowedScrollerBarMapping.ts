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
 *   `padding + weight × rate` (see WeightCalibration for why linear). Both are
 *   FITTED to the rows measured so far (a bounded sample), real heights against real text lengths,
 *   so a window of odd rows cannot swing them. The weights
 *   themselves never change, so once the fit settles the estimate is static: the
 *   thumb stops creeping and a drag lands where the row really is. Prefix sums make
 *   both directions O(log n); they are rebuilt only when the fit has drifted.
 *
 *   In flow mode a row flowing inline with its neighbours takes no line of its own:
 *   its extent is `weight × pxPerWeight`, a rate fitted over the measured flowing
 *   rows beside the block model.
 *
 * - One average, from the mounted rows, smoothed: the fallback while the weights
 *   have not arrived, when the list has none, or until enough rows are measured.
 *
 * The same position space carries the scroller's landmarks (section starts, as row
 * indexes) to the bar, which gives each a minimum span of track.
 */
import { ref, type Ref } from 'vue'
import type { WindowedScrollerFlowPlan } from '@/components/windowedScrollerFlowPlan'

export interface WindowedScrollerBarMappingOptions {
  count: () => number
  viewport: () => HTMLElement | null
  windowStart: Ref<number>
  windowEnd: Ref<number>
  rowElement: (index: number) => HTMLElement | null
  /** The mounted row at a viewport-content offset — the window's own lookup. */
  findRowIndexAt: (top: number, fully: boolean) => number | null
  landAt: (index: number, align: 'start' | 'end') => Promise<unknown>
  /** Abandons a landing in flight: the reader has since moved within the window. */
  cancelLanding?: () => void
  /** Row indexes of the list's section starts, coarse tier first. */
  landmarkTiers: () => readonly (readonly number[])[]
  /** A size weight per row, `count` long, or nothing — see the file comment. */
  rowWeights: () => ArrayLike<number> | null | undefined
  /**
   * The block plan (flow mode). A row it lets flow takes no whole line of its own:
   * its extent is simply proportional to its weight, so it is estimated by one
   * pixels-per-weight rate fitted beside the line model. A new plan rebuilds the
   * estimate.
   */
  flowPlan?: () => WindowedScrollerFlowPlan
}

/** How much of each fresh window average flows into the average estimate. */
const ESTIMATE_SMOOTHING = 0.1
/** A calibration parameter must move this much (relative) before the prefix sums are rebuilt. */
const CALIBRATION_REBUILD_DRIFT = 0.03
/** Fewer measured rows of a kind than this cannot be fitted. */
const MIN_ROWS_TO_FIT = 4
/** Measured block rows needed before the line is trusted over the average. */
const MIN_BLOCK_ROWS_TO_FIT = 30
/** Measured block rows kept for the fit. */
const MAX_BLOCK_SAMPLES = 2000
/** Huber's constant, in residual standard deviations, and the reweighting rounds. */
const HUBER_K = 1.5
const HUBER_ROUNDS = 3

/**
 * The weight estimate: a row's extent is LINEAR in its weight: `padding +
 * weight x rate` for a block row, `weight x pxPerWeight` for a flowing one.
 *
 * Linear on purpose. A list's weights may be estimates (FastDocView gives every
 * block of a 64-block group the group's average), and only a linear model is right
 * on averages: the group's estimated total is the real one whatever the split. The
 * earlier per-line model (`ceil(weight / capacity)` lines) rounded averages up and
 * swung 3-50x on a title page of short tall rows and one hard-broken block.
 */
interface WeightCalibration {
  padding: number
  rate: number
  /** Pixels per weight unit of a flowing row; null until flowing rows have been measured. */
  pxPerWeight: number | null
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
  /** The block plan the sums were built with: a new one changes which rows flow. */
  let prefixSumsPlan: WindowedScrollerFlowPlan | null = null
  /** The window the last fit was taken from — a fit per window, not per scroll tick. */
  let fittedWindowStart = -1
  let fittedWindowEnd = -1

  function usableWeights(): ArrayLike<number> | null {
    const weights = options.rowWeights()
    return weights && weights.length === options.count() ? weights : null
  }

  /**
   * Whether a MOUNTED row is rendered flowing: inside a paragraph element. The
   * caller may make a row within a flowing stretch a block (an empty line), and a
   * block's extent must not be read as a flowing row's.
   */
  function isRenderedFlowing(row: HTMLElement) {
    return row.parentElement?.classList.contains('windowed-scroller-paragraph') ?? false
  }

  /** A row's estimated extent; `plan` is read once by the caller for a whole rebuild. */
  function estimatedExtent(index: number, weight: number, applied: WeightCalibration, plan: WindowedScrollerFlowPlan | undefined) {
    if (applied.pxPerWeight != null && (plan?.isFlowRow(index) ?? false)) return weight * applied.pxPerWeight
    return applied.padding + weight * applied.rate
  }

  // ── Measurements, accumulated ──────────────────────────────────────────────────
  //
  // The fit runs over the rows measured so far, not just the mounted window: each
  // row is measured once, kept in a bounded fair sample (2000), so a fit costs a
  // few passes over the sample whatever the list's length. A width or zoom change
  // re-wraps every row, so the measurements start over.

  /** Measured block rows (bounded, a fair random sample past the cap): weight = real text length. */
  let blockWeights = new Float64Array(MAX_BLOCK_SAMPLES)
  let blockExtents = new Float64Array(MAX_BLOCK_SAMPLES)
  let blockCount = 0
  let blockSeen = 0
  let flowSumWeight = 0
  let flowSumExtent = 0
  let flowCount = 0
  let measuredRows: Uint8Array | null = null
  let measuredWeights: ArrayLike<number> | null = null
  let measuredWidth = 0
  let measuredFontSize = ''

  function resetMeasurements(weights: ArrayLike<number>, width: number, fontSize: string) {
    blockCount = blockSeen = 0
    flowSumWeight = flowSumExtent = flowCount = 0
    measuredRows = new Uint8Array(weights.length)
    measuredWeights = weights
    measuredWidth = width
    measuredFontSize = fontSize
    // The old fit described the old wrapping: the average stands in until a new one.
    calibration = null
    appliedCalibration = null
    prefixSums = null
    prefixSumsWeights = null
  }

  /**
   * Adds the window's not-yet-measured rows. Extent is top to next top, so the gap
   * between rows is in it. A mounted block row is measured against its REAL text
   * length (its weight may be only a group average), which estimates the same
   * quantity the weights do, so the rate carries over to the unmounted rows.
   * True when the measurements started over (a new list, width or zoom).
   */
  function accumulateWindow(weights: ArrayLike<number>): boolean {
    let isReset = false
    const viewport = options.viewport()
    if (!viewport) return false
    const width = viewport.clientWidth
    const fontSize = getComputedStyle(viewport).fontSize
    if (!measuredRows || measuredWeights !== weights || width !== measuredWidth || fontSize !== measuredFontSize) {
      resetMeasurements(weights, width, fontSize)
      isReset = true
    }
    const seen = measuredRows!
    const end = options.windowEnd.value
    for (let index = options.windowStart.value; index < end - 1; index++) {
      if (seen[index]) continue
      const row = options.rowElement(index)
      const next = options.rowElement(index + 1)
      if (!row || !next) continue
      seen[index] = 1
      const extent = next.offsetTop - row.offsetTop
      if (isRenderedFlowing(row)) {
        flowSumWeight += weights[index]!
        flowSumExtent += extent
        flowCount++
        continue
      }
      const weight = row.textContent!.length
      blockSeen++
      const slot = blockCount < MAX_BLOCK_SAMPLES ? blockCount++ : Math.floor(Math.random() * blockSeen)
      if (slot < MAX_BLOCK_SAMPLES) {
        blockWeights[slot] = weight
        blockExtents[slot] = extent
      }
    }
    return isReset
  }

  /** Weighted least squares of extent on weight, both parameters kept non-negative. */
  function weightedLine(robustness: Float64Array | null) {
    let sw = 0, sx = 0, sy = 0, sxx = 0, sxy = 0
    for (let k = 0; k < blockCount; k++) {
      const w = robustness ? robustness[k]! : 1
      const x = blockWeights[k]!
      const y = blockExtents[k]!
      sw += w; sx += w * x; sy += w * y; sxx += w * x * x; sxy += w * x * y
    }
    const determinant = sw * sxx - sx * sx
    let rate = determinant > 0 ? (sw * sxy - sx * sy) / determinant : 0
    let padding = sw > 0 ? (sy - rate * sx) / sw : 0
    if (rate < 0) {
      rate = 0
      padding = sw > 0 ? sy / sw : 0
    } else if (padding < 0) {
      // Summed over every row of a long list a negative padding drove the total
      // below zero (the thumb vanished): through the origin instead.
      padding = 0
      rate = sxx > 0 ? sxy / sxx : 0
    }
    return { padding, rate }
  }

  /**
   * A robust line through every measured block row (Huber weights, a few rounds):
   * one hard-broken block of 166 short lines (2.7 px per character against 0.28 in
   * prose) pulled plain least squares 4.5x high over the whole list. Plus the
   * flowing rows' rate. Null until enough rows are measured: the average estimate
   * stands in meanwhile.
   */
  function fitCalibration(weights: ArrayLike<number>): WeightCalibration | null {
    const before = blockSeen + flowCount
    const reset = accumulateWindow(weights)
    // Nothing new measured (a window revisited): the fit would be the same.
    if (!reset && blockSeen + flowCount === before && calibration) return calibration
    const pxPerWeight = flowCount >= MIN_ROWS_TO_FIT && flowSumWeight > 0 ? flowSumExtent / flowSumWeight : null
    if (blockCount < MIN_BLOCK_ROWS_TO_FIT) return pxPerWeight == null ? null : { padding: 0, rate: pxPerWeight, pxPerWeight }
    let line = weightedLine(null)
    const robustness = new Float64Array(blockCount)
    const residuals = new Float64Array(blockCount)
    for (let round = 0; round < HUBER_ROUNDS; round++) {
      for (let k = 0; k < blockCount; k++) {
        residuals[k] = Math.abs(blockExtents[k]! - line.padding - line.rate * blockWeights[k]!)
      }
      // Scale: the median absolute residual (1.4826 makes it a standard deviation).
      // A typed copy sorts numerically and natively: the spread-and-comparator sort
      // cost up to 45 ms on 2000 rows.
      const scale = Math.max(1, 1.4826 * residuals.slice().sort()[blockCount >> 1]!)
      const limit = HUBER_K * scale
      for (let k = 0; k < blockCount; k++) robustness[k] = residuals[k]! <= limit ? 1 : limit / residuals[k]!
      line = weightedLine(robustness)
    }
    return { padding: line.padding, rate: line.rate, pxPerWeight }
  }

  function driftExceeds(previous: WeightCalibration, next: WeightCalibration) {
    const relative = (before: number, after: number) => Math.abs(after - before) / Math.max(1e-9, Math.abs(before))
    return (
      relative(previous.padding, next.padding) > CALIBRATION_REBUILD_DRIFT ||
      relative(previous.rate, next.rate) > CALIBRATION_REBUILD_DRIFT ||
      relative(previous.pxPerWeight ?? 0, next.pxPerWeight ?? 0) > CALIBRATION_REBUILD_DRIFT
    )
  }

  function rebuildPrefixSums(weights: ArrayLike<number>, applied: WeightCalibration) {
    const count = weights.length
    const sums = new Float64Array(count + 1)
    const plan = options.flowPlan?.()
    let total = 0
    for (let index = 0; index < count; index++) {
      sums[index] = total
      total += estimatedExtent(index, weights[index]!, applied, plan)
    }
    sums[count] = total
    prefixSums = sums
    prefixSumsWeights = weights
    appliedCalibration = applied
  }

  /** Fits on a new window and rebuilds the sums once the fit has drifted enough. */
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
      if (fitted) calibration = fitted
    }
    if (!calibration) return
    const plan = options.flowPlan?.() ?? null
    if (
      !prefixSums ||
      prefixSumsWeights !== weights ||
      prefixSumsPlan !== plan ||
      !appliedCalibration ||
      driftExceeds(appliedCalibration, calibration)
    ) {
      rebuildPrefixSums(weights, calibration)
      prefixSumsPlan = plan
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

  // ── Landmarks ──────────────────────────────────────────────────────────────────
  //
  // A landmark's position is its estimated top, except that the window sits at its REAL
  // height: one above the window is at its estimate, one below it is shifted by a single
  // shared amount (the window's real height minus its estimate), and only the few inside
  // the window read their row. So the estimates are computed per landmark only when what
  // they depend on changes (the sums, the tiers, the count, or the average while it is
  // the estimate) - a window move is one add per landmark, no layout read, and the bar
  // gets new arrays only when a position actually moved (its track map rebuilds on them).

  let landmarkEstimates: Float64Array[] = []
  let estimatesTiers: readonly (readonly number[])[] | null = null
  let estimatesSums: Float64Array | null = null
  let estimatesCount = -1
  let estimatesRowHeight = -1

  function syncLandmarkPositions(tiers: readonly (readonly number[])[], start: number, end: number, shiftBelow: number) {
    const count = options.count()
    if (
      tiers !== estimatesTiers ||
      prefixSums !== estimatesSums ||
      count !== estimatesCount ||
      (!prefixSums && estimatedRowHeight !== estimatesRowHeight)
    ) {
      landmarkEstimates = tiers.map((tier) => Float64Array.from(tier, estimatedHeightBefore))
      estimatesTiers = tiers
      estimatesSums = prefixSums
      estimatesCount = count
      estimatesRowHeight = estimatedRowHeight
    }
    const above = pixelsAboveWindow()
    const previous = landmarkTiers.value
    let changed = previous.length !== tiers.length
    const next = tiers.map((tier, t) => {
      const estimates = landmarkEstimates[t]!
      const before = previous[t]
      const positions = new Array<number>(tier.length)
      let tierChanged = !before || before.length !== tier.length
      for (let k = 0; k < tier.length; k++) {
        const index = tier[k]!
        let position: number
        if (index < start) position = estimates[k]!
        else {
          const row = index < end ? options.rowElement(index) : null
          position = row ? above + row.offsetTop : estimates[k]! + shiftBelow
        }
        positions[k] = position
        if (!tierChanged && before![k] !== position) tierChanged = true
      }
      if (!tierChanged) return before!
      changed = true
      return positions
    })
    if (changed) landmarkTiers.value = next
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
      syncLandmarkPositions(tiers, start, end, above + scrollHeight - estimatedHeightBefore(end))
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
  let pendingAlign: 'start' | 'end' = 'start'

  async function landPending() {
    if (isLanding) return
    isLanding = true
    try {
      while (pendingIndex != null) {
        const index = pendingIndex
        const align = pendingAlign
        pendingIndex = null
        await options.landAt(index, align)
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
    const count = options.count()
    const liveMax = above + viewport.scrollHeight - viewport.clientHeight
    // The track's ends are the list's REAL ends, never an estimated row near them:
    // the estimate below the window is only that, and a seek to "the last pixel"
    // through it lands rows short of the end — then re-estimates, and the thumb
    // creeps while the reader holds it there.
    const totalMax = above + viewport.scrollHeight + estimatedHeightBetween(options.windowEnd.value, count) - viewport.clientHeight
    // Top first: on a list that barely scrolls the two ends meet, and a seek to 0
    // must not land at the bottom.
    const atTop = barTop <= 0
    const atBottom = !atTop && barTop >= totalMax - 1
    if (atBottom && options.windowEnd.value >= count) {
      pendingIndex = null
      pendingPosition = null
      options.cancelLanding?.()
      viewport.scrollTop = viewport.scrollHeight
      return
    }
    if (atTop && options.windowStart.value === 0) {
      pendingIndex = null
      pendingPosition = null
      options.cancelLanding?.()
      viewport.scrollTop = 0
      return
    }
    if (!atBottom && !atTop && barTop >= above && barTop <= liveMax) {
      // A landing queued by an earlier seek outside the window is stale now: the
      // drag came back inside, and landing it afterwards would yank the view away.
      pendingIndex = null
      pendingPosition = null
      options.cancelLanding?.()
      viewport.scrollTop = barTop - above
      return
    }
    pendingIndex = atBottom ? count - 1 : atTop ? 0 : indexForPosition(barTop)
    pendingAlign = atBottom ? 'end' : 'start'
    pendingPosition = Math.round(barTop)
    position.value = pendingPosition
    void landPending()
  }

  return { totalHeight, position, landmarkTiers, syncFromViewport, seek, indexForPosition }
}
