<script setup lang="ts">
import { ref, computed } from 'vue'
import { IconPin12Filled, IconDelete16Filled } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { wantsNewTab, OPEN_IN_NEW_TAB_HINT } from '@/composables/useOpenInNewTab'
import type { HomeSuggestionCard as HomeSuggestionCardData } from './homeSuggestionCard'

const props = defineProps<{ card: HomeSuggestionCardData }>()

const actions = computed(() => !!props.card.pinning)

const isRemoving = ref(false)
const isPinPopping = ref(false)

// Tooltip: title + full path, plus the open-in-new-tab hint (every card honours
// Ctrl+click / Ctrl+Enter).
const cardTooltip = computed(() =>
  [props.card.title, props.card.path, OPEN_IN_NEW_TAB_HINT].filter(Boolean).join('\n'),
)

// ── Touch: long-press reveals the actions (touch has no hover) ──────────────────
// Detected via Pointer Events at runtime rather than an @media (hover) query, so
// hybrid mouse+touch devices get both behaviours.
const wrapRef = ref<HTMLElement | null>(null)
const isRevealed = ref(false)
const isTouch = ref(false)
let pressTimer: number | undefined
let longPressed = false
let startX = 0
let startY = 0
const LONG_PRESS_MS = 450
const MOVE_CANCEL_PX = 10

function clearPressTimer() {
  if (pressTimer !== undefined) {
    window.clearTimeout(pressTimer)
    pressTimer = undefined
  }
}

function onPointerDown(e: PointerEvent) {
  if (!actions.value || e.pointerType === 'mouse') return // mouse uses hover
  if ((e.target as HTMLElement).closest('.card-action')) return // don't re-arm on the buttons
  longPressed = false
  startX = e.clientX
  startY = e.clientY
  clearPressTimer()
  pressTimer = window.setTimeout(() => {
    pressTimer = undefined
    longPressed = true
    isTouch.value = true
    isRevealed.value = true
  }, LONG_PRESS_MS)
}

function onPointerMove(e: PointerEvent) {
  if (pressTimer === undefined) return
  // Movement past the threshold means the user is scrolling, not long-pressing.
  if (Math.abs(e.clientX - startX) > MOVE_CANCEL_PX || Math.abs(e.clientY - startY) > MOVE_CANCEL_PX) {
    clearPressTimer()
  }
}

function onPointerUp() {
  clearPressTimer()
}

function onClickCapture(e: MouseEvent) {
  // Swallow the click that trails a long-press so the card doesn't also open.
  if (longPressed) {
    e.stopPropagation()
    e.preventDefault()
    longPressed = false
  }
}

function onContextMenu(e: Event) {
  // Suppress the OS press-and-hold callout on touch for cards with actions.
  if (actions.value) e.preventDefault()
}

function hideActions() {
  isRevealed.value = false
  isTouch.value = false
}

useDropdownClose(wrapRef, hideActions, { enabled: () => isRevealed.value, closeOnBlur: false })

function onPin() {
  props.card.pinning?.togglePin()
  // Retrigger the pop animation on every click (toggle the class off, then on next frame).
  isPinPopping.value = false
  requestAnimationFrame(() => (isPinPopping.value = true))
}

function onRemove() {
  if (isRemoving.value) return
  isRemoving.value = true
  // Let the card animate out before the parent unmounts it.
  window.setTimeout(() => props.card.pinning?.remove(), 170)
}
</script>

<template>
  <div
    ref="wrapRef"
    class="card-wrap"
    :class="{ 'has-actions': actions, 'is-removing': isRemoving, 'is-revealed': isRevealed, 'is-touch': isTouch }"
    @pointerdown="onPointerDown"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
    @pointercancel="onPointerUp"
    @pointerleave="onPointerUp"
    @click.capture="onClickCapture"
    @contextmenu="onContextMenu"
  >
    <button
      type="button"
      class="card"
      data-nav-item
      dir="rtl"
      :title="cardTooltip"
      @click="card.open(wantsNewTab($event))"
      @auxclick.middle="card.open(wantsNewTab($event))"
    >
      <component :is="card.icon" class="card-icon" :style="card.color ? { color: card.color } : undefined" />
      <span class="card-text">
        <span class="card-title">{{ card.title }}</span>
        <span class="card-path">{{ card.path }}</span>
      </span>
    </button>

    <div v-if="card.pinning" class="card-actions">
      <button
        type="button"
        class="card-action card-action--pin"
        :class="{ 'is-active': card.pinning.pinned, 'is-popping': isPinPopping }"
        tabindex="-1"
        :title="card.pinning.pinned ? 'בטל הצמדה' : 'הצמד'"
        :aria-label="card.pinning.pinned ? 'בטל הצמדה' : 'הצמד'"
        @click.stop="onPin"
        @animationend="isPinPopping = false"
      >
        <IconPin12Filled />
      </button>
      <button
        type="button"
        class="card-action card-action--remove"
        tabindex="-1"
        title="הסר מהרשימה"
        aria-label="הסר מהרשימה"
        @click.stop="onRemove"
      >
        <IconDelete16Filled />
      </button>
    </div>
  </div>
