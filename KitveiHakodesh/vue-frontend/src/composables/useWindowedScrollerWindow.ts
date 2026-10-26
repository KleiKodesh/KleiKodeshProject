/**
 * The sliding window of rows behind WindowedScroller.vue: which rows are mounted,
 * growing the window toward whichever edge the reader approaches, trimming the far
 * edge, landing on a row, and keeping the viewport still while the content above it
 * changes.
 *
 * Rows are real flow content, so every position here is a DOM measurement
 * (offsetTop within the content element) — nothing is estimated.
 *
 * Keeping the viewport still — the ANCHOR. Anything that changes the height of the
 * content above the viewport would shift what the reader is looking at: rows being
 * prepended or trimmed, a placeholder row filling in with its text, a zoom. The
 * browser's own scroll anchoring handles some of these and misses others (nothing
 * at scrollTop 0, and its choice of anchor node is its own), so the scroller owns
 * this itself and the browser's is switched off. The first visible row and its
 * offset are remembered before every change (`rememberAnchor`) and the row is put
 * back exactly where it was right after (`holdAnchor`).
 *
 * Ordering matters and is why the component calls these from its own update hooks:
 * a `scroll` event is dispatched a frame after a programmatic scroll and a
 * ResizeObserver fires after layout, so neither can be trusted to see a DOM that
 * has been corrected yet. Vue's onBeforeUpdate/onUpdated bracket the patch itself,
 * synchronously, before anything else observes the new layout.
 */
import { nextTick, ref } from 'vue'

/** Rows mounted above / below the target when the window is rebuilt around a jump. */
const ROWS_ABOVE_ON_REBUILD = 30
const ROWS_BELOW_ON_REBUILD = 90
/** Rows added per extension step. */
const EXTEND_ROWS = 40
/** Past this the far edge is trimmed. */
const MAX_WINDOW_ROWS = 320
/** Extend when the content runs out within this many pixels of the viewport (at least two screens). */
const MIN_EDGE_PX = 1200
/** Rows this far outside the viewport are kept when trimming. */
const TRIM_KEEP_PX = 400

export interface WindowedScrollerWindowOptions {
  count: () => number
  viewport: () => HTMLElement | null
  /** The mounted element of an in-window row, or null. */
  rowElement: (index: number) => HTMLElement | null
}

export interface WindowedScrollerLandingOptions {
  offsetPx?: number
  align?: 'start' | 'end'
}

export function useWindowedScrollerWindow(options: WindowedScrollerWindowOptions) {
  const windowStart = ref(0)
  const windowEnd = ref(0)
  // Window changes applied but not yet patched into the DOM. No filling until they
  // are: it would measure a layout the pending patch is about to change. A count,
  // because a landing and an extension can overlap. Cleared on the very next tick,
  // never on a timer.
  let rendersPending = 0

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

  /** The first visible row and where it was within the content, as of the last look. */
  let anchor: { index: number; offsetTop: number } | null = null

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
    if (row && index != null) anchor = { index, offsetTop: row.offsetTop }
  }

  /**
   * Call after the DOM changed: if the anchor row moved within the content, the
   * scroll follows it by the same amount, so the row stays where the reader saw it.
   */
  function holdAnchor() {
    const viewport = options.viewport()
    if (!viewport || !anchor) return
    const row = options.rowElement(anchor.index)
    if (!row) return
    const delta = row.offsetTop - anchor.offsetTop
    if (delta !== 0) {
      viewport.scrollTop += delta
      anchor.offsetTop = row.offsetTop
    }
  }

  // ── Landing ───────────────────────────────────────────────────────────────────

  /**
   * Puts the row at the top of the viewport (or its bottom at the bottom, for
   * 'end'), rebuilding the window around it first when it is not mounted. Exact:
   * the scroll is set from the row's real position once it is in the DOM.
   */
  async function landAt(index: number, landing: WindowedScrollerLandingOptions = {}): Promise<void> {
    const count = options.count()
    if (count <= 0) return
    const target = Math.max(0, Math.min(index, count - 1))
    if (!isRowInWindow(target) || !options.rowElement(target)) {
      const start = Math.max(0, target - ROWS_ABOVE_ON_REBUILD)
      windowStart.value = start
      windowEnd.value = Math.min(count, target + ROWS_BELOW_ON_REBUILD)
      // Nothing to hold through the rebuild: the view is leaving wherever it was.
      anchor = null
      rendersPending++
      await nextTick()
      rendersPending--
    }
    const viewport = options.viewport()
    const row = options.rowElement(target)
    if (!viewport || !row) return
    const offset = landing.offsetPx ?? 0
    viewport.scrollTop =
      landing.align === 'end'
        ? row.offsetTop + row.offsetHeight - viewport.clientHeight + offset
        : Math.max(0, row.offsetTop + offset)
    rememberAnchor()
    fill()
  }

  // ── Growing and trimming ──────────────────────────────────────────────────────

  function edgePx(viewport: HTMLElement) {
    return Math.max(MIN_EDGE_PX, viewport.clientHeight * 2)
  }

  /** Extends the window toward an edge the viewport is near. Call on scroll and on content resize. */
  function fill() {
    const viewport = options.viewport()
    if (!viewport || rendersPending) return
    const count = options.count()
    const edge = edgePx(viewport)
    const belowViewport = viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight
    if (windowEnd.value < count && belowViewport < edge) {
      mutateWindow(() => {
        windowEnd.value = Math.min(count, windowEnd.value + EXTEND_ROWS)
        trimTop(viewport)
      })
      return
    }
    if (windowStart.value > 0 && viewport.scrollTop < edge) {
      mutateWindow(() => {
        windowStart.value = Math.max(0, windowStart.value - EXTEND_ROWS)
        trimBottom(viewport)
      })
    }
  }

  function trimTop(viewport: HTMLElement) {
    const limit = viewport.scrollTop - TRIM_KEEP_PX
    let start = windowStart.value
    while (windowEnd.value - start > MAX_WINDOW_ROWS) {
      const row = options.rowElement(start)
      if (!row || row.offsetTop + row.offsetHeight >= limit) break
      start++
    }
    windowStart.value = start
  }

  function trimBottom(viewport: HTMLElement) {
    const limit = viewport.scrollTop + viewport.clientHeight + TRIM_KEEP_PX
    let end = windowEnd.value
    while (end - windowStart.value > MAX_WINDOW_ROWS) {
      const row = options.rowElement(end - 1)
      if (!row || row.offsetTop <= limit) break
      end--
    }
    windowEnd.value = end
  }

  /**
   * Applies a window change; the component's update hooks hold the anchor through
   * its render. Once rendered, keeps filling — one step may not have been enough (a
   * landing near the end of a short book, a zoom-out that halved every row).
   */
  function mutateWindow(apply: () => void) {
    apply()
    rendersPending++
    nextTick(() => {
      rendersPending--
      fill()
    })
  }

  return {
    windowStart,
    windowEnd,
    isRowInWindow,
    clampToCount,
    findRowIndexAt,
    rememberAnchor,
    holdAnchor,
    landAt,
    fill,
  }
}
