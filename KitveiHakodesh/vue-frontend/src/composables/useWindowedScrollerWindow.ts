/**
 * The sliding window of rows behind WindowedScroller.vue: which rows are mounted,
 * growing the window toward whichever edge the reader approaches, trimming the far
 * edge, landing on a row, and keeping the viewport still while the content above it
 * changes.
 *
 * Rows are real flow content, so every position here is a DOM measurement
 * (offsetTop within the content element) — nothing is estimated.
 *
 * Rows are asked for before they are mounted (`ensureRows`), so a row is never on
 * screen without its content: no placeholders, and a landing renders once.
 *
 * Keeping the viewport still — the ANCHOR. Anything that changes the height of the
 * content above the viewport would shift what the reader is looking at: rows being
 * prepended or trimmed, a zoom, a font arriving. The browser's own scroll anchoring
 * handles some of these and misses others (nothing at scrollTop 0, and its choice of
 * anchor node is its own), so the scroller owns this itself and the browser's is
 * switched off. The first visible row and its offset are remembered before every
 * change (`rememberAnchor`) and the row is put back exactly where it was right after
 * (`holdAnchor`).
 *
 * Ordering matters and is why the component calls these from its own update hooks:
 * a `scroll` event is dispatched a frame after a programmatic scroll and a
 * ResizeObserver fires after layout, so neither can be trusted to see a DOM that
 * has been corrected yet. Vue's onBeforeUpdate/onUpdated bracket the patch itself,
 * synchronously, before anything else observes the new layout.
 */
import { nextTick, ref } from 'vue'

/** Rows mounted above / below the target when the window is rebuilt around a jump. */
const ROWS_ABOVE_ON_REBUILD = 12
const ROWS_BELOW_ON_REBUILD = 36
/**
 * Rows added per extension step — small, so a step is a cheap patch. A fast scroll
 * that has already eaten into the edge gets more: enough rows to cover the shortfall
 * at the mounted rows' average height, so the window keeps ahead of a fling instead
 * of the native scroll clamping at the end of the mounted content.
 */
const EXTEND_ROWS = 12
const MAX_EXTEND_ROWS = 120
/** Extend when the content runs out within this many pixels of the viewport (at least a screen). */
const MIN_EDGE_PX = 600
/**
 * Rows this far outside the viewport are kept when the far side is trimmed, as a
 * multiple of the extension edge. MUST exceed 1: trimming below the edge would put
 * that side straight back under its own extension threshold, and the two sides
 * would extend and trim each other forever. Mounted rows are what every reflow (a
 * resize, a zoom) has to lay out again, so this stays as small as that allows.
 */
const TRIM_KEEP_EDGES = 1.5

export interface WindowedScrollerWindowOptions {
  count: () => number
  viewport: () => HTMLElement | null
  /** The mounted element of an in-window row, or null. */
  rowElement: (index: number) => HTMLElement | null
  /** Resolves once the rows of [start, end) can be rendered; rejects when they cannot be. */
  ensureRows: (start: number, end: number) => Promise<void>
}

export interface WindowedScrollerLandingOptions {
  offsetPx?: number
  align?: 'start' | 'end'
}

