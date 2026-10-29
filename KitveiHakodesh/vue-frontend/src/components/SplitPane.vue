<script setup lang="ts">
import { ref, watch } from 'vue'

// Generic split pane with draggable divider.
// When bottomVisible is false, top fills 100%.
// modelValue controls the top-pane fraction (0.1–0.9); defaults to 0.5.
// `secondPlace` puts the second ("bottom") pane below (default) or beside the first, on the
// PHYSICAL right or left (FastDocView's notes place); the fraction is then the first pane's width.
const props = defineProps<{ bottomVisible?: boolean; modelValue?: number; secondPlace?: 'bottom' | 'right' | 'left' }>()
const emit = defineEmits<{ 'update:modelValue': [value: number] }>()

const container = ref<HTMLElement | null>(null)
const topFraction = ref(props.modelValue ?? 0.5)
const isDragging = ref(false)

// Keep topFraction in sync when the parent changes modelValue externally (e.g. session restore)
watch(
  () => props.modelValue,
  (value) => {
    if (value != null && !isDragging.value) topFraction.value = value
  },
)

function onDividerPointerDown(e: PointerEvent) {
  isDragging.value = true
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
}

function onPointerMove(e: PointerEvent) {
  if (!isDragging.value || !container.value) return
  const rect = container.value.getBoundingClientRect()
  const place = props.secondPlace ?? 'bottom'
  const raw =
    place === 'bottom' ? (e.clientY - rect.top) / rect.height
    : place === 'right' ? (e.clientX - rect.left) / rect.width
    : (rect.right - e.clientX) / rect.width
  const newFraction = Math.min(0.9, Math.max(0.1, raw))
  topFraction.value = newFraction
  emit('update:modelValue', newFraction)
}

function onPointerUp() {
  isDragging.value = false
}
</script>

<template>
  <div
    ref="container"
    class="split-pane"
    :class="secondPlace && secondPlace !== 'bottom' ? `split-pane--beside split-pane--${secondPlace}` : ''"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
  >
    <div
      class="pane top-pane"
      :style="
        !bottomVisible ? { flex: '1' }
        : secondPlace && secondPlace !== 'bottom' ? { width: `${topFraction * 100}%` }
        : { height: `${topFraction * 100}%` }
      "
    >
      <slot name="top" />
    </div>

    <template v-if="bottomVisible">
      <div class="sash" :class="secondPlace && secondPlace !== 'bottom' ? 'sash-v' : 'sash-h'" @pointerdown="onDividerPointerDown" />
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
/* Beside: physical sides whatever the page direction (the panes set their own dir). */
.split-pane--beside {
  direction: ltr;
  flex-direction: row;
}
.split-pane--left {
  flex-direction: row-reverse;
}
.split-pane--beside > .pane {
  min-width: 0;
}
</style>
