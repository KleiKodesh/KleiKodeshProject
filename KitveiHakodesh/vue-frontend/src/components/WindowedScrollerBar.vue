<script setup lang="ts">
/**
 * A WindowedScroller's scrollbar, drawn by us to look and behave like the app's
 * native bars: the same width the browser gives its own bar (measured once), the
 * same theme — the thumb colour the app sets on every native bar through
 * `scrollbar-color` (theme.css) — the hidden-scrollbars mode (main.css), arrow
 * buttons that step and repeat while held, a track that pages to the row under the
 * pointer (the parent pages; see WindowedScroller's track hold),
 * and a thumb that drags.
 *
 * It represents the whole list, most of which is not mounted: the parent gives it
 * the list's estimated total height and the viewport's position within it
 * (useWindowedScrollerBarMapping), and it reports where the user took it (`seek`).
 * While the pointer is down on the thumb the parent's position is not applied, so a
 * landing's small correction cannot tug the thumb from under the reader's hand.
 *
 * The track is not plain proportion: the list's landmarks (section starts) each get
 * a minimum span, so a section can be pointed at — windowedScrollerTrackMap.ts.
 * `targetAt` tells a hover label what is under the pointer: the thumb (the current
 * position) or the position a drag of the thumb to there would land on.
 */
import { computed, onMounted, ref, watch } from 'vue'
import { useElementSize, useEventListener, useIntervalFn } from '@vueuse/core'
import { revealScrollbarOnHover } from '@/composables/useUiChromeVisibility'
import { buildTrackMap, positionForTrackOffset, trackOffsetForPosition, type TrackMap } from './windowedScrollerTrackMap'
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
  /**
   * The track was pressed: the position a thumb drag to the pointer would land on.
   * The parent locks that destination and pages to it on each `trackStep` while
   * held, until `trackRelease` — a destination, not a pixel, so the row-height
   * estimate moving under the paging cannot change where it ends.
   */
  trackHold: [position: number]
  trackStep: []
  trackRelease: []
}>()

/** What the browser scrolls by per arrow click. */
const ARROW_STEP_PX = 40
const REPEAT_DELAY_MS = 300
const REPEAT_INTERVAL_MS = 50
/** The least track a landmark's section gets — enough to put the pointer on. */
const MIN_LANDMARK_SPAN_PX = 14
/**
 * In hidden-scrollbars mode the reveal lingers about a second from its last call and
 * `mousemove` stops the moment the pointer rests — so while the pointer is on the
 * bar the reveal is re-armed on this interval, as the native bars' hover does.
 */
const REVEAL_KEEPALIVE_MS = 400

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

const liveThumbHeight = computed(() => {
  if (!isScrollable.value || trackHeight.value <= 0) return 0
  const proportional = (trackHeight.value * viewportHeight.value) / totalHeight.value
  return Math.min(trackHeight.value, Math.max(barWidth.value, proportional))
})
/**
 * The thumb's length follows the estimated total, which wobbles a few percent each
 * time the window slides and a row's real height replaces its estimate. That wobble
 * is not news to the reader, so the shown length only follows a real change: one
 * past THUMB_LENGTH_HYSTERESIS, the track resizing, or reaching either bound.
 */
const THUMB_LENGTH_HYSTERESIS = 0.1
const steadyThumbHeight = ref(0)
watch([liveThumbHeight, trackHeight], ([next], [, previousTrack]) => {
  const shown = steadyThumbHeight.value
  const atBound = next <= barWidth.value || next >= trackHeight.value
  if (!shown || !next || atBound || trackHeight.value !== previousTrack || Math.abs(next - shown) / shown > THUMB_LENGTH_HYSTERESIS) {
    steadyThumbHeight.value = next
  }
}, { immediate: true })
// Frozen while dragged — see the drag section.
const thumbHeight = computed(() => drag.value?.thumbHeight ?? steadyThumbHeight.value)
const thumbTravel = computed(() => Math.max(0, trackHeight.value - thumbHeight.value))

const trackMap = computed(() =>
  buildTrackMap(props.barState.landmarkTiers.value, maxPosition.value, thumbTravel.value, MIN_LANDMARK_SPAN_PX),
)

/** The thumb's offset for a position, and the position for a thumb offset. */
function thumbTopFor(position: number) {
  return trackOffsetForPosition(trackMap.value, Math.min(maxPosition.value, Math.max(0, position)))
}
function positionForThumbTop(top: number) {
  if (thumbTravel.value <= 0) return 0
  return positionForTrackOffset(trackMap.value, Math.min(thumbTravel.value, Math.max(0, top)))
}