export function useWindowedScrollerWindow(options: WindowedScrollerWindowOptions) {
  const windowStart = ref(0)
  const windowEnd = ref(0)
  // Window changes being prepared (rows requested) or applied but not yet patched
  // into the DOM. No filling until they are done: it would measure a layout that is
  // about to change. A count, because a landing and an extension can overlap.
  // Cleared on the very next tick after the patch, never on a timer.
  let changesPending = 0

  function isRowInWindow(index: number) {
    return index >= windowStart.value && index < windowEnd.value
  }

  /** The count shrank (a book change): drop rows that no longer exist. */
  function clampToCount() {
    const count = options.count()
    if (windowEnd.value > count) windowEnd.value = count
    if (windowStart.value > windowEnd.value) windowStart.value = windowEnd.value
  }

  /**
   * Binary search over the mounted window, which is in index order and therefore in
   * position order. `fully` wants the first row starting at or below `top`; otherwise
   * the first row with any part below it.
   */
  function findRowIndexAt(top: number, fully: boolean): number | null {
    let low = windowStart.value
    let high = windowEnd.value - 1
    if (high < low) return null
    while (low < high) {
      const middle = (low + high) >> 1
      const row = options.rowElement(middle)
      if (!row) return null
      const rowIsAbove = fully ? row.offsetTop < top : row.offsetTop + row.offsetHeight <= top
      if (rowIsAbove) low = middle + 1
      else high = middle
    }
    return options.rowElement(low) ? low : null
  }

  // ── Anchor ────────────────────────────────────────────────────────────────────

  /** The first visible row, where it was within the content, and where the viewport was, as of the last look. */
  let anchor: { index: number; offsetTop: number; scrollTop: number } | null = null

  /**
   * The reader's row is wherever the viewport is now. Call before the DOM changes.
   *
   * Keeps the previous anchor when no row can be found: before a patch the window
   * range already names rows that are not in the DOM yet, so the lookup can fail
   * while the previous anchor's row is still right there to be held.
   */
  function rememberAnchor() {
    const viewport = options.viewport()
    if (!viewport) return
    const index = findRowIndexAt(viewport.scrollTop, false)
    const row = index != null ? options.rowElement(index) : null
    if (row && index != null) anchor = { index, offsetTop: row.offsetTop, scrollTop: viewport.scrollTop }
  }

  /**
   * Call after the DOM changed (a patch, a layout change): puts the anchor row back
   * exactly where the reader saw it. The scroll is set ABSOLUTELY, from the row's
   * new offset and the remembered distance into it — not by the row's movement. An
   * extension that trims more above than it adds below leaves the content shorter
   * for a moment, and the browser clamps the scroll during that layout; a relative
   * correction would then stack on top of the clamp and land a screen early.
   */
  function holdAnchor() {
    const viewport = options.viewport()
    if (!viewport || !anchor) return
    const row = options.rowElement(anchor.index)
    if (!row) return
    const expected = row.offsetTop + (anchor.scrollTop - anchor.offsetTop)
    if (viewport.scrollTop !== expected) {
      viewport.scrollTop = expected
      anchor.offsetTop = row.offsetTop
      anchor.scrollTop = viewport.scrollTop
    }
  }

  /**
   * On a scroll event, before the new position is remembered: the reader's scroll
   * is real and must be kept, so only a shift of the anchor row within the content
   * (a layout change that landed between the scroll and the observer that would
   * hold it) is corrected, relatively.
   */
  function holdAnchorThroughScroll() {
    const viewport = options.viewport()
    if (!viewport || !anchor) return
    const row = options.rowElement(anchor.index)
    if (!row) return
    const delta = row.offsetTop - anchor.offsetTop
    if (delta !== 0) viewport.scrollTop += delta
  }

  // ── Landing ───────────────────────────────────────────────────────────────────

  // Bumped by every landing. A landing whose rows arrive after a newer one started
  // must not apply: two jumps in flight would otherwise resolve in fetch order, and
  // the earlier one would yank the view away from where the reader just went.
  let landingGeneration = 0

  /**
   * Asks for rows, then applies a window change and waits for its render. False —
   * and nothing applied — when the rows are unavailable or a newer landing has
   * superseded this one.
   */
  async function prepareWindow(generation: number, start: number, end: number, apply: () => void): Promise<boolean> {
    changesPending++
    try {
      await options.ensureRows(start, end)
    } catch {
      changesPending--
      return false
    }
    if (generation !== landingGeneration) {
      changesPending--
      return false
    }
    apply()
    await nextTick()
    changesPending--
    return true
  }

  /**
   * Puts the row at the top of the viewport (or its bottom at the bottom, for
   * 'end'), rebuilding the window around it first when it is not mounted — after
   * its rows have been asked for, so the one render has their content. A mounted
   * target near the window's end first gets the rows below it, so the landing has
   * text under it rather than a gap. Exact: the scroll is set from the row's real
   * position once it is in the DOM. Does nothing when the rows cannot be had.
   */
  async function landAt(index: number, landing: WindowedScrollerLandingOptions = {}): Promise<void> {
    const count = options.count()
    if (count <= 0) return
    const target = Math.max(0, Math.min(index, count - 1))
    const generation = ++landingGeneration
    const wantEnd = Math.min(count, target + ROWS_BELOW_ON_REBUILD)
    if (!isRowInWindow(target) || !options.rowElement(target)) {
      const start = Math.max(0, target - ROWS_ABOVE_ON_REBUILD)
      const applied = await prepareWindow(generation, start, wantEnd, () => {
        // Nothing to hold through the rebuild: the view is leaving wherever it was.
        anchor = null
        windowStart.value = start
        windowEnd.value = wantEnd
      })
      if (!applied) return
    } else if (windowEnd.value < wantEnd) {
      const applied = await prepareWindow(generation, windowEnd.value, wantEnd, () => {
        windowEnd.value = wantEnd
      })
      if (!applied) return
    }
    const viewport = options.viewport()
    const row = options.rowElement(target)
    if (!viewport || !row) return
    // A positive offset is a saved position partway down the row; one that no
    // longer fits (the row re-wrapped at another zoom or width) lands at its top
    // rather than inside the row after it.
    const requested = landing.offsetPx ?? 0
    const offset = requested > 0 && requested >= row.offsetHeight ? 0 : requested
    // offsetHeight is rounded down; a row of fractional height would be clipped by
    // that fraction at the bottom, so the end landing rounds the other way.
    viewport.scrollTop =
      landing.align === 'end'
        ? Math.ceil(row.getBoundingClientRect().bottom - viewport.getBoundingClientRect().top + viewport.scrollTop - viewport.clientHeight + offset)
        : Math.max(0, row.offsetTop + offset)
    rememberAnchor()
    fill()
  }

  /**
   * The row source changed under the mounted window (a text version swap): asks
   * for the window's rows again. The rows re-render as their new content arrives,
   * and the update hooks hold the reader's row through the height changes.
   */
  async function refreshRows(): Promise<void> {
    if (windowEnd.value <= windowStart.value) return
    try {
      await options.ensureRows(windowStart.value, windowEnd.value)
    } catch {
      // Unavailable for now; the next extension or landing asks again.
    }
  }

  // ── Growing and trimming ──────────────────────────────────────────────────────

  function edgePx(viewport: HTMLElement) {
    return Math.max(MIN_EDGE_PX, viewport.clientHeight)
  }

  /** Extends the window toward an edge the viewport is near. Call on scroll and on content resize. */
  function fill() {
    const viewport = options.viewport()
    if (!viewport || changesPending) return
    const count = options.count()
    const edge = edgePx(viewport)
    const belowViewport = viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight
    if (windowEnd.value < count && belowViewport < edge) {
      void extend('bottom', rowsToExtend(viewport, edge - belowViewport))
    } else if (windowStart.value > 0 && viewport.scrollTop < edge) {
      void extend('top', rowsToExtend(viewport, edge - viewport.scrollTop))
    }
  }

  /** EXTEND_ROWS plus whatever covers `shortfallPx` of edge already lost, at the mounted average. */
  function rowsToExtend(viewport: HTMLElement, shortfallPx: number): number {
    const mounted = windowEnd.value - windowStart.value
    if (mounted <= 0 || shortfallPx <= 0) return EXTEND_ROWS
    const averageRowPx = viewport.scrollHeight / mounted
    return Math.min(MAX_EXTEND_ROWS, EXTEND_ROWS + Math.ceil(shortfallPx / Math.max(1, averageRowPx)))
  }

  /**
   * Asks for the next rows on one side, then mounts them and trims the far side.
   * The component's update hooks hold the anchor through the render. Dropped if a
   * landing moved the window meanwhile, or if the rows cannot be had — the next
   * scroll asks again, so a failing source costs one request per reader scroll,
   * never a loop.
   */
  async function extend(side: 'top' | 'bottom', rows: number) {
    const startBefore = windowStart.value
    const endBefore = windowEnd.value
    const addStart = side === 'bottom' ? endBefore : Math.max(0, startBefore - rows)
    const addEnd = side === 'bottom' ? Math.min(options.count(), endBefore + rows) : startBefore
    changesPending++
    try {
      await options.ensureRows(addStart, addEnd)
    } catch {
      changesPending--
      return
    }
    const viewport = options.viewport()
    if (!viewport || windowStart.value !== startBefore || windowEnd.value !== endBefore) {
      changesPending--
      return
    }
    const heightBefore = viewport.scrollHeight
    if (side === 'bottom') {
      windowEnd.value = addEnd
      trimTop(viewport)
    } else {
      windowStart.value = addStart
      trimBottom(viewport)
    }
    await nextTick()
    changesPending--
    // Rows that added no height would be asked for again at once, forever.
    if (viewport.scrollHeight === heightBefore) return
    // One step may not have been enough (a landing near the end of a short book, a
    // zoom-out that halved every row).
    fill()
  }

  function keepPx(viewport: HTMLElement) {
    return edgePx(viewport) * TRIM_KEEP_EDGES
  }

  function trimTop(viewport: HTMLElement) {
    const limit = viewport.scrollTop - keepPx(viewport)
    let start = windowStart.value
    while (start < windowEnd.value - 1) {
      const row = options.rowElement(start)
      if (!row || row.offsetTop + row.offsetHeight >= limit) break
      start++
    }
    windowStart.value = start
  }

  function trimBottom(viewport: HTMLElement) {
    const limit = viewport.scrollTop + viewport.clientHeight + keepPx(viewport)
    let end = windowEnd.value
    while (end > windowStart.value + 1) {
      const row = options.rowElement(end - 1)
      if (!row || row.offsetTop <= limit) break
      end--
    }
    windowEnd.value = end
  }

  return {
    windowStart,
    windowEnd,
    isRowInWindow,
    clampToCount,
    findRowIndexAt,
    rememberAnchor,
    holdAnchor,
    holdAnchorThroughScroll,
    landAt,
    refreshRows,
    fill,
  }
}
