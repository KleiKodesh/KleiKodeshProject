<script setup lang="ts">
/**
 * A long-list scroller that mounts only a sliding WINDOW of rows, in normal document
 * flow, with its own index-based scrollbar beside the viewport.
 *
 * Why not a virtualizer: a virtualizer positions every row absolutely from an
 * ESTIMATED height and corrects as rows get measured, so a jump lands on a guess and
 * then drifts while the real heights arrive — every consumer ends up with a settle
 * loop. Here rows are real flow content: a jump rebuilds the window around the target
 * and sets the scroll from its measured position, exactly and once. When rows above
 * the viewport change height (a zoom, a font swap, the window growing)
 * the reader's row is held in place — see useWindowedScrollerWindow.
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
 * Attributes fall through to the viewport (`tabindex`, data-*, style, native
 * listeners), so the caller treats it as the scroll element it is.
 */
import { computed, nextTick, onBeforeUpdate, onUpdated, ref, watch, type ComponentPublicInstance } from 'vue'
import { useEventListener, useResizeObserver } from '@vueuse/core'
import WindowedScrollerBar from './WindowedScrollerBar.vue'
import { useWindowedScrollerWindow } from '@/composables/useWindowedScrollerWindow'
import { useWindowedScrollerBarMapping } from '@/composables/useWindowedScrollerBarMapping'
import type {
  WindowedScrollerBarTarget,
  WindowedScrollerScrollToOptions,
  WindowedScrollerVisibleRow,
} from './windowedScrollerTypes'

defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<{
    count: number
    pinnedIndexes?: number[]
    /**
     * Where to open: the FIRST window rendered is built around this row, so a
     * restored position never shows the top of the list first. Read once, when the
     * count first becomes known; later jumps go through scrollToIndex.
     */
    initialIndex?: number
    /** Pixels added to the initial row's top — a saved offset into the row. */
    initialOffsetPx?: number
    /**
     * Resolves once the rows of [start, end) can be rendered. The scroller asks
     * before it mounts a range — a landing or an extension — so a row never appears
     * without its content and a landing renders exactly once. Omit when the rows
     * are always available.
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
  }>(),
  { pinnedIndexes: () => [] },
)

const emit = defineEmits<{
  /**
   * The opening landing on `initialIndex` has run — the opening render. Not emitted
   * without one. Emitted even if the landing applied nothing (its rows could not be
   * had, or a newer landing superseded it), so listeners null-check what they touch.
   */
  opened: []
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

const {
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
} = useWindowedScrollerWindow({
  count: () => props.count,
  viewport: () => viewportElement.value,
  rowElement,
  ensureRows: (start, end) => props.ensureRows?.(start, end) ?? Promise.resolve(),
})

const renderedRange = computed(() => ({ start: windowStart.value, end: windowEnd.value }))

/** Pinned rows outside the window, then the window, then pinned rows after — index order is DOM order. */
const renderedIndexes = computed(() => {
  const start = windowStart.value
  const end = windowEnd.value
  const pinned = [...new Set(props.pinnedIndexes)]
    .filter((index) => index >= 0 && index < props.count)
    .sort((first, second) => first - second)
  const indexes: number[] = pinned.filter((index) => index < start)
  for (let index = start; index < end; index++) indexes.push(index)
  indexes.push(...pinned.filter((index) => index >= end))
  return indexes
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
  landAt: (index) => scrollToIndex(index),
  landmarkTiers: () => props.landmarkTiers ?? [],
  rowWeights: () => props.rowWeights,
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
  if (row && Math.abs(row.offsetTop - viewport.scrollTop) <= page) {
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

// Layout changes that are not this component's own patch: a width change re-wrapping
// every row, a font arriving. Held against the last remembered anchor.
useResizeObserver(contentElement, () => {
  holdAnchor()
  fill()
  bar.syncFromViewport()
})
useResizeObserver(viewportElement, () => fill())

// The opening waits for BOTH the rows and the line to open on. The two arrive
// from different places (the book's line count from the service, a saved position
// from IndexedDB) in either order — watching the count alone lost the saved
// position whenever the count came first.
let hasOpened = false
watch(
  () => [props.count, props.initialIndex] as const,
  ([count, initialIndex]) => {
    clampToCount()
    if (!hasOpened && count > 0 && initialIndex != null) {
      hasOpened = true
      void scrollToIndex(initialIndex, { offsetPx: props.initialOffsetPx }).then(() => emit('opened'))
      return
    }
    nextTick(fill)
  },
  { immediate: true },
)
// The opening is only taken while nothing has moved yet. A saved index that
// arrives after the reader has already scrolled or jumped must not pull them back
// to it; any landing (a TOC click, a bar seek) or reader input ends the opening.
useEventListener(viewportElement, ['wheel', 'touchstart', 'pointerdown', 'keydown'], () => { hasOpened = true }, { passive: true })

watch(() => props.rowSourceVersion, () => { void refreshRows() })

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

async function scrollToIndex(index: number, options: WindowedScrollerScrollToOptions = {}): Promise<void> {
  hasOpened = true
  await landAt(index, options)
  bar.syncFromViewport()
}

function onViewportKeydown(event: KeyboardEvent) {
  // Plain Home/End too: natively they would reach the ends of the mounted window,
  // which is nowhere in particular.
  if (event.key === 'Home') {
    event.preventDefault()
    void scrollToIndex(0)
  } else if (event.key === 'End') {
    event.preventDefault()
    void scrollToIndex(props.count - 1, { align: 'end' })
  }
}

defineExpose({
  scrollerElement: viewportElement,
  scrollbarElement: computed(() => barReference.value?.barElement ?? null),
  renderedRange,
  scrollToIndex,
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
      @keydown="onViewportKeydown"
    >
      <div ref="contentElement" class="windowed-scroller-content">
        <div
          v-for="index in renderedIndexes"
          :key="index"
          :ref="(element) => setRowElement(index, element)"
          :data-index="index"
          class="windowed-scroller-row"
          :class="{ 'pinned-out-of-window': !isRowInWindow(index) }"
        >
          <slot name="row" :index="index" />
        </div>
      </div>
    </div>
    <!-- The bar state goes in as refs on purpose: unwrapping them here would make
         this whole row list re-render on every scroll tick and resize frame. -->
    <WindowedScrollerBar
      ref="barReference"
      :bar-state="bar"
      :viewport="viewportElement"
      @seek="bar.seek"
      @track-hold="onTrackHold"
      @track-step="onTrackStep"
      @track-release="onTrackRelease"
    />
  </div>
</template>

<style scoped>
.windowed-scroller {
  display: flex;
  height: 100%;
  min-height: 0;
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
/* Mounted only so a selection boundary keeps its text node: no box. */
.windowed-scroller-row.pinned-out-of-window {
  position: absolute;
  top: 0;
  height: 0;
  width: 0;
  overflow: hidden;
}
</style>
