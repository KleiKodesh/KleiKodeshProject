<script setup lang="ts">
/**
 * A long-list scroller that mounts only a sliding WINDOW of rows, in normal document
 * flow, with its own index-based scrollbar beside the viewport.
 *
 * THE MODEL — read this before changing anything here or in a caller:
 *
 *   1. The scroller shows "the rows from index N". That is all it knows. The top of
 *      the list is not special: row 0 and row 5000 are the same kind of window.
 *   2. So WHERE TO OPEN IS AN ARGUMENT (`initialIndex`, `initialOffsetPx`), given
 *      when the scroller is created. The first window is built there. The list is
 *      never shown at the top first and then moved.
 *   3. A caller that does not know its start row yet does not create the scroller
 *      yet (`v-if`). The scroller never waits for a position to "arrive".
 *   4. Rows that do not exist yet are the ROW SOURCE's business, not the caller's: the
 *      scroller asks `ensureRows` for its start row like for any other, and a list
 *      still streaming in answers when the row has arrived. A caller never holds the
 *      scroller back "until the row is loaded".
 *   5. Going elsewhere later is `scrollToIndex`: it builds the window at that row.
 *      Exact, one render, resolves when the row is in the DOM.
 *   6. Therefore there is no opening phase, no settling, no retry, no "did the
 *      reader move first?" guard — in the scroller or around it. Code of that kind
 *      is a leftover of the virtualizer we used before; delete it, do not extend it.
 *
 * Why not a virtualizer: a virtualizer positions every row absolutely from an
 * ESTIMATED height and corrects as rows get measured, so a jump lands on a guess and
 * then drifts while the real heights arrive — every consumer ends up with a settle
 * loop, and opening at a row means "mount at the top, then scroll". Here rows are
 * real flow content: a window is built at its target and the scroll is set from the
 * row's measured position, exactly and once. When rows above the viewport change
 * height (a zoom, a font swap, the window growing) the reader's row is held in
 * place — see useWindowedScrollerWindow.
 *
 * The viewport is a native scroll container with its own scrollbar hidden; wheel,
 * touch, PageUp/PageDown and the arrows are the browser's own. Only Home/End need
 * help, as they must reach rows that are not mounted. The scrollbar the reader sees
 * is drawn beside the viewport in the native bar's image and represents the whole
 * list: estimated heights outside the window, real ones inside it
 * (WindowedScrollerBar.vue, useWindowedScrollerBarMapping).
 *
 * Rows are asked for (`ensureRows`) before a range is mounted, so nothing is ever
 * rendered as a placeholder.
 *
 * `pinnedIndexes` keeps rows mounted outside the window, out of flow and clipped —
 * the two ends of a text selection, so a native Range keeps its boundary text nodes.
 *
 * FLOW MODE (`mode="flow"`): rows run on as spans of one paragraph instead of a
 * block each, the stretch between two landmarks being the paragraph. Prepending
 * words to a paragraph would re-wrap the lines on screen, so the window's top edge
 * only ever moves by whole paragraphs — never seams, never a re-wrap. A stretch
 * too long to be one paragraph keeps a block per row: windowedScrollerFlowPlan.ts.
 * The caller's `rowKind` can still make a row within a stretch a block (an empty
 * line, a centred one), and its slot renders inline content for an `inline` row.
 *
 * Attributes fall through to the viewport (`tabindex`, data-*, style, native
 * listeners), so the caller treats it as the scroll element it is.
 */
import { computed, nextTick, onBeforeUnmount, onBeforeUpdate, onUpdated, ref, watch, type ComponentPublicInstance } from 'vue'
import { useResizeObserver } from '@vueuse/core'
import WindowedScrollerBar from './WindowedScrollerBar.vue'
import LoadingAnimation from './LoadingAnimation.vue'
import { useWindowedScrollerWindow } from '@/composables/useWindowedScrollerWindow'
import { useWindowedScrollerBarMapping } from '@/composables/useWindowedScrollerBarMapping'
import { buildFlowPlan, ROWS_PLAN } from './windowedScrollerFlowPlan'
import type {
  WindowedScrollerBarTarget,
  WindowedScrollerMoveSource,
  WindowedScrollerRowKind,
  WindowedScrollerScrollToOptions,
  WindowedScrollerVisibleRow,
} from './windowedScrollerTypes'

defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<{
    count: number
    pinnedIndexes?: number[]
    /**
     * The row the FIRST window is built at (0 when omitted). An argument of the
     * scroller's creation, read once at setup and never watched: create the scroller
     * only once the start row is known (`v-if`), and re-key it to open elsewhere from
     * scratch. Jumps within a live scroller go through scrollToIndex. See "The first
     * window" below.
     */
    initialIndex?: number
    /** Pixels into the start row — a saved offset. Read once, with `initialIndex`. */
    initialOffsetPx?: number
    /**
     * Resolves once the rows of [start, end) can be rendered. The scroller asks
     * before it mounts a range — the first window, a jump or an extension — so a row
     * never appears without its content and a window renders exactly once. Asked for
     * rows that do not exist YET (a list still streaming in, asked for its start row),
     * it resolves when they do, or when it is known they never will. Omit when the
     * rows are always available.
     */
    ensureRows?: (start: number, end: number) => Promise<void>
    /**
     * Changes when the rows' CONTENT changes under the same indexes (a text
     * version swap). The mounted window is asked for again, and each row
     * re-renders as its new content arrives, held in place.
     */
    rowSourceVersion?: unknown
    /**
     * Row indexes where the list's sections start, in tiers from coarse to fine (a
     * book's TOC levels). The scrollbar gives each section start a minimum span of
     * track so it can be pointed at, taking as many tiers as fit — see
     * windowedScrollerTrackMap.ts.
     */
    landmarkTiers?: number[][]
    /**
     * A size weight per row — a text line's character count — when the list has
     * them. Gives the scrollbar a STATIC per-row height estimate (weight over the
     * measured weight-per-visual-line) instead of one moving average, so the thumb
     * and a drag's landing reflect where a row really is. Must be `count` long to
     * be used; otherwise, or when omitted, the bar estimates from measured rows.
     */
    rowWeights?: ArrayLike<number>
    /** `flow` runs rows on as paragraphs — see the file comment. `rows` (default) is a block per row. */
    mode?: 'rows' | 'flow'
    /**
     * Flow mode: a row's own kind, read for mounted rows only (it may need the
     * row's content). `block` takes a row out of its paragraph; `inline` (default)
     * lets it flow. Rows outside a flowing stretch are blocks regardless.
     */
    rowKind?: (index: number) => WindowedScrollerRowKind
    /** Flow mode: class(es) for the paragraph element holding a run of inline rows — the caller's column look. */
    paragraphClass?: string
    /** The drawn bar in the app's thin style (`.toc-thin-scroll`): narrower gutter, thumb and arrows. */
    thin?: boolean
  }>(),
  { pinnedIndexes: () => [], mode: 'rows' },
)

const emit = defineEmits<{
  /**
   * The first window is on screen, at the start row. Emitted once per scroller,
   * whatever the start row (0 included). Emitted even if nothing could be mounted
   * (its rows could not be had, or a jump superseded it), so listeners null-check
   * what they touch.
   */
  opened: []
  /**
   * The scroller has stopped moving. Each way it moves ends in exactly one known
   * place: a native scroll (wheel, touch, keys) at the viewport's `scrollend`; one of
   * its own landings (a jump, Home/End, a far bar seek) when the landing finishes -
   * even when it swapped the rows without changing scrollTop, which fires nothing
   * native; a held bar (drag, track hold) at release.
   *
   * `source` says who moved it: 'reader' (wheel, touch, keys incl. Home/End, the
   * bar and its far seeks) or 'landing' (a landing code asked for: scrollToIndex,
   * the first window).
   */
  moveend: [source: WindowedScrollerMoveSource]
}>()

const viewportElement = ref<HTMLElement | null>(null)
const contentElement = ref<HTMLElement | null>(null)
const barReference = ref<InstanceType<typeof WindowedScrollerBar> | null>(null)

const rowElements = new Map<number, HTMLElement>()
function setRowElement(index: number, element: Element | ComponentPublicInstance | null) {
  if (element) rowElements.set(index, element as HTMLElement)
  else rowElements.delete(index)
}
function rowElement(index: number): HTMLElement | null {
  return rowElements.get(index) ?? null
}

