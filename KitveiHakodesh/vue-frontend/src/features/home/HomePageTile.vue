<script setup lang="ts">
import { ref, computed } from 'vue'
import type { Component } from 'vue'
import { IconPin12Filled, IconDelete16Filled } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { wantsNewTab, OPEN_IN_NEW_TAB_HINT } from '@/composables/useOpenInNewTab'

const props = defineProps<{
  label: string
  icon: Component
  color?: string
  iconScale?: number
  pinned?: boolean
  /** When set, the tile shows pin/delete actions (recently-opened tiles). */
  actions?: boolean
}>()
// `tap` carries whether the user held Ctrl/⌘ (open in new tab). Recently-opened
// tiles honour it; the static navigation tiles ignore the flag.
const emit = defineEmits<{ tap: [openInNewTab: boolean]; togglePin: []; remove: [] }>()

const isRemoving = ref(false)
const isPinPopping = ref(false)

// Tooltip: the item label plus a keyboard/mouse hint. Only the recently-opened
// tiles (actions:true) honour Ctrl+click / Ctrl+Enter, so they show the full
// current-tab/new-tab hint (which already covers click + Enter). The static
// navigation tiles just get the Tab/Enter navigation hint.
const tileTooltip = computed(() =>
  props.actions
    ? `${props.label}\n${OPEN_IN_NEW_TAB_HINT}`
    : `${props.label}\nלחץ Tab למעבר בין האפשרויות, Enter לפתיחה`,
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
  if (!props.actions || e.pointerType === 'mouse') return // mouse uses hover
  if ((e.target as HTMLElement).closest('.tile-action')) return // don't re-arm on the buttons
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
  // Swallow the click that trails a long-press so the tile doesn't also open.
  if (longPressed) {
    e.stopPropagation()
    e.preventDefault()
    longPressed = false
  }
}

function onContextMenu(e: Event) {
  // Suppress the OS press-and-hold callout on touch for tiles with actions.
  if (props.actions) e.preventDefault()
}

function hideActions() {
  isRevealed.value = false
  isTouch.value = false
}

useDropdownClose(wrapRef, hideActions, { enabled: () => isRevealed.value, closeOnBlur: false })

function onPin() {
  emit('togglePin')
  // Retrigger the pop animation on every click (toggle the class off, then on next frame).
  isPinPopping.value = false
  requestAnimationFrame(() => (isPinPopping.value = true))
}

function onRemove() {
  if (isRemoving.value) return
  isRemoving.value = true
  // Let the tile animate out before the parent unmounts it.
  window.setTimeout(() => emit('remove'), 170)
}
</script>

<template>
  <div
    ref="wrapRef"
    class="tile-wrap"
    :class="{ 'is-removing': isRemoving, 'is-revealed': isRevealed, 'is-touch': isTouch }"
    @pointerdown="onPointerDown"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
    @pointercancel="onPointerUp"
    @pointerleave="onPointerUp"
    @click.capture="onClickCapture"
    @contextmenu="onContextMenu"
  >
    <button
      class="tile"
      data-nav-item
      :title="tileTooltip"
      @click="$emit('tap', wantsNewTab($event))"
      @auxclick.middle="$emit('tap', wantsNewTab($event))"
    >
      <div class="tile-icon">
        <component
          :is="icon"
          :style="{ ...(color ? { color } : {}), ...(iconScale !== undefined ? { fontSize: iconScale + 'em' } : {}) }"
        />
      </div>
      <span class="tile-label">{{ label }}</span>
    </button>

    <div v-if="actions" class="tile-actions">
      <button
        type="button"
        class="tile-action tile-action--pin"
        :class="{ 'is-active': pinned, 'is-popping': isPinPopping }"
        tabindex="-1"
        :title="pinned ? 'בטל הצמדה' : 'הצמד'"
        :aria-label="pinned ? 'בטל הצמדה' : 'הצמד'"
        @click.stop="onPin"
        @animationend="isPinPopping = false"
      >
        <IconPin12Filled />
      </button>
      <button
        type="button"
        class="tile-action tile-action--remove"
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
.tile-wrap {
  position: relative;
  display: flex;
  user-select: none;
  -webkit-user-select: none;
  -webkit-touch-callout: none;
}

.tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 5px;
  width: 72px;
  padding: 6px 4px;
  background: none;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}
.tile:focus-visible {
  outline: none;
}
.tile:focus-visible .tile-icon {
  transform: scale(1.25);
}
.tile:hover .tile-icon {
  transform: scale(1.15);
}
.tile:active .tile-icon {
  transform: scale(0.95);
}

.tile-icon {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  border-radius: 6px;
  background: none;
  font-size: 28px;
  transition:
    transform 0.15s ease,
    opacity 0.12s ease;
}

/* ── Actions (pin / delete), stacked in the top corner ───────────────────── */
.tile-actions {
  position: absolute;
  top: 0;
  inset-inline-start: -1px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  z-index: 2;
}
.tile-action {
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
.tile-wrap:hover .tile-action,
.tile-wrap.is-revealed .tile-action {
  opacity: 1;
  pointer-events: auto;
}
.tile-action:hover {
  background: var(--bg-secondary);
  transform: scale(1.1);
}
.tile-action:active {
  transform: scale(1.02);
}
.tile-action--pin {
  color: var(--accent-color);
}
/* The pin glyph tilts into a "planted" angle when pinned, so the pinned state
   reads at a glance. Rotating the inner glyph keeps it independent of the disc's
   hover/press scaling. */
.tile-action--pin :deep(svg) {
  transition: transform 0.24s ease;
}
.tile-action--pin.is-active :deep(svg) {
  transform: rotate(-28deg);
}
.tile-action--remove {
  color: var(--text-secondary);
}
.tile-action--remove:hover {
  color: var(--status-danger);
}
/* A pinned tile keeps its pin badge visible even when not hovering. */
.tile-action--pin.is-active {
  opacity: 1;
  pointer-events: auto;
}
/* Click feedback: the disc gives a gentle pulse when toggled. */
.tile-action--pin.is-popping {
  animation: pin-pop 0.26s ease;
}
@keyframes pin-pop {
  0% {
    transform: scale(1);
  }
  45% {
    transform: scale(1.2);
  }
  100% {
    transform: scale(1);
  }
}

/* Touch long-press reveal: enlarged to a comfortable tap size and laid out as a
   horizontal pair centered over the icon, so the targets clear the label. */
.tile-wrap.is-touch .tile-actions {
  top: 6px;
  inset-inline-start: 0;
  width: 100%;
  height: 48px;
  flex-direction: row;
  align-items: center;
  justify-content: center;
  gap: 8px;
}
.tile-wrap.is-touch .tile-action {
  width: 30px;
  height: 30px;
  font-size: 17px;
}
/* Fade the icon back so the action buttons read as a deliberate action mode. */
.tile-wrap.is-touch .tile-icon {
  opacity: 0.3;
}

/* Delete: the whole tile gently scales + fades out before it is removed. */
.tile-wrap.is-removing {
  animation: tile-out 0.16s ease forwards;
  pointer-events: none;
}
@keyframes tile-out {
  to {
    transform: scale(0.82);
    opacity: 0;
  }
}

.tile-label {
  font-size: 11px;
  color: var(--text-primary);
  text-align: center;
  line-height: 1.3;
  max-width: 68px;
  overflow: hidden;
  white-space: normal;
  word-break: break-word;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
}
</style>
