<script setup lang="ts">
import { ref, computed } from 'vue'
import { IconPin12Filled, IconDelete16Filled } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { wantsNewTab, OPEN_IN_NEW_TAB_HINT } from '@/composables/useOpenInNewTab'
import type { HomeSuggestionCard as HomeSuggestionCardData } from './homeSuggestionCard'
import type { CardsViewMode } from '@/stores/settingsStore'

// `layout` restyles the same card as a list row or an icon tile (home's view
// dropdown); the catalog uses the default card look.
const props = withDefaults(defineProps<{ card: HomeSuggestionCardData; layout?: CardsViewMode }>(), {
  layout: 'cards',
})

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

useDropdownClose(wrapRef, hideActions, { enabled: () => isRevealed.value, closeOnBlur: false, fitToViewport: false })

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
    :class="[`layout-${layout}`, { 'has-actions': actions, 'is-removing': isRemoving, 'is-revealed': isRevealed, 'is-touch': isTouch }]"
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
      :title="cardTooltip"
      @click="card.open(wantsNewTab($event))"
      @auxclick.middle="card.open(wantsNewTab($event))"
    >
      <component :is="card.icon" class="card-icon" :style="card.color ? { color: card.color } : undefined" />
      <span class="card-text">
        <span class="card-title">{{ card.title }}</span>
      </span>
    </button>

    <div v-if="card.pinning" class="card-actions">
      <button
        type="button"
        class="card-action card-action--pin"
        :class="{ 'is-active': card.pinning.pinned, 'is-popping': isPinPopping }"
        tabindex="-1"
        :title="card.pinning.pinned ? $t('home.card.unpin') : $t('home.card.pin')"
        :aria-label="card.pinning.pinned ? $t('home.card.unpin') : $t('home.card.pin')"
        @click.stop="onPin"
        @animationend="isPinPopping = false"
      >
        <IconPin12Filled />
      </button>
      <button
        type="button"
        class="card-action card-action--remove"
        tabindex="-1"
        :title="$t('home.card.remove')"
        :aria-label="$t('home.card.remove')"
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
  min-height: 49px;
  box-sizing: border-box;
  padding: 8px 12px;
  /* No fill and no border: spacing separates the items; hover/focus paint them. */
  background: none;
  border: 1px solid transparent;
  border-radius: 8px;
  color: var(--text-primary);
  text-align: start;
  cursor: pointer;
  transition:
    background 150ms,
    transform 0.15s ease;
  -webkit-tap-highlight-color: transparent;
}
.card:hover {
  background: color-mix(in srgb, var(--text-primary) 6.5%, transparent);
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
  font-size: 20px;
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

/* One line, like the title: ellipsis, never wraps — the full path is in the
   tooltip. Omitted when empty, so a lone title centres vertically; the card's
   min-height keeps it as tall as a title+path card. */

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

/* ── Cards layout: the inline padding grows with the card's width (% of the
   wrapper), so icons stay aligned down a column while wider cards get a roomier
   start margin. ── */
.layout-cards .card {
  padding-inline: max(12px, 10%);
}
.layout-cards .card-icon {
  font-size: 26px;
}
.layout-cards .card-title {
  font-size: 14px;
}

/* ── List layout: a flat 38px row, the title only. ─────────────────────────────────────────────────────── */
.layout-list .card {
  min-height: 38px;
  height: 38px;
  padding: 0 12px;
  background: none;
  border-radius: 6px;
}
.layout-list .card:hover {
  background: var(--hover-bg);
}
.layout-list .card:active {
  transform: none;
  background: var(--active-bg);
}
.layout-list .card-title {
  font-size: 14px;
}
.layout-list .card-actions {
  top: 50%;
  transform: translateY(-50%);
}

/* ── Tiles layout: the catalog's 72px icon tile — icon over a short label. ── */
.layout-tiles {
  width: 72px;
}
.layout-tiles .card {
  flex-direction: column;
  gap: 5px;
  min-height: 0;
  padding: 6px 4px;
  background: none;
  border-radius: 6px;
}
.layout-tiles .card:active {
  transform: none;
}
/* Only the icon moves, as on the catalog tiles, so the grid does not jitter. */
.layout-tiles .card-icon {
  font-size: 24px;
  transition: transform 0.15s ease;
}
.layout-tiles .card:hover .card-icon,
.layout-tiles .card:focus-visible .card-icon {
  transform: scale(1.15);
}
.layout-tiles .card:active .card-icon {
  transform: scale(0.95);
}
.layout-tiles .card-text {
  flex: none;
  width: 100%;
  align-items: center;
}
.layout-tiles .card-title {
  max-width: 68px;
  font-size: 11px;
  line-height: 1.3;
  text-align: center;
  white-space: normal;
  word-break: break-word;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
}
/* No room under a tile; the path stays in the tooltip. */
.layout-tiles .card-actions {
  top: 0;
  inset-inline-end: 0;
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