// The block plan: which rows flow and where the blocks are. Rebuilt only when
// the list changes shape — the window composable and the bar read it per call.
const flowPlan = computed(() =>
  props.mode === 'flow' ? buildFlowPlan(props.count, props.landmarkTiers ?? [], props.rowWeights) : ROWS_PLAN,
)

function rowKind(index: number): WindowedScrollerRowKind {
  if (!flowPlan.value.isFlowRow(index)) return 'block'
  return props.rowKind?.(index) ?? 'inline'
}

const {
  windowStart,
  windowEnd,
  isRowInWindow,
  clampToCount,
  replaceRows: replaceWindowRows,
  findRowIndexAt,
  rememberAnchor,
  holdAnchor,
  holdAnchorThroughScroll,
  landAt,
  cancelLanding,
  refreshRows,
  snapToBlocks,
  fill,
} = useWindowedScrollerWindow({
  count: () => props.count,
  viewport: () => viewportElement.value,
  rowElement,
  ensureRows: (start, end) => props.ensureRows?.(start, end) ?? Promise.resolve(),
  blockRange: (index) => flowPlan.value.blockRange(index),
})

const renderedRange = computed(() => ({ start: windowStart.value, end: windowEnd.value }))

/**
 * Everything mounted, as DOM groups in index order: a block row stands alone; a run
 * of inline rows shares one paragraph element. ONE keyed list, pinned rows included,
 * so a row trimmed out of the window but pinned keeps its very element (and the
 * text node a selection's Range sits on): a block row by its index, a flowing row
 * inside a pinned paragraph keyed like the paragraph it left.
 *
 * A paragraph is keyed by its first row. The window's top moves by whole blocks,
 * so that row never changes while the paragraph is mounted: it keeps its spans as
 * it grows at its end, and a paragraph prepended above is a new element — nothing
 * mounted ever re-wraps. A pinned paragraph takes the key the paragraph it left
 * had: the first row of its run, found by walking back over the inline rows.
 */
/** The first row of the inline run holding `index`, within its block. */
function runStartOf(index: number, block: number): number {
  let start = index
  while (start > block && rowKind(start - 1) === 'inline') start--
  return start
}

type RenderedGroup =
  | { kind: 'block'; key: string; index: number; pinned: boolean }
  | { kind: 'flow'; key: string; block: number; indexes: number[]; pinned: boolean }
const renderedGroups = computed<RenderedGroup[]>(() => {
  const start = windowStart.value
  const end = windowEnd.value
  const pinned = [...new Set(props.pinnedIndexes)]
    .filter((index) => index >= 0 && index < props.count && (index < start || index >= end))
    .sort((first, second) => first - second)
  const groups: RenderedGroup[] = []
  const usedKeys = new Set<string>()
  let run: Extract<RenderedGroup, { kind: 'flow' }> | null = null
  const add = (index: number, isPinned: boolean) => {
    if (rowKind(index) !== 'inline') {
      run = null
      groups.push({ kind: 'block', key: `r${index}`, index, pinned: isPinned })
      return
    }
    const block = flowPlan.value.blockRange(index)[0]
    if (run && run.pinned === isPinned && run.block === block) {
      run.indexes.push(index)
      return
    }
    let key = `p${isPinned ? runStartOf(index, block) : index}`
    // The window's bottom may end inside a block: a pinned row further down it
    // would take the key of the window's own last paragraph.
    if (usedKeys.has(key)) key += '-pinned'
    usedKeys.add(key)
    groups.push((run = { kind: 'flow', key, block, indexes: [index], pinned: isPinned }))
  }
  for (const index of pinned) if (index < start) add(index, true)
  run = null
  for (let index = start; index < end; index++) add(index, false)
  run = null
  for (const index of pinned) if (index >= end) add(index, true)
  return groups
})

// Every patch of this component — the window changing, a row's slot content
// changing height, a zoom arriving through attrs — is bracketed so the reader's row
// ends up exactly where it was. Synchronous around the patch, so nothing (scroll
// events, resize observers, paint) ever sees an un-held layout.
onBeforeUpdate(rememberAnchor)
onUpdated(() => {
  holdAnchor()
  bar.syncFromViewport()
})

