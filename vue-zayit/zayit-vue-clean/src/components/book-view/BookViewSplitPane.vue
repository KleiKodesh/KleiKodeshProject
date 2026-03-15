<script setup lang="ts">
import { ref } from 'vue'

defineProps<{ bottomVisible: boolean }>()

const container = ref<HTMLElement | null>(null)
const topPane = ref<HTMLElement | null>(null)
const topHeight = ref(0.5)
const isDragging = ref(false)

defineExpose({ topPane })

function onDividerPointerDown(e: PointerEvent) {
  isDragging.value = true
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
}

function onPointerMove(e: PointerEvent) {
  if (!isDragging.value || !container.value) return
  const rect = container.value.getBoundingClientRect()
  const fraction = (e.clientY - rect.top) / rect.height
  topHeight.value = Math.min(0.85, Math.max(0.15, fraction))
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
      ref="topPane"
      class="pane top-pane"
      :style="{ height: bottomVisible ? `${topHeight * 100}%` : '100%' }"
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
}
.top-pane { flex-shrink: 0; }
.bottom-pane { flex: 1; }
.divider {
  height: 5px;
  flex-shrink: 0;
  background: var(--border-color);
  cursor: row-resize;
  touch-action: none;
  transition: background 120ms;
}
.divider:hover { background: color-mix(in srgb, var(--text-secondary) 25%, transparent); }
</style>
