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
  /**
   * The [start, end) of the block holding a row. The window's top edge only ever
   * moves by whole blocks — a landing, an extension or a trim at the top never
   * starts inside one — so rows flowing together in a paragraph (flow mode, see
   * windowedScrollerFlowPlan.ts) are never prepended to and never re-wrap. Each
   * row its own block when omitted.
   */
  blockRange?: (index: number) => readonly [number, number]
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

  /** The first row of the block holding `index`. */
  function blockStartOf(index: number): number {
    return options.blockRange ? options.blockRange(index)[0] : index
  }

  /** One past the last row of the block holding `index`. */
  function blockEndOf(index: number): number {
    return options.blockRange ? options.blockRange(index)[1] : index + 1
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
  let anchor: { index: number; offsetTop: number; offsetHeight: number; scrollTop: number } | null = null
  /**
   * The reader is at the list's REAL bottom (last row mounted, viewport scrolled to
   * its end). Then the bottom is what is held, not the top row: rows above
   * re-wrapping or arriving would otherwise push the end out of view, and "End"
   * would not stay at the end.
   */
  let isPinnedToEnd = false

  function isViewportAtListEnd(viewport: HTMLElement) {
    return windowEnd.value >= options.count() && viewport.scrollTop >= viewport.scrollHeight - viewport.clientHeight - 1
  }

  /**
   * The row's top in content coordinates, measured (fractional). offsetTop is rounded:
   * holding an anchor in rounded offsets drifted a pixel whenever the rows above had
   * fractional heights, and the row above showed as a 1 px sliver at the top.
   */
  function measuredTop(viewport: HTMLElement, row: HTMLElement): number {
    return row.getBoundingClientRect().top - viewport.getBoundingClientRect().top + viewport.scrollTop
  }

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
    isPinnedToEnd = isViewportAtListEnd(viewport)
    const index = findRowIndexAt(viewport.scrollTop, false)
    const row = index != null ? options.rowElement(index) : null
    if (row && index != null) {
      // The scroll our own hold set is not the reader moving: keep the same words
      // anchored, or each hold would hand the anchor to whichever character the
      // re-wrap left at the top, and the reader's words would walk a line per step.
      const isOwnHold = viewport.scrollTop === heldScrollTop && textAnchor != null && row.contains(textAnchor.node)
      anchor = { index, offsetTop: measuredTop(viewport, row), offsetHeight: row.offsetHeight, scrollTop: viewport.scrollTop }
      if (!isOwnHold) textAnchor = findTextAnchor(viewport, row)
    }
  }

  // ── Text anchor ───────────────────────────────────────────────────────────────
  //
  // The character at the top of the viewport, and how far below the viewport's top
  // it sat. A reflow (zoom, width) re-wraps the row; holding that character's line
  // in place keeps the very words the reader was on, where a row fraction drifts.

  let textAnchor: { node: Text; offset: number; top: number } | null = null
  /** The scrollTop the last hold left, so the scroll event it causes is recognised as ours. */
  let heldScrollTop: number | null = null

  function caretAt(x: number, y: number): { node: Node; offset: number } | null {
    const doc = document as Document & {
      caretPositionFromPoint?: (x: number, y: number) => { offsetNode: Node; offset: number } | null
    }
    if (doc.caretPositionFromPoint) {
      const position = doc.caretPositionFromPoint(x, y)
      return position ? { node: position.offsetNode, offset: position.offset } : null
    }
    const range = document.caretRangeFromPoint?.(x, y)
    return range ? { node: range.startContainer, offset: range.startOffset } : null
  }

  function textTop(node: Text, offset: number): number | null {
    if (!node.isConnected || node.length === 0) return null
    const start = Math.min(offset, node.length - 1)
    const range = document.createRange()
    range.setStart(node, start)
    range.setEnd(node, start + 1)
    const rect = range.getClientRects()[0] ?? range.getBoundingClientRect()
    return rect && (rect.height > 0 || rect.width > 0) ? rect.top : null
  }

  /** The first text line that starts inside the viewport — one the reader can see, not one cut off above. */
  function findTextAnchor(viewport: HTMLElement, row: HTMLElement) {
    const box = viewport.getBoundingClientRect()
    const rowBox = row.getBoundingClientRect()
    const bottom = Math.min(box.bottom, rowBox.bottom)
    let y = Math.max(box.top, rowBox.top) + 2
    for (let probe = 0; probe < 2 && y < bottom; probe++) {
      const caret = caretAt(box.left + box.width / 2, y)
      if (!caret || caret.node.nodeType !== Node.TEXT_NODE || !row.contains(caret.node)) return null
      const node = caret.node as Text
      const top = textTop(node, caret.offset)
      if (top == null) return null
      if (top >= box.top) return { node, offset: caret.offset, top: top - box.top }
      // A line cut off at the top: try the line below it.
      const range = document.createRange()
      range.setStart(node, Math.min(caret.offset, node.length - 1))
      range.setEnd(node, Math.min(caret.offset, node.length - 1) + 1)
      y = range.getBoundingClientRect().bottom + 2
    }
    return null
  }

  /** Puts the anchored character back at its distance below the viewport's top. True when it could. */
  function holdTextAnchor(viewport: HTMLElement, row: HTMLElement): boolean {
    const text = textAnchor
    if (!text || !row.contains(text.node)) return false
    const top = textTop(text.node, text.offset)
    if (top == null) return false
    const delta = Math.round(top - viewport.getBoundingClientRect().top - text.top)
    if (delta !== 0) viewport.scrollTop += delta
    heldScrollTop = viewport.scrollTop
    return true
  }

  /**
   * Call after the DOM changed (a patch, a layout change): puts the anchor row back
   * exactly where the reader saw it. The scroll is set ABSOLUTELY, from the row's
   * new offset and the remembered distance into it — not by the row's movement. An
   * extension that trims more above than it adds below leaves the content shorter
   * for a moment, and the browser clamps the scroll during that layout; a relative
   * correction would then stack on top of the clamp and land a screen early.
   *
   * The distance into the row is kept as a FRACTION of its height: when the row
   * re-wraps (a zoom, a width change) a fixed pixel offset would point at other
   * text, and a tall row would visibly slide under the reader.
   */
  function holdAnchor() {
    const viewport = options.viewport()
    if (!viewport) return
    if (isPinnedToEnd && windowEnd.value >= options.count()) {
      viewport.scrollTop = viewport.scrollHeight
      heldScrollTop = viewport.scrollTop
      rememberAnchor()
      return
    }
    if (!anchor) return
    const row = options.rowElement(anchor.index)
    if (!row) return
    const height = row.offsetHeight
    // A re-wrapped row: hold the reader's words, not a pixel offset into the row.
    if (height !== anchor.offsetHeight && holdTextAnchor(viewport, row)) {
      anchor.offsetTop = measuredTop(viewport, row)
      anchor.offsetHeight = height
      anchor.scrollTop = viewport.scrollTop
      return
    }
    const offsetInto = anchor.scrollTop - anchor.offsetTop
    const scaled = anchor.offsetHeight > 0 && height !== anchor.offsetHeight
      ? Math.round(offsetInto * (height / anchor.offsetHeight))
      : offsetInto
    const top = measuredTop(viewport, row)
    const expected = Math.max(0, Math.round(top + scaled))
    if (viewport.scrollTop !== expected) viewport.scrollTop = expected
    heldScrollTop = viewport.scrollTop
    anchor.offsetTop = measuredTop(viewport, row)
    anchor.offsetHeight = height
    anchor.scrollTop = viewport.scrollTop
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
    const delta = Math.round(measuredTop(viewport, row) - anchor.offsetTop)
    if (delta !== 0) viewport.scrollTop += delta
  }

  /** The viewport is where our last hold left it - a scroll event now is that hold's, not a move. */
  function isHeldScrollTop(): boolean {
    const viewport = options.viewport()
    return viewport != null && heldScrollTop != null && viewport.scrollTop === heldScrollTop
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

  /** Abandons any landing in flight: it will not move the view when its rows arrive. */
  function cancelLanding() {
    landingGeneration++
  }

  /**
   * Puts the row at the top of the viewport (or its bottom at the bottom, for
   * 'end'), rebuilding the window around it first when it is not mounted — after
   * its rows have been asked for, so the one render has their content. A mounted
   * target near the window's end first gets the rows below it, so the landing has
   * text under it rather than a gap. Exact: the scroll is set from the row's real
   * position once it is in the DOM. Does nothing when the rows cannot be had.
   * Resolves true when it landed (the row mounted, the scroll set); false when it did
   * not - no rows, the rows not to be had, or cancelled / superseded by a newer landing.
   */
  async function landAt(index: number, landing: WindowedScrollerLandingOptions = {}): Promise<boolean> {
    const count = options.count()
    if (count <= 0) return false
    const target = Math.max(0, Math.min(index, count - 1))
    const generation = ++landingGeneration
    const wantEnd = Math.min(count, target + ROWS_BELOW_ON_REBUILD)
    if (!isRowInWindow(target) || !options.rowElement(target)) {
      const start = blockStartOf(Math.max(0, target - ROWS_ABOVE_ON_REBUILD))
      // Fetching the rows can shrink the count (a list whose length was an estimate
      // until its rows were read): a target now past the end lands on the new last
      // row instead of mounting an empty window beyond it — a blank screen.
      let isTargetPastEnd = false
      const applied = await prepareWindow(generation, start, wantEnd, () => {
        if (target >= options.count()) { isTargetPastEnd = true; return }
        // Nothing to hold through the rebuild: the view is leaving wherever it was.
        anchor = null
        isPinnedToEnd = false
        windowStart.value = start
        windowEnd.value = Math.min(wantEnd, options.count())
      })
      if (isTargetPastEnd) return landAt(options.count() - 1, { align: landing.align ?? 'end' })
      if (!applied) return false
    } else if (windowEnd.value < wantEnd) {
      let isTargetPastEnd = false
      const applied = await prepareWindow(generation, windowEnd.value, wantEnd, () => {
        if (target >= options.count()) { isTargetPastEnd = true; clampToCount(); return }
        windowEnd.value = Math.min(wantEnd, options.count())
        clampToCount()
      })
      if (isTargetPastEnd) return landAt(options.count() - 1, { align: landing.align ?? 'end' })
      if (!applied) return false
    }
    // Cancelled (or superseded) after the window moved: the reader moved on, but
    // the new rows still need an anchor to hold and their neighbours filled.
    if (generation !== landingGeneration) {
      rememberAnchor()
      fill()
      return false
    }
    const viewport = options.viewport()
    const row = options.rowElement(target)
    if (!viewport || !row) return false
    // A positive offset is a saved position partway down the row; one that no
    // longer fits (the row re-wrapped at another zoom or width) lands at its top
    // rather than inside the row after it.
    const requested = landing.offsetPx ?? 0
    const offset = requested > 0 && requested >= row.offsetHeight ? 0 : requested
    // offsetHeight is rounded down; a row of fractional height would be clipped by
    // that fraction at the bottom, so the end landing rounds the other way.
    // offsetTop is rounded too, and rows of fractional height put the target's real
    // top up to a pixel below it: the row above then showed as a 1 px sliver at the
    // top (read as "first visible"). Land on the measured top, to the nearest pixel.
    const viewportTop = viewport.getBoundingClientRect().top
    viewport.scrollTop =
      landing.align === 'end'
        ? Math.ceil(row.getBoundingClientRect().bottom - viewportTop + viewport.scrollTop - viewport.clientHeight + offset)
        : Math.max(0, Math.round(row.getBoundingClientRect().top - viewportTop + viewport.scrollTop + offset))
    rememberAnchor()
    fill()
    return true
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

  /**
   * The blocks changed under the mounted window (flow mode switched on, or the
   * plan's inputs — landmarks, weights — arrived after the window was built): moves
   * the window's start back to the start of its block, so the top edge again lies
   * on a block boundary. The update hooks hold the reader's words through the
   * regrouping it causes.
   */
  async function snapToBlocks(): Promise<void> {
    const startBefore = windowStart.value
    const endBefore = windowEnd.value
    const start = blockStartOf(startBefore)
    if (endBefore <= startBefore || start === startBefore) return
    changesPending++
    try {
      await options.ensureRows(start, startBefore)
    } catch {
      changesPending--
      return
    }
    if (windowStart.value === startBefore && windowEnd.value === endBefore) windowStart.value = start
    await nextTick()
    changesPending--
    fill()
  }

  // ── Growing and trimming ──────────────────────────────────────────────────────

  function edgePx(viewport: HTMLElement) {
    return Math.max(MIN_EDGE_PX, viewport.clientHeight)
  }

  /**
   * Extends the window toward an edge the viewport is near. Call on scroll and on content resize.
   * Never before the first window exists: that window is built AT the start row (an argument of
   * the scroller's creation), and growing an empty window would grow it from row 0 - the top of
   * the list painted first, then replaced.
   */
  function fill() {
    const viewport = options.viewport()
    if (!viewport || changesPending || windowEnd.value <= windowStart.value) return
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
    // Upward, the whole block the step reaches into comes along.
    const addStart = side === 'bottom' ? endBefore : blockStartOf(Math.max(0, startBefore - rows))
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

  /** Whole blocks only: a block goes once its last row is above the limit. */
  function trimTop(viewport: HTMLElement) {
    const limit = viewport.scrollTop - keepPx(viewport)
    let start = windowStart.value
    while (true) {
      const end = blockEndOf(start)
      if (end >= windowEnd.value) break
      const last = options.rowElement(end - 1)
      if (!last || last.offsetTop + last.offsetHeight >= limit) break
      start = end
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

  // ── Rows inserted or removed mid-list ─────────────────────────────────────────

  /**
   * The list changed shape in ONE place: the `removed` rows from `at` became `inserted`
   * new rows (a tree node expanded or collapsed). Call synchronously BEFORE the patch
   * that renders the new count, then remap the index-keyed row elements the same way.
   * Indexes below `at` keep their meaning; indexes from `at + removed` move by the
   * difference. The window and the anchor move with their rows, so the reader's row
   * stays pixel-still however many rows changed above it - the update hooks hold it
   * as for any other patch.
   *
   * A change overlapping the window keeps the side the reader is on: below the change,
   * the window restarts after it (fill re-grows the rows above); otherwise the window
   * keeps its top and mounts at most MAX_EXTEND_ROWS of the new rows. A reader whose
   * top row was itself removed is held on the row before the change.
   */
  function replaceRows(at: number, removed: number, inserted: number) {
    const delta = inserted - removed
    const changeEnd = at + removed
    const start = windowStart.value
    const end = windowEnd.value
    const viewport = options.viewport()
    const readerIndex = viewport && end > start ? findRowIndexAt(viewport.scrollTop, false) : null
    if (end <= at) {
      // Wholly below the window: nothing mounted moves.
    } else if (start >= changeEnd) {
      windowStart.value = start + delta
      windowEnd.value = end + delta
    } else if (readerIndex != null && readerIndex >= changeEnd) {
      windowStart.value = changeEnd + delta
      windowEnd.value = end + delta
    } else {
      windowStart.value = Math.min(start, at)
      // Few new rows: keep the mounted rows after them too. Many: mount a screenful's
      // worth and let fill grow the rest as the reader goes.
      const newEnd = inserted > MAX_EXTEND_ROWS ? at + MAX_EXTEND_ROWS : Math.max(at + inserted, end + delta)
      windowEnd.value = Math.max(windowStart.value + 1, newEnd)
    }
    // The count prop may not have caught up yet: clampToCount runs when it does.
    if (windowStart.value > windowEnd.value) windowStart.value = windowEnd.value
    textAnchor = null
    if (anchor) {
      if (anchor.index >= changeEnd) anchor.index += delta
      else if (anchor.index >= at) {
        const previous = at - 1
        const row = previous >= windowStart.value ? options.rowElement(previous) : null
        anchor = row && viewport
          ? { index: previous, offsetTop: measuredTop(viewport, row), offsetHeight: row.offsetHeight, scrollTop: viewport.scrollTop }
          : null
      }
    }
  }

  return {
    windowStart,
    windowEnd,
    isRowInWindow,
    clampToCount,
    replaceRows,
    findRowIndexAt,
    rememberAnchor,
    holdAnchor,
    holdAnchorThroughScroll,
    isHeldScrollTop,
    landAt,
    cancelLanding,
    refreshRows,
    snapToBlocks,
    fill,
  }
}
