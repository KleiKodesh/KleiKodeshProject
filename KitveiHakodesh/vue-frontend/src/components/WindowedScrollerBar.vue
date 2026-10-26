<script setup lang="ts">
/**
 * A WindowedScroller's scrollbar, drawn by us to look and behave like the app's
 * native bars: the same width the browser gives its own bar (measured once), the
 * same theme — the thumb colour the app sets on every native bar through
 * `scrollbar-color` (theme.css) — the hidden-scrollbars mode (main.css), arrow
 * buttons that step and repeat while held, a track that pages toward the pointer,
 * and a thumb that drags.
 *
 * It represents the whole list, most of which is not mounted: the parent gives it
 * the list's estimated total height and the viewport's position within it
 * (useWindowedScrollerBarMapping), and it reports where the user took it (`seek`).
 * While the pointer is down on the thumb the parent's position is not applied, so a
 * landing's small correction cannot tug the thumb from under the reader's hand.
 */
import { computed, onMounted, ref, watch } from 'vue'
import { useElementSize, useEventListener, useIntervalFn } from '@vueuse/core'
import { revealScrollbarOnHover } from '@/composables/useUiChromeVisibility'
import type { WindowedScrollerBarState } from './windowedScrollerTypes'

const props = defineProps<{
  /**
   * Total height and position as REFS, read here and nowhere else: the scroller's
   * row list must not depend on them, or every scroll tick re-renders every row.
   */
  barState: WindowedScrollerBarState
  /** The viewport this bar drives: wheeled over the bar, revealed on hover, paged by the track. */
  viewport: HTMLElement | null
}>()

const totalHeight = computed(() => props.barState.totalHeight.value)
const position = computed(() => props.barState.position.value)

const emit = defineEmits<{
  seek: [position: number]
}>()

/** What the browser scrolls by per arrow click. */
const ARROW_STEP_PX = 40
/** Page step, as Chromium's: most of a viewport, leaving an overlap. */
const PAGE_FRACTION = 0.875
const REPEAT_DELAY_MS = 300
const REPEAT_INTERVAL_MS = 50

// ── Native width ───────────────────────────────────────────────────────────────

// Measured from a real scroll container, once per page: the width follows the OS,
// the zoom level and `scrollbar-width`, and the bar must match its siblings exactly.
let measuredNativeWidth = 0
function nativeScrollbarWidth(): number {
  if (measuredNativeWidth) return measuredNativeWidth
  const probe = document.createElement('div')
  probe.style.cssText = 'position:absolute;top:-9999px;width:100px;height:100px;overflow:scroll;visibility:hidden'
  document.body.appendChild(probe)
  measuredNativeWidth = probe.offsetWidth - probe.clientWidth || 17
  probe.remove()
  return measuredNativeWidth
}
const barWidth = ref(17)
onMounted(() => { barWidth.value = nativeScrollbarWidth() })

// ── Geometry ───────────────────────────────────────────────────────────────────

/** The whole bar (arrows included) — what the TOC tooltip hovers. */
const barElement = ref<HTMLElement | null>(null)
/** The part between the arrows that the thumb travels in. */
const trackElement = ref<HTMLElement | null>(null)
const { height: trackHeight } = useElementSize(trackElement)
// Observed, not read once: a pane resize changes it, and everything below —
// scrollable, the thumb's length, the drag mapping — must follow.
const { height: observedViewportHeight } = useElementSize(() => props.viewport)
const viewportHeight = computed(() => observedViewportHeight.value || trackHeight.value)
const isScrollable = computed(() => totalHeight.value > viewportHeight.value + 1)
const maxPosition = computed(() => Math.max(1, totalHeight.value - viewportHeight.value))

const thumbHeight = computed(() => {
  if (!isScrollable.value || trackHeight.value <= 0) return 0
  const proportional = (trackHeight.value * viewportHeight.value) / totalHeight.value
  return Math.min(trackHeight.value, Math.max(barWidth.value, proportional))
})
const thumbTravel = computed(() => Math.max(0, trackHeight.value - thumbHeight.value))

/** The thumb's offset for a position, and the position for a thumb offset. */
function thumbTopFor(position: number) {
  return Math.min(1, Math.max(0, position / maxPosition.value)) * thumbTravel.value
}
function positionForThumbTop(top: number) {
  if (thumbTravel.value <= 0) return 0
  return Math.min(1, Math.max(0, top / thumbTravel.value)) * maxPosition.value
}

// The thumb follows the parent's position, except while being dragged.
const shownPosition = ref(0)
let dragging = false
watch(position, (next) => { if (!dragging) shownPosition.value = next }, { immediate: true })
const thumbTop = computed(() => thumbTopFor(shownPosition.value))

// ── Thumb drag ─────────────────────────────────────────────────────────────────

let dragStartY = 0
let dragStartThumbTop = 0

function onThumbPointerDown(event: PointerEvent) {
  if (event.button !== 0) return
  event.preventDefault()
  event.stopPropagation()
  dragging = true
  dragStartY = event.clientY
  dragStartThumbTop = thumbTop.value
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
}

function onThumbPointerMove(event: PointerEvent) {
  if (!dragging) return
  const position = positionForThumbTop(dragStartThumbTop + event.clientY - dragStartY)
  shownPosition.value = position
  emit('seek', position)
}

function onThumbPointerUp() {
  if (!dragging) return
  dragging = false
  shownPosition.value = position.value
}

// ── Arrows and track: step, page, repeat while held ────────────────────────────

let repeatTimer: ReturnType<typeof setTimeout> | null = null

function stopRepeat() {
  if (repeatTimer != null) { clearTimeout(repeatTimer); repeatTimer = null }
}

