<script setup lang="ts">
import { ref } from 'vue'
import type { Component } from 'vue'
import { IconPin12Filled, IconDelete16Filled } from '@iconify-prerendered/vue-fluent'
defineProps<{
  label: string
  icon: Component
  color?: string
  iconScale?: number
  pinned?: boolean
  /** When set, the tile shows hover pin/delete actions (recently-opened tiles). */
  actions?: boolean
}>()
const emit = defineEmits<{ tap: []; togglePin: []; remove: [] }>()

const isRemoving = ref(false)
const isPinPopping = ref(false)

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
  <div class="tile-wrap" :class="{ 'is-removing': isRemoving }">
    <button class="tile" data-nav-item title="לחץ Tab למעבר בין האפשרויות, Enter לפתיחה" @click="$emit('tap')">
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
  transition: transform 0.15s ease;
}

/* ── Hover actions (pin / delete), stacked in the top corner ─────────────── */
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
.tile-wrap:hover .tile-action {
  opacity: 1;
  pointer-events: auto;
}
.tile-action:hover {
  background: var(--bg-secondary);
  transform: scale(1.1);
}
.tile-action:active {
  transform: scale(0.9);
}
.tile-action--pin {
  color: var(--accent-color);
}
.tile-action--remove {
  color: var(--text-secondary);
}
.tile-action--remove:hover {
  color: #e5484d;
}
/* A pinned tile keeps its pin badge visible even when not hovering. */
.tile-action--pin.is-active {
  opacity: 1;
  pointer-events: auto;
}
/* Click feedback: the pin gives a gentle pulse when toggled. */
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
}
</style>