// ── The scrollbar ──────────────────────────────────────────────────────────────

const bar = useWindowedScrollerBarMapping({
  count: () => props.count,
  viewport: () => viewportElement.value,
  windowStart,
  windowEnd,
  rowElement,
  findRowIndexAt,
  landAt: (index, align) => landOn(index, { align }, true),
  cancelLanding,
  landmarkTiers: () => props.landmarkTiers ?? [],
  rowWeights: () => props.rowWeights,
  flowPlan: () => flowPlan.value,
})

// ── Track hold: page to the destination named at the press ─────────────────────
//
// The hover label names the row under the pointer; pressing there and holding must
// end on THAT row. Paging toward the pointer's pixel does not: the thumb reaching
// the pointer is not that row, and each landing re-estimates the heights so the
// pixel names another row anyway. So the row is locked at the press, paged toward,
// landed on exactly once in reach — and reported as the bar's target meanwhile, so
// the label keeps naming it.

const PAGE_FRACTION = 0.875
let heldTargetIndex: number | null = null
let heldTargetReached = false

function onTrackHold(position: number) {
  heldTargetIndex = Math.min(props.count - 1, Math.max(0, bar.indexForPosition(position)))
  heldTargetReached = false
}

function onTrackStep() {
  const target = heldTargetIndex
  const viewport = viewportElement.value
  if (target == null || heldTargetReached || !viewport) return
  const page = viewport.clientHeight * PAGE_FRACTION
  const row = isRowRendered(target) ? rowElement(target) : null
  // Beyond the mounted window, paging a screen per tick takes minutes on a long
  // list (measured: ~170 rows/s against a target 30k rows away): land on it.
  if (!row || Math.abs(row.offsetTop - viewport.scrollTop) <= page) {
    heldTargetReached = true
    void scrollToIndex(target)
    return
  }
  const current = readFirstVisible()?.index ?? bar.indexForPosition(bar.position.value)
  if (current === target) { heldTargetReached = true; return }
  const maxPosition = Math.max(0, bar.totalHeight.value - viewport.clientHeight)
  const direction = target > current ? 1 : -1
  bar.seek(Math.min(maxPosition, Math.max(0, bar.position.value + direction * page)))
}

function onTrackRelease() {
  heldTargetIndex = null
}

function barTargetAt(clientY: number): WindowedScrollerBarTarget | null {
  if (heldTargetIndex != null) return { index: heldTargetIndex, onThumb: false }
  const target = barReference.value?.targetAt(clientY)
  if (!target) return null
  return { index: bar.indexForPosition(target.position), onThumb: target.onThumb }
}

// Hold BEFORE remembering: a layout change the scroller did not make (a font
// swap re-wrapping rows above) can land between a scroll and the resize observer
// that would hold it, and the scroll event is dispatched first. Holding first
// corrects any such shift against the last anchor; on a plain reader scroll the
// anchor row has not moved within the content and the hold is a no-op.
function onViewportScroll() {
  holdAnchorThroughScroll()
  rememberAnchor()
  bar.syncFromViewport()
  fill()
}

// ── Move end ─────────────────────────────────────────────────────────────────────
// A native scrollend inside one of our own landings or a bar hold is part of that
// move, not its end: the landing / the release reports it. A landing that moved
// scrollTop gets its native scrollend a frame after it finishes - that scrollend is
// the landing's end, so it is reported as the landing's, not as a reader scroll.
let landingsInFlight = 0
let isBarPressed = false
let landingScrollEndOwed: WindowedScrollerMoveSource | null = null
function onViewportScrollEnd() {
  if (landingsInFlight > 0 || isBarPressed) return
  const source = landingScrollEndOwed ?? 'reader'
  landingScrollEndOwed = null
  emit('moveend', source)
}
function onBarPointerDown() {
  isBarPressed = true
  window.addEventListener('pointerup', onBarPointerUp, { capture: true })
  window.addEventListener('pointercancel', onBarPointerUp, { capture: true })
}
function onBarPointerUp() {
  window.removeEventListener('pointerup', onBarPointerUp, { capture: true })
  window.removeEventListener('pointercancel', onBarPointerUp, { capture: true })
  if (!isBarPressed) return
  isBarPressed = false
  landingScrollEndOwed = null
  if (landingsInFlight === 0) emit('moveend', 'reader')
}
onBeforeUnmount(onBarPointerUp)

