<script setup lang="ts">
import { ref, watch } from 'vue'

// Generic vertical split pane with draggable divider.
// When bottomVisible is false, top fills 100%.
// modelValue controls the top-pane fraction (0.1–0.9); defaults to 0.5.
const props = defineProps<{ bottomVisible?: boolean; modelValue?: number }>()
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
  const newFraction = Math.min(0.9, Math.max(0.1, (e.clientY - rect.top) / rect.height))
  topFraction.value = newFraction
  emit('update:modelValue', newFraction)
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
      <div class="sash sash-h" @pointerdown="onDividerPointerDown" />
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
</style>
