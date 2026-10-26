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
 * the viewport change height (a placeholder filling in, a zoom, the window growing)
 * the reader's row is held in place — see useWindowedScrollerWindow.
 *
 * The viewport is a native scroll container with its scrollbar hidden; wheel, touch,
 * PageUp/PageDown and the arrows are the browser's own. Only Home/End need help, as
 * they must reach rows that are not mounted.
 *
 * `pinnedIndexes` keeps rows mounted outside the window, out of flow and clipped —
 * the two ends of a text selection, so a native Range keeps its boundary text nodes.
 *
 * Attributes fall through to the viewport (`tabindex`, data-*, style, native
 * listeners), so the caller treats it as the scroll element it is.
 */
import { computed, nextTick, onBeforeUpdate, onUpdated, ref, watch, type ComponentPublicInstance } from 'vue'
import { useResizeObserver } from '@vueuse/core'
import WindowedScrollerBar from './WindowedScrollerBar.vue'
import { useWindowedScrollerWindow } from '@/composables/useWindowedScrollerWindow'
import type { WindowedScrollerScrollToOptions, WindowedScrollerVisibleRow } from './windowedScrollerTypes'

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
  }>(),
  { pinnedIndexes: () => [] },
)

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
  landAt,
  fill,
} = useWindowedScrollerWindow({
  count: () => props.count,
  viewport: () => viewportElement.value,
  rowElement,
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
  updateVisibleRows()
})

// ── Visible rows (feeds the bar) ───────────────────────────────────────────────

const firstVisibleIndex = ref(0)
const visibleRowCount = ref(1)

function updateVisibleRows() {
  const viewport = viewportElement.value
  if (!viewport) return
  const first = findRowIndexAt(viewport.scrollTop, false)
  if (first == null) return
  const last = findRowIndexAt(viewport.scrollTop + viewport.clientHeight, true) ?? windowEnd.value
  firstVisibleIndex.value = first
  visibleRowCount.value = Math.max(1, last - first)
}

// Hold BEFORE remembering: a layout change the scroller did not make (a font
// swap re-wrapping rows above) can land between a scroll and the resize observer
// that would hold it, and the scroll event is dispatched first. Holding first
// corrects any such shift against the last anchor; on a plain reader scroll the
// anchor row has not moved within the content and the hold is a no-op.
function onViewportScroll() {
  holdAnchor()
  rememberAnchor()
  updateVisibleRows()
  fill()
}

// Layout changes that are not this component's own patch: a width change re-wrapping
// every row, a font arriving. Held against the last remembered anchor.
useResizeObserver(contentElement, () => {
  holdAnchor()
  fill()
  updateVisibleRows()
})
useResizeObserver(viewportElement, () => fill())

let opened = false
watch(
  () => props.count,
  (count) => {
    clampToCount()
    if (!opened && count > 0 && props.initialIndex != null) {
      opened = true
      void scrollToIndex(props.initialIndex, { offsetPx: props.initialOffsetPx })
      return
    }
    nextTick(fill)
  },
  { immediate: true },
)

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
  await landAt(index, options)
  updateVisibleRows()
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
  scrollbarTrackElement: computed(() => barReference.value?.trackElement ?? null),
  renderedRange,
  scrollToIndex,
  readFirstVisible,
  isRowRendered,
  isRowFullyVisible,
  rowElement,
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
    <WindowedScrollerBar
      ref="barReference"
      :count="count"
      :first-visible-index="firstVisibleIndex"
      :visible-row-count="visibleRowCount"
      :viewport="viewportElement"
      @jump-to-index="(index) => scrollToIndex(index)"
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