/** Runs one of the scroller's own landings and reports its end (see moveend). */
async function runLanding(land: () => Promise<void>, byReader = false): Promise<void> {
  const viewport = viewportElement.value
  const before = viewport?.scrollTop
  const source: WindowedScrollerMoveSource = byReader || isBarPressed ? 'reader' : 'landing'
  landingsInFlight++
  try {
    await land()
  } finally {
    landingsInFlight--
  }
  if (landingsInFlight > 0 || isBarPressed) return
  if (viewport && viewport.scrollTop !== before) landingScrollEndOwed = source
  else emit('moveend', source)
}

/** One of the scroller's own landings is under way (or its scroll has not ended yet). */
function isLanding(): boolean {
  return landingsInFlight > 0 || landingScrollEndOwed != null
}

// Layout changes that are not this component's own patch: a width change re-wrapping
// every row, a font arriving. Held against the last remembered anchor.
useResizeObserver(contentElement, () => {
  holdAnchor()
  fill()
  bar.syncFromViewport()
})
useResizeObserver(viewportElement, () => fill())

// ── The first window ───────────────────────────────────────────────────────────
//
// THE START ROW IS AN ARGUMENT, NOT A SCROLL. A windowed scroller only ever shows
// "the rows from index N"; row 0 and row 5000 are the same thing to it. So opening
// in the middle of the list is not a step that follows opening at the top: the
// first window is simply built at `initialIndex` (0 when omitted), through the same
// code that builds any window. Nothing is rendered before it, so there is nothing
// to scroll away from, nothing to wait for and nothing reader input could cancel.
//
// Hence `initialIndex` / `initialOffsetPx` are read ONCE, here. A caller that does
// not know its start row yet (a saved position still being read) must not create
// the scroller yet. Do NOT make these props reactive, and do NOT add a "has the
// reader moved?" guard: both belong to the old virtualizer model (mount at the
// top, scroll to the row afterwards), where a late scroll had to be protected from
// the reader. That guard once cancelled the opening on the auto-repeating keydown
// of a Ctrl still held from the Ctrl+click that opened the tab.
//
// The scroller asks its ROW SOURCE for the start row, like for any other row, and
// builds the first window when the source answers. A list whose rows are still
// arriving (search results streaming in) is not the caller's problem to hold the
// scroller back for: its `ensureRows` simply resolves once the start row exists —
// or once it is known that it never will, and then the window is built at the last
// row there is. The caller never waits "until the row is loaded" before creating
// the scroller, and never moves it there afterwards.
const startIndex = props.initialIndex ?? 0
const startOffsetPx = props.initialOffsetPx ?? 0
/** The row source has answered for the start row. Row 0 needs no asking: any rows at all will do. */
let isStartRowAnswered = startIndex === 0
let isFirstWindowBuilt = false

// A scroller created deep in a list shows nothing until its start row has arrived
// (there is no "top of the list meanwhile" to show). When that takes long — a slow
// source, results still streaming toward the row — say so, rather than sit blank.
// After a delay, so the usual open never flashes it. Changes twice in a scroller's
// life, so unwrapping it in the template costs the rows nothing.
const OPENING_INDICATOR_DELAY_MS = 400
const isOpeningSlow = ref(false)
let openingIndicatorTimer: ReturnType<typeof setTimeout> | null =
  startIndex > 0 ? setTimeout(() => { isOpeningSlow.value = true }, OPENING_INDICATOR_DELAY_MS) : null
function endOpeningIndicator() {
  if (openingIndicatorTimer != null) { clearTimeout(openingIndicatorTimer); openingIndicatorTimer = null }
  isOpeningSlow.value = false
}
onBeforeUnmount(endOpeningIndicator)

// Set once the list has emptied after its first window: the rows that come back are
// a new list (a search re-run clears its results, then streams them again), opened at the top.
let reopensAtTop = false