function stepBy(pixels: number) {
  emit('seek', Math.min(maxPosition.value, Math.max(0, position.value + pixels)))
}

/** Acts once now, then repeats while the button stays down — as a native arrow does. */
function pressAndHold(event: PointerEvent, action: () => void) {
  if (event.button !== 0) return
  event.preventDefault()
  event.stopPropagation()
  action()
  stopRepeat()
  const repeat = () => { action(); repeatTimer = setTimeout(repeat, REPEAT_INTERVAL_MS) }
  repeatTimer = setTimeout(repeat, REPEAT_DELAY_MS)
}

function onArrowPointerDown(event: PointerEvent, direction: -1 | 1) {
  pressAndHold(event, () => stepBy(direction * ARROW_STEP_PX))
}

function onTrackPointerDown(event: PointerEvent) {
  if (event.target !== trackElement.value) return
  const trackTop = trackElement.value!.getBoundingClientRect().top
  pressAndHold(event, () => {
    // Page toward the pointer, and stop once the thumb has reached it.
    const pointerOffset = event.clientY - trackTop
    const direction = pointerOffset > thumbTop.value + thumbHeight.value ? 1 : pointerOffset < thumbTop.value ? -1 : 0
    if (direction !== 0) stepBy(direction * viewportHeight.value * PAGE_FRACTION)
  })
}

useEventListener(window, 'pointerup', stopRepeat)
useEventListener(window, 'pointercancel', stopRepeat)

useEventListener(barElement, 'wheel', (event: WheelEvent) => {
  if (!props.viewport) return
  event.preventDefault()
  props.viewport.scrollBy({ top: event.deltaY })
}, { passive: false })

// Hidden-scrollbars mode keeps the bar invisible until the pointer reaches it, and
// keeps it shown for as long as the pointer stays — see REVEAL_KEEPALIVE_MS.
const revealKeepalive = useIntervalFn(() => revealScrollbarOnHover(props.viewport), REVEAL_KEEPALIVE_MS, { immediate: false })
useEventListener(barElement, 'mouseenter', () => {
  revealScrollbarOnHover(props.viewport)
  revealKeepalive.resume()
})
useEventListener(barElement, 'mouseleave', () => revealKeepalive.pause())


defineExpose({ barElement })
</script>

<template>
  <div
    ref="barElement"
    class="windowed-scroller-bar"
    :class="{ 'is-disabled': !isScrollable }"
    :style="{ width: `${barWidth}px` }"
  >
    <div
      class="windowed-scroller-bar-arrow is-up"
      :style="{ height: `${barWidth}px` }"
      @pointerdown="onArrowPointerDown($event, -1)"
    />
    <div ref="trackElement" class="windowed-scroller-bar-track" @pointerdown="onTrackPointerDown">
      <div
        v-if="isScrollable"
        class="windowed-scroller-thumb"
        :style="{ top: `${thumbTop}px`, height: `${thumbHeight}px` }"
        @pointerdown="onThumbPointerDown"
        @pointermove="onThumbPointerMove"
        @pointerup="onThumbPointerUp"
        @pointercancel="onThumbPointerUp"
      />
    </div>
    <div
      class="windowed-scroller-bar-arrow is-down"
      :style="{ height: `${barWidth}px` }"
      @pointerdown="onArrowPointerDown($event, 1)"
    />
  </div>
</template>

<style scoped>
/* The same colours theme.css gives every native bar: thumb = text-secondary at 30%
   on a transparent track. Only the variables are referenced, so every theme and
   custom tint applies here exactly as it does to the native bars. */
.windowed-scroller-bar {
  --bar-thumb: color-mix(in srgb, var(--text-secondary) 30%, transparent);
  --bar-thumb-hover: color-mix(in srgb, var(--text-secondary) 45%, transparent);
  --bar-thumb-active: color-mix(in srgb, var(--text-secondary) 60%, transparent);
  flex: 0 0 auto;
  display: flex;
  flex-direction: column;
  height: 100%;
  user-select: none;
  touch-action: none;
}
.windowed-scroller-bar-track {
  position: relative;
  flex: 1 1 auto;
  min-height: 0;
}
/* The modern (Fluent) Chromium thumb, measured from Edge at 3x: a fully rounded
   pill about 9px wide centred in the 16px gutter, over a bare track. */
.windowed-scroller-thumb {
  position: absolute;
  left: 50%;
  width: calc(100% - 7px);
  min-width: 6px;
  translate: -50% 0;
  border-radius: 999px;
  background: var(--bar-thumb);
}
.windowed-scroller-thumb:hover {
  background: var(--bar-thumb-hover);
}
.windowed-scroller-thumb:active {
  background: var(--bar-thumb-active);
}
/* The arrow: a bare triangle glyph (8px base, 5px tall) in the thumb colour that
   darkens under the pointer — no button background, as Edge draws it. */
.windowed-scroller-bar-arrow {
  position: relative;
  flex: 0 0 auto;
}
.windowed-scroller-bar-arrow::before {
  content: '';
  position: absolute;
  left: 50%;
  top: 50%;
  translate: -50% -50%;
  border-inline: 4px solid transparent;
}
.windowed-scroller-bar-arrow.is-up::before {
  border-bottom: 5px solid var(--bar-thumb);
}
.windowed-scroller-bar-arrow.is-down::before {
  border-top: 5px solid var(--bar-thumb);
}
.windowed-scroller-bar-arrow.is-up:hover::before {
  border-bottom-color: var(--bar-thumb-active);
}
.windowed-scroller-bar-arrow.is-down:hover::before {
  border-top-color: var(--bar-thumb-active);
}
.windowed-scroller-bar.is-disabled .windowed-scroller-bar-arrow::before {
  opacity: 0.4;
}
</style>