</template>

<style scoped>
.card-wrap {
  position: relative;
  display: flex;
  min-width: 0;
  user-select: none;
  -webkit-user-select: none;
  -webkit-touch-callout: none;
}

.card {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  min-width: 0;
  padding: 8px 12px;
  background: none;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  color: var(--text-primary);
  text-align: start;
  cursor: pointer;
  transition:
    background 150ms,
    transform 0.15s ease;
  -webkit-tap-highlight-color: transparent;
}
.card:hover {
  background: var(--input-bg-focus);
}
.card:focus-visible {
  outline: 2px solid var(--accent-color);
  outline-offset: 1px;
}
.card:active {
  transform: scale(0.98);
}

.card-icon {
  flex-shrink: 0;
  font-size: 22px;
}

.card-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
  min-width: 0;
}

.card-title {
  font-size: 13px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* The full path may wrap to two lines, then clips — never overflows the card.
   Always rendered (even empty) so every card has the same height. */
.card-path {
  min-height: 1.3em;
  font-size: 11px;
  line-height: 1.3;
  color: var(--text-secondary);
  overflow: hidden;
  overflow-wrap: anywhere;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

/* ── Actions (pin / delete), in the card's end corner ─────────────────────── */
.card-actions {
  position: absolute;
  top: 4px;
  inset-inline-end: 4px;
  display: flex;
  gap: 4px;
  z-index: 2;
}
.card-action {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 19px;
  height: 19px;
  padding: 0;
  border: 1px solid var(--border-color);
  border-radius: 50%;
  background: var(--bg-primary);
  cursor: pointer;
  font-size: 11px;
  opacity: 0;
  pointer-events: none;
  transition:
    opacity 0.12s ease,
    background 0.12s ease,
    color 0.12s ease,
    transform 0.15s ease;
}
/* Mouse reveals on hover; touch reveals on long-press (.is-revealed). */
.card-wrap:hover .card-action,
.card-wrap.is-revealed .card-action {
  opacity: 1;
  pointer-events: auto;
}
.card-action:hover {
  background: var(--bg-secondary);
  transform: scale(1.1);
}
.card-action--pin {
  color: var(--accent-color);
}
.card-action--pin :deep(svg) {
  transition: transform 0.24s ease;
}
.card-action--pin.is-active :deep(svg) {
  transform: rotate(-28deg);
}
/* A pinned card keeps its pin badge visible even when not hovering. */
.card-action--pin.is-active {
  opacity: 1;
  pointer-events: auto;
}
.card-action--pin.is-popping {
  animation: pin-pop 0.26s ease;
}
@keyframes pin-pop {
  45% {
    transform: scale(1.2);
  }
}
.card-action--remove {
  color: var(--text-secondary);
}
.card-action--remove:hover {
  color: var(--status-danger);
}

/* Touch: ~44px tap targets (iOS HIG) for the card and its actions. */
.card-wrap.is-touch .card-action {
  width: 36px;
  height: 36px;
  font-size: 18px;
}
.card-wrap.is-touch .card-actions {
  gap: 8px;
}
@media (pointer: coarse) {
  .card {
    min-height: 52px;
    padding: 10px 14px;
  }
  .card-action {
    width: 36px;
    height: 36px;
    font-size: 18px;
  }
  .card-actions {
    gap: 8px;
  }
}

/* Delete: the card scales + fades out before it is removed. */
.card-wrap.is-removing {
  animation: card-out 0.16s ease forwards;
  pointer-events: none;
}
@keyframes card-out {
  to {
    transform: scale(0.92);
    opacity: 0;
  }
}
</style>