function buildFirstWindowWhenPossible() {
  if (isFirstWindowBuilt || !isStartRowAnswered || props.count <= 0) return
  isFirstWindowBuilt = true
  const [index, offsetPx] = reopensAtTop ? [0, 0] : [startIndex, startOffsetPx]
  void runLanding(() => landAt(index, { offsetPx })).then(() => {
    endOpeningIndicator()
    bar.syncFromViewport()
    emit('opened')
  })
}

if (!isStartRowAnswered) {
  void Promise.resolve()
    .then(() => props.ensureRows?.(startIndex, startIndex + 1))
    .catch(() => { /* not to be had: the window is built at the nearest row there is */ })
    .then(() => {
      isStartRowAnswered = true
      buildFirstWindowWhenPossible()
    })
}

watch(
  () => props.count,
  () => {
    clampToCount()
    // An emptied list has no window left to grow from: fill() never mounts rows into
    // an empty window, so without this the rows that arrive next stay blank. Drop the
    // first window (and any landing still in flight toward it) and build it again.
    if (props.count <= 0 && isFirstWindowBuilt) {
      isFirstWindowBuilt = false
      reopensAtTop = true
      cancelLanding()
      return
    }
    if (isFirstWindowBuilt) nextTick(fill)
    else buildFirstWindowWhenPossible()
  },
  { immediate: true },
)

watch(() => props.rowSourceVersion, () => { void refreshRows() })
watch(flowPlan, () => { void snapToBlocks() })

// ── Public API ─────────────────────────────────────────────────────────────────

function isRowRendered(index: number): boolean {
  return rowElements.has(index) && isRowInWindow(index)
}

function isRowFullyVisible(index: number): boolean {
  const viewport = viewportElement.value
  const row = isRowRendered(index) ? rowElement(index) : null
  if (!viewport || !row) return false
  return row.offsetTop >= viewport.scrollTop && row.offsetTop + row.offsetHeight <= viewport.scrollTop + viewport.clientHeight
}

function readFirstVisible(topInsetPx = 0, fully = false): WindowedScrollerVisibleRow | null {
  const viewport = viewportElement.value
  if (!viewport) return null
  const index = findRowIndexAt(viewport.scrollTop + topInsetPx, fully)
  const row = index != null ? rowElement(index) : null
  if (index == null || !row) return null
  return { index, offsetWithinRow: viewport.scrollTop - row.offsetTop }
}

/**
 * The list changed shape in one place - `removed` rows from `at` became `inserted` new
 * rows (a tree node expanded or collapsed). Call synchronously before the new count is
 * rendered: the window, its anchor and the mounted row elements move with their rows,
 * so the reader's row stays pixel-still. Any other change of the list is a new list:
 * re-key the scroller.
 */
function replaceRows(at: number, removed: number, inserted: number) {
  replaceWindowRows(at, removed, inserted)
  const delta = inserted - removed
  const changeEnd = at + removed
  const moved: [number, HTMLElement][] = []
  for (const [index, element] of rowElements) {
    if (index < at) continue
    rowElements.delete(index)
    if (index >= changeEnd) moved.push([index + delta, element])
  }
  for (const [index, element] of moved) rowElements.set(index, element)
}

function scrollToIndex(index: number, options: WindowedScrollerScrollToOptions = {}): Promise<void> {
  return landOn(index, options, false)
}

async function landOn(index: number, options: WindowedScrollerScrollToOptions, byReader: boolean): Promise<void> {
  // A jump made while rows already exist builds the window itself: the first window
  // must not then be built at the start row on top of it.
  if (props.count > 0) {
    isFirstWindowBuilt = true
    endOpeningIndicator()
  }
  await runLanding(() => landAt(index, options), byReader)
  bar.syncFromViewport()
}

function onViewportKeydown(event: KeyboardEvent) {
  // Plain Home/End too: natively they would reach the ends of the mounted window,
  // which is nowhere in particular.
  if (event.key === 'Home') {
    event.preventDefault()
    void landOn(0, {}, true)
  } else if (event.key === 'End') {
    event.preventDefault()
    void landOn(props.count - 1, { align: 'end' }, true)
  }
}

/**
 * Brings row `index` into view with 'nearest' semantics: nothing while it is fully
 * showing; a mounted row scrolls the few pixels needed (the browser's own nearest-edge
 * scroll, so stepping through the list reads as advancing, never as a jump); a row that
 * is not mounted lands exactly through scrollToIndex, top-aligned when it lies above the
 * viewport and bottom-aligned when below. For keyboard highlight movement.
 */
