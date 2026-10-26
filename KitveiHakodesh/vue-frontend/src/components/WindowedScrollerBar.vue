<script setup lang="ts">
/**
 * The index-based scrollbar beside a WindowedScroller viewport. The thumb's place on
 * the track is the first visible row's index over the row count — no row heights are
 * involved, so it is exact for a list whose heights are never all known.
 *
 * Dragging the thumb reports the index under it; clicking the track pages the
 * viewport toward the pointer, as a native track does. The wheel over the bar scrolls
 * the viewport.
 */
import { computed, ref } from 'vue'
import { useElementSize, useEventListener } from '@vueuse/core'
import { revealScrollbarOnHover } from '@/composables/useUiChromeVisibility'

const props = defineProps<{
  count: number
  firstVisibleIndex: number
  visibleRowCount: number
  /** The viewport this bar drives — paged on a track click, revealed on hover. */
  viewport: HTMLElement | null
}>()

const emit = defineEmits<{
  jumpToIndex: [index: number]
}>()

const MIN_THUMB_PX = 24

const trackElement = ref<HTMLElement | null>(null)
const { height: trackHeight } = useElementSize(trackElement)

const thumbHeight = computed(() => {
  if (props.count <= 0) return 0
  return Math.max(MIN_THUMB_PX, (trackHeight.value * props.visibleRowCount) / props.count)
})

/** Rows the thumb can address: the thumb is at the bottom when the last screen is in view. */
const scrollableRows = computed(() => Math.max(1, props.count - props.visibleRowCount))

const thumbTop = computed(() => {
  const travel = Math.max(0, trackHeight.value - thumbHeight.value)
  const fraction = Math.min(1, Math.max(0, props.firstVisibleIndex / scrollableRows.value))
  return fraction * travel
})

const hidden = computed(() => props.count <= props.visibleRowCount)

let dragStartY = 0
let dragStartThumbTop = 0
let dragging = false

function indexForThumbTop(top: number): number {
  const travel = Math.max(1, trackHeight.value - thumbHeight.value)
  const fraction = Math.min(1, Math.max(0, top / travel))
  return Math.round(fraction * scrollableRows.value)
}

function onThumbPointerDown(event: PointerEvent) {
  if (event.button !== 0) return
  event.preventDefault()
  dragging = true
  dragStartY = event.clientY
  dragStartThumbTop = thumbTop.value
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
}

function onThumbPointerMove(event: PointerEvent) {
  if (!dragging) return
  emit('jumpToIndex', indexForThumbTop(dragStartThumbTop + event.clientY - dragStartY))
}

function onThumbPointerUp() {
  dragging = false
}

function onTrackPointerDown(event: PointerEvent) {
  if (event.button !== 0 || event.target !== trackElement.value || !props.viewport) return
  event.preventDefault()
  const trackTop = trackElement.value!.getBoundingClientRect().top
  const towardBottom = event.clientY - trackTop > thumbTop.value
  props.viewport.scrollBy({ top: (towardBottom ? 1 : -1) * props.viewport.clientHeight * 0.9 })
}

useEventListener(trackElement, 'wheel', (event: WheelEvent) => {
  if (!props.viewport) return
  event.preventDefault()
  props.viewport.scrollBy({ top: event.deltaY })
}, { passive: false })

// Hidden-scrollbars mode keeps the bar invisible until the pointer reaches it.
useEventListener(trackElement, 'mousemove', () => revealScrollbarOnHover(props.viewport))

defineExpose({ trackElement })
</script>

<template>
  <div
    ref="trackElement"
    class="windowed-scroller-bar"
    :class="{ hidden }"
    @pointerdown="onTrackPointerDown"
  >
    <div
      class="windowed-scroller-thumb"
      :style="{ top: `${thumbTop}px`, height: `${thumbHeight}px` }"
      @pointerdown="onThumbPointerDown"
      @pointermove="onThumbPointerMove"
      @pointerup="onThumbPointerUp"
      @pointercancel="onThumbPointerUp"
    />
  </div>
</template>

<style scoped>
.windowed-scroller-bar {
  position: relative;
  flex: 0 0 12px;
  width: 12px;
  user-select: none;
  touch-action: none;
}
.windowed-scroller-bar.hidden {
  visibility: hidden;
}
.windowed-scroller-thumb {
  position: absolute;
  left: 2px;
  right: 2px;
  border-radius: 4px;
  background: color-mix(in srgb, var(--text-secondary) 30%, transparent);
  transition: background 120ms;
}
.windowed-scroller-bar:hover .windowed-scroller-thumb,
.windowed-scroller-thumb:active {
  background: color-mix(in srgb, var(--text-secondary) 55%, transparent);
}
</style>
