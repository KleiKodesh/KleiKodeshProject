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
import { computed, nextTick, onBeforeUpdate, onUpdated, ref, watch, type ComponentPublicInstance } from 'vue'
import { useEventListener, useResizeObserver } from '@vueuse/core'
import WindowedScrollerBar from './WindowedScrollerBar.vue'
import { useWindowedScrollerWindow } from '@/composables/useWindowedScrollerWindow'
import { useWindowedScrollerBarMapping } from '@/composables/useWindowedScrollerBarMapping'
import { buildFlowPlan, ROWS_PLAN } from './windowedScrollerFlowPlan'
import type {
  WindowedScrollerBarTarget,
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
  }>(),
  { pinnedIndexes: () => [], mode: 'rows' },
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
  landAt: (index, align) => scrollToIndex(index, { align }),
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
// A bare modifier is not the reader moving: Ctrl is still held (and auto-repeating)
// when a Ctrl+click opens a new tab and its viewport takes focus, which would
// otherwise cancel the opening before the rows arrive.
const MODIFIER_KEYS = new Set(['Control', 'Shift', 'Alt', 'Meta'])
useEventListener(viewportElement, ['wheel', 'touchstart', 'pointerdown', 'keydown'], (e: Event) => {
  if (e instanceof KeyboardEvent && MODIFIER_KEYS.has(e.key)) return
  hasOpened = true
}, { passive: true })

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
  revealIndex,
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