// ── Thumb drag ─────────────────────────────────────────────────────────────────
//
// A drag runs on a SNAPSHOT of the bar's geometry taken at the press. Every landing
// the drag causes re-estimates the list's height and moves the landmark positions,
// and a thumb mapped through that live geometry jumps under the hand holding it.
// So while dragging, the thumb's offset is the pointer's own travel, and positions
// are read off the snapshot; on release the thumb takes the parent's position
// through the live map — which a seek outside the window has already set to the
// drag's target (useWindowedScrollerBarMapping), so the thumb stays where the hand
// left it while the rows are fetched, a small correction at most once they land.

interface ThumbDrag {
  startY: number
  startThumbTop: number
  thumbTop: number
  thumbHeight: number
  travel: number
  map: TrackMap
  maxPosition: number
}
// Declared ahead of the immediate watch below, which reads it at setup.
const drag = ref<ThumbDrag | null>(null)
const isDragging = computed(() => drag.value != null)

// The thumb follows the parent's position, except while being dragged.
const shownPosition = ref(0)
watch(position, (next) => { if (!drag.value) shownPosition.value = next }, { immediate: true })
const thumbTop = computed(() => (drag.value ? drag.value.thumbTop : thumbTopFor(shownPosition.value)))

function onThumbPointerDown(event: PointerEvent) {
  if (event.button !== 0) return
  event.preventDefault()
  event.stopPropagation()
  drag.value = {
    startY: event.clientY,
    startThumbTop: thumbTop.value,
    thumbTop: thumbTop.value,
    thumbHeight: thumbHeight.value,
    travel: thumbTravel.value,
    map: trackMap.value,
    maxPosition: maxPosition.value,
  }
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
}

function onThumbPointerMove(event: PointerEvent) {
  const current = drag.value
  if (!current) return
  current.thumbTop = Math.min(current.travel, Math.max(0, current.startThumbTop + event.clientY - current.startY))
  // The snapshot names a position in ITS pixel space. The row-height estimate
  // behind that space moves with every landing the drag causes, so the same pixel
  // would name a different row by the end of a long drag (live-verified: 85% of the
  // track released at 49% of the book). The fraction of the list survives the
  // estimate changing, so that is what is seeked, in the live space.
  const fraction = positionForTrackOffset(current.map, current.thumbTop) / current.maxPosition
  const livePosition = fraction * maxPosition.value
  shownPosition.value = livePosition
  emit('seek', livePosition)
}

function onThumbPointerUp() {
  if (!drag.value) return
  drag.value = null
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

let isTrackHeld = false

function onTrackPointerDown(event: PointerEvent) {
  if (event.target !== trackElement.value || event.button !== 0) return
  const pointerOffset = event.clientY - trackElement.value!.getBoundingClientRect().top
  isTrackHeld = true
  emit('trackHold', positionForThumbTop(pointerOffset - thumbHeight.value / 2))
  pressAndHold(event, () => emit('trackStep'))
}

function onPointerReleased() {
  stopRepeat()
  if (!isTrackHeld) return
  isTrackHeld = false
  emit('trackRelease')
}

useEventListener(window, 'pointerup', onPointerReleased)
useEventListener(window, 'pointercancel', onPointerReleased)

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

/**
 * What is under a viewport Y: the thumb or an arrow (the current position), or a
 * point on the track — the position the thumb would have if dragged there. A thumb
 * being dragged is reported as a target too: it is ahead of the parent's position,
 * and where it is going is what the hand holding it wants to know.
 */
function targetAt(clientY: number): { position: number; onThumb: boolean } | null {
  const bar = barElement.value
  const track = trackElement.value
  if (!bar || !track) return null
  if (isDragging.value) return { position: shownPosition.value, onThumb: false }
  const barRect = bar.getBoundingClientRect()
  if (clientY < barRect.top || clientY > barRect.bottom) return null
  const trackRect = track.getBoundingClientRect()
  const offset = clientY - trackRect.top
  const onTrack = offset >= 0 && offset <= trackRect.height
  const onThumb = offset >= thumbTop.value && offset <= thumbTop.value + thumbHeight.value
  if (!isScrollable.value || !onTrack || onThumb) return { position: position.value, onThumb: true }
  return { position: positionForThumbTop(offset - thumbHeight.value / 2), onThumb: false }
}

defineExpose({ barElement, targetAt })
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
        :class="{ 'is-dragging': isDragging }"
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
  /* Glides rather than jumps — ease-out (easeOutCubic): quick to start, slowing into
     place. A scroll tick, a page step or the estimate correcting itself all flow; a
     new target mid-glide retargets from where the thumb is. The scroll-follow label
     (BookViewScrollbarTocTooltip) glides on the same curve. */
  transition:
    top var(--bar-glide-ms, 220ms) cubic-bezier(0.33, 1, 0.68, 1),
    height var(--bar-glide-ms, 220ms) cubic-bezier(0.33, 1, 0.68, 1),
    background-color 120ms;
}
/* Under the hand it must track the pointer exactly. */
.windowed-scroller-thumb.is-dragging {
  transition: background-color 120ms;
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
