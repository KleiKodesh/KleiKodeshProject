<script setup lang="ts">
import { ref, watch } from 'vue'
import SplitPane from '@/components/SplitPane.vue'

const props = defineProps<{
  commentaryVisible: boolean
  sideBySide?: boolean
  commentaryFraction?: number
}>()
const emit = defineEmits<{ 'update:commentaryFraction': [value: number] }>()

const container = ref<HTMLElement | null>(null)
const commentaryFraction = ref(props.commentaryFraction ?? 0.4)
const isDragging = ref(false)

// Sync from parent when restored from IDB
watch(() => props.commentaryFraction, (v) => { if (v != null) commentaryFraction.value = v })

function onDividerPointerDown(e: PointerEvent) {
  isDragging.value = true
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
}

function onPointerMove(e: PointerEvent) {
  if (!isDragging.value || !container.value) return
  const rect = container.value.getBoundingClientRect()
  commentaryFraction.value = Math.min(0.9, Math.max(0.1, (rect.right - e.clientX) / rect.width))
  emit('update:commentaryFraction', commentaryFraction.value)
}

function onPointerUp() {
  isDragging.value = false
}
</script>

<template>
  <div
    v-if="sideBySide && commentaryVisible"
    ref="container"
    class="side-by-side"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
  >
    <div class="side-commentary" :style="{ width: `${commentaryFraction * 100}%` }">
      <slot name="bottom" />
    </div>
    <div class="side-divider" @pointerdown="onDividerPointerDown" />
    <div class="side-lines">
      <slot name="top" />
    </div>
  </div>
  <SplitPane v-else :bottom-visible="commentaryVisible">
    <template #top><slot name="top" /></template>
    <template #bottom><slot name="bottom" /></template>
  </SplitPane>
</template>

<style scoped>
.side-by-side {
  display: flex;
  flex-direction: row;
  flex: 1;
  overflow: hidden;
  min-height: 0;
}
.side-commentary {
  flex-shrink: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.side-lines {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.side-divider {
  width: 2px;
  flex-shrink: 0;
  background: var(--border-color);
  touch-action: none;
  position: relative;
  cursor:
    url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'%3E%3Cpath d='M3 12 L7 8 L7 10 L11 10 L11 14 L7 14 L7 16 Z' fill='%23ffffff' stroke='%23000000' stroke-width='0.5'/%3E%3Cpath d='M21 12 L17 8 L17 10 L13 10 L13 14 L17 14 L17 16 Z' fill='%23ffffff' stroke='%23000000' stroke-width='0.5'/%3E%3C/svg%3E")
      12 12,
    col-resize;
}
/* 20px touch target */
.side-divider::before {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: 50%;
  transform: translateX(-50%);
  width: 20px;
}
/* Visible bar that expands on hover */
.side-divider::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: 50%;
  transform: translateX(-50%);
  width: 2px;
  background: var(--border-color);
  transition: width 120ms;
}
.side-divider:hover::after {
  width: 6px;
  background: color-mix(in srgb, var(--text-secondary) 25%, transparent);
}
</style>
