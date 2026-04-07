<script setup lang="ts">
import { ref } from 'vue'

// Generic vertical split pane with draggable divider.
// When bottomVisible is false, top fills 100%.
defineProps<{ bottomVisible?: boolean }>()

const container = ref<HTMLElement | null>(null)
const topFraction = ref(0.5)
const isDragging = ref(false)

function onDividerPointerDown(e: PointerEvent) {
  isDragging.value = true
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
}

function onPointerMove(e: PointerEvent) {
  if (!isDragging.value || !container.value) return
  const rect = container.value.getBoundingClientRect()
  topFraction.value = Math.min(0.9, Math.max(0.1, (e.clientY - rect.top) / rect.height))
}

function onPointerUp() {
  isDragging.value = false
}
</script>

<template>
  <div ref="container" class="split-pane" @pointermove="onPointerMove" @pointerup="onPointerUp">
    <div
      class="pane top-pane"
      :style="bottomVisible ? { height: `${topFraction * 100}%` } : { flex: '1' }"
    >
      <slot name="top" />
    </div>

    <template v-if="bottomVisible">
      <div class="divider" @pointerdown="onDividerPointerDown" />
      <div class="pane bottom-pane">
        <slot name="bottom" />
      </div>
    </template>
  </div>
</template>

<style scoped>
.split-pane {
  display: flex;
  flex-direction: column;
  flex: 1;
  overflow: hidden;
  min-height: 0;
}
.pane {
  overflow: hidden;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.top-pane {
  flex-shrink: 0;
}
.bottom-pane {
  flex: 1;
}
.divider {
  height: 2px;
  flex-shrink: 0;
  background: var(--border-color);
  cursor:
    url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'%3E%3Cpath d='M12 3 L8 7 L10 7 L10 11 L14 11 L14 7 L16 7 Z' fill='%23ffffff' stroke='%23000000' stroke-width='0.5'/%3E%3Cpath d='M12 21 L8 17 L10 17 L10 13 L14 13 L14 17 L16 17 Z' fill='%23ffffff' stroke='%23000000' stroke-width='0.5'/%3E%3C/svg%3E")
      12 12,
    row-resize;
  touch-action: none;
  position: relative;
}
.divider::before {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  top: 50%;
  transform: translateY(-50%);
  height: 44px;
}
.divider::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  top: 50%;
  transform: translateY(-50%);
  height: 2px;
  background: var(--border-color);
  transition: height 120ms;
}
.divider:hover::after {
  height: 6px;
  background: color-mix(in srgb, var(--accent-color) 5%, var(--border-color));
}
</style>
