<script setup lang="ts">
import UnreachableBadge from '@/layout/UnreachableBadge.vue'
import { ref, computed } from 'vue'
import { IconPin12Filled, IconDelete16Filled } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { wantsNewTab, OPEN_IN_NEW_TAB_HINT } from '@/composables/useOpenInNewTab'
import type { HomeSuggestionCard as HomeSuggestionCardData } from './homeSuggestionCard'
import { HOME_CARD_PATH_SEPARATOR } from './homeResumePath'
import type { CardsViewMode } from '@/stores/settingsStore'

// `layout` restyles the same card as a list row or an icon tile (home's view
// dropdown); the catalog uses the default card look.
const props = withDefaults(defineProps<{ card: HomeSuggestionCardData; layout?: CardsViewMode }>(), {
  layout: 'cards',
})

const actions = computed(() => !!props.card.pinning || !!props.card.remove)

const isRemoving = ref(false)

// Tiles: the keyboard-focused tile grows to show its full title (see .tile-expanded).
const isKeyboardFocused = ref(false)
function onFocusIn(e: FocusEvent) {
  isKeyboardFocused.value = (e.target as HTMLElement).matches?.(':focus-visible') ?? false
}
const isPinPopping = ref(false)

// Cards' second line: only the nearest step of the path (parent folder / last
// category), like Explorer's short location line; the full path is in the tooltip.
const shortPath = computed(() => {
  const parts = props.card.path
    .split(HOME_CARD_PATH_SEPARATOR)
    .join('\\')
    .split('\\')
    .map((p) => p.trim())
    .filter(Boolean)
  // Folder / file / category paths end with the item itself (file name may add an extension).
  const last = parts.at(-1)
  if (last?.startsWith(props.card.title)) parts.pop()
  return parts.at(-1) ?? ''
})

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
  if (props.card.remove) {
    props.card.remove()
    return
  }
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
    @focusin="onFocusIn"
    @focusout="isKeyboardFocused = false"
  >
    <button
      type="button"
      class="card"
      data-nav-item
      :title="cardTooltip"
      @click="card.open(wantsNewTab($event))"
      @auxclick.middle="card.open(wantsNewTab($event))"
    >
      <span class="card-icon-wrap">
        <component :is="card.icon" class="card-icon" :style="card.color ? { color: card.color } : undefined" />
        <UnreachableBadge v-if="card.unreachable" />
      </span>
      <span class="card-text">
        <span class="card-title">{{ card.title }}</span>
        <span v-if="card.path && layout === 'cards'" class="card-path">{{ shortPath }}</span>
      </span>
    </button>
    <div
      v-if="layout === 'tiles' && isKeyboardFocused"
      class="tile-expanded"
      aria-hidden="true"
    >
      <component :is="card.icon" class="card-icon" :style="card.color ? { color: card.color } : undefined" />
      <span class="tile-expanded-title">{{ card.title }}</span>
    </div>

    <div v-if="actions" class="card-actions">
      <button
        v-if="card.pinning"
        type="button"
        class="card-action card-action--pin"
        :class="{ 'is-active': card.pinning?.pinned, 'is-popping': isPinPopping }"
        tabindex="-1"
        :title="card.pinning?.pinned ? $t('home.card.unpin') : $t('home.card.pin')"
        :aria-label="card.pinning?.pinned ? $t('home.card.unpin') : $t('home.card.pin')"
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
/* Item sizes come from main.css --item-*: the book catalog uses the same ones, so
   home and catalog items are always the same size in every mode. Never hard-code
   a size here that the catalog doesn't share. */
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
  /* Fluent/iOS: spacing separates items, not lines. A barely-there fill gives
     each item a body (focus) without the grid reading as boxes; no border. */
  background: color-mix(in srgb, var(--text-primary) 3%, transparent);
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
/* Selected: Explorer's selection look (main.css --item-selected-*). Keyboard
   focus here; the catalog marks its current card with .is-focused instead. */
/* .card-wrap prefix: outranks the per-layout `background: none` rules below. */
.card-wrap .card:focus-visible,
.card-wrap.is-focused .card {
  background: var(--item-selected-bg);
  box-shadow: inset 0 0 0 1px var(--item-selected-border);
  border-radius: 0;
}
.card:active {
  transform: scale(0.98);
}

.card-icon-wrap {
  position: relative;
  display: inline-flex;
  flex-shrink: 0;
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
.card-path {
  font-size: 11px;
  line-height: 1.3;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
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

/* ── Cards layout: Windows 11 (Fluent) two-line item: 13px title over an 11px
   secondary line, 10px icon gap, same icon as tiles, no fill until hover. */
.layout-cards .card {
  gap: var(--item-gap);
  padding: 6px var(--item-padding-inline);
  border-radius: var(--item-radius);
  background: none;
}
.layout-cards .card-icon {
  font-size: var(--item-large-icon);
}
.layout-cards .card:hover {
  background: var(--hover-bg);
}
.layout-cards .card-text {
  gap: 1px;
}
.layout-cards .card-title {
  font-size: var(--item-title);
  line-height: 18px;
}
.layout-cards .card-path {
  font-size: var(--item-card-path);
  line-height: 15px;
}

/* ── List layout: a flat 32px row, the title only — Explorer's list row:
   20px icon, 13px title, same 10px gap / 8px padding / 4px radius as cards. ── */
.layout-list .card {
  min-height: var(--catalog-row-height);
  height: var(--catalog-row-height);
  gap: var(--item-gap);
  padding: 0 var(--item-padding-inline);
  background: none;
  border-radius: var(--item-radius);
}
.layout-list .card:hover {
  background: var(--hover-bg);
}
.layout-list .card:active {
  transform: none;
  background: var(--active-bg);
}
.layout-list .card-icon {
  font-size: var(--item-icon);
}
.layout-list .card-title {
  font-size: var(--item-title);
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
  border-radius: var(--item-radius);
}
.layout-tiles .card:active {
  transform: none;
}
/* Only the icon moves, as on the catalog tiles, so the grid does not jitter. */
.layout-tiles .card-icon {
  font-size: var(--item-large-icon);
  transition: transform 0.15s ease;
}
.layout-tiles .card:hover .card-icon {
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
  font-size: var(--item-tile-label);
  line-height: 1.3;
  text-align: center;
  white-space: normal;
  word-break: break-word;
  display: -webkit-box;
  -webkit-line-clamp: var(--item-tile-lines);
  -webkit-box-orient: vertical;
}
/* Focused tile: like Explorer's selected icon, the whole selection box grows
   to fit the full title and lays over the row below. Drawn as a copy on top
   of the tile (only for the focused one), so the tile itself keeps its size and
   the grid never moves. Must mirror .layout-tiles .card / .card-title above. */
.card-wrap.layout-tiles:has(.tile-expanded) {
  z-index: 3;
}
.tile-expanded {
  position: absolute;
  top: 0;
  inset-inline: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 5px;
  padding: 6px 4px;
  background: var(--item-selected-bg);
  box-shadow: inset 0 0 0 1px var(--item-selected-border);
  color: var(--text-primary);
  pointer-events: none;
}
.tile-expanded .card-icon {
  font-size: var(--item-large-icon);
}
.tile-expanded-title {
  max-width: 68px;
  font-size: var(--item-tile-label);
  line-height: 1.3;
  text-align: center;
  word-break: break-word;
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
