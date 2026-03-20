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
  topFraction.value = Math.min(0.90, Math.max(0.10, (e.clientY - rect.top) / rect.height))
}

function onPointerUp() {
  isDragging.value = false
}
</script>

<template>
  <div
    ref="container"
    class="split-pane"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
  >
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
.top-pane { flex-shrink: 0; }
.bottom-pane { flex: 1; }
.divider {
  height: 3px;
  flex-shrink: 0;
  background: var(--border-color);
  cursor: row-resize;
  touch-action: none;
  position: relative;
  transition: background 120ms;
}
.divider::before {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  top: 50%;
  transform: translateY(-50%);
  height: 20px;
}
.divider:hover {
  background: color-mix(in srgb, var(--text-secondary) 25%, transparent);
}
</style>