function revealIndex(index: number) {
  if (isRowFullyVisible(index)) return
  const row = isRowRendered(index) ? rowElement(index) : null
  if (row) {
    row.scrollIntoView({ block: 'nearest' })
    return
  }
  const isAbove = index < (readFirstVisible()?.index ?? renderedRange.value.start)
  void scrollToIndex(index, { align: isAbove ? 'start' : 'end' })
}

defineExpose({
  scrollerElement: viewportElement,
  scrollbarElement: computed(() => barReference.value?.barElement ?? null),
  renderedRange,
  scrollToIndex,
  replaceRows,
  revealIndex,
  isLanding,
  readFirstVisible,
  isRowRendered,
  isRowFullyVisible,
  rowElement,
  barTargetAt,
})
</script>

<template>
  <div class="windowed-scroller">
    <div
      ref="viewportElement"
      class="windowed-scroller-viewport"
      v-bind="$attrs"
      @scroll="onViewportScroll"
      @scrollend="onViewportScrollEnd"
      @keydown="onViewportKeydown"
    >
      <div ref="contentElement" class="windowed-scroller-content">
        <template v-for="group in renderedGroups" :key="group.key">
          <div
            v-if="group.kind === 'block'"
            :ref="(element) => setRowElement(group.index, element)"
            :data-index="group.index"
            class="windowed-scroller-row"
            :class="{ 'pinned-out-of-window': group.pinned }"
          >
            <slot name="row" :index="group.index" kind="block" />
          </div>
          <div
            v-else
            class="windowed-scroller-paragraph"
            :class="[paragraphClass, { 'pinned-out-of-window': group.pinned }]"
          >
            <!-- The space after each span is the word gap between one row's text and the next. -->
            <template v-for="index in group.indexes" :key="index">
              <span
                :ref="(element) => setRowElement(index, element)"
                :data-index="index"
                class="windowed-scroller-row"
              >
                <slot name="row" :index="index" kind="inline" />
              </span>{{ ' ' }}
            </template>
          </div>
        </template>
      </div>
    </div>
    <div v-if="isOpeningSlow" class="windowed-scroller-opening">
      <LoadingAnimation />
    </div>
    <!-- The bar state goes in as refs on purpose: unwrapping them here would make
         this whole row list re-render on every scroll tick and resize frame. -->
    <WindowedScrollerBar
      ref="barReference"
      :bar-state="bar"
      :viewport="viewportElement"
      :thin="thin"
      @pointerdown="onBarPointerDown"
      @seek="bar.seek"
      @track-hold="onTrackHold"
      @track-step="onTrackStep"
      @track-release="onTrackRelease"
    />
  </div>
</template>

<style scoped>
.windowed-scroller {
  position: relative;
  display: flex;
  height: 100%;
  min-height: 0;
}
/* Over the (empty) viewport while a scroller created deep in its list waits for its start row. */
.windowed-scroller-opening {
  position: absolute;
  inset: 0;
  pointer-events: none;
}
.windowed-scroller-viewport {
  flex: 1 1 auto;
  min-width: 0;
  height: 100%;
  overflow-y: auto;
  /* The scroller anchors the reader's row itself — see useWindowedScrollerWindow. */
  overflow-anchor: none;
  scrollbar-width: none;
}
.windowed-scroller-viewport::-webkit-scrollbar {
  display: none;
}
.windowed-scroller-content {
  position: relative;
  /* A formatting context, so a leading row's top margin cannot collapse through
     this element and move every row relative to the viewport without changing
     their offsetTop — the one shift the anchor could not see. */
  display: flow-root;
}
/* NOT positioned, whatever class the caller gives it (a column look may set
   `position: relative`): every row's offsetTop must be measured against the
   content element, the inline rows' as much as the blocks'. */
.windowed-scroller-content > .windowed-scroller-paragraph {
  position: static;
}
/* Mounted only so a selection boundary keeps its text node: no box. */
.windowed-scroller-content > .pinned-out-of-window {
  position: absolute;
  top: 0;
  height: 0;
  width: 0;
  overflow: hidden;
}
</style>
