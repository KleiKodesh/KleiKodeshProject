<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  label: string
  modelValue: number
  min: number
  max: number
  step: number
  suffix?: string
}>()
const emit = defineEmits<{ 'update:modelValue': [number] }>()

const display = computed(() =>
  props.suffix ? `${props.modelValue}${props.suffix}` : props.modelValue,
)
</script>

<template>
  <div class="setting-row">
    <div class="setting-header">
      <label class="setting-label">{{ label }}</label>
      <span class="value">{{ display }}</span>
    </div>
    <input
      type="range"
      :name="label"
      :value="modelValue"
      :min="min"
      :max="max"
      :step="step"
      @input="emit('update:modelValue', Number(($event.target as HTMLInputElement).value))"
    />
  </div>
</template>

<style scoped>
.setting-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 10px;
}
.setting-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}
.setting-label {
  font-size: 11px;
  color: var(--text-secondary);
}
.value {
  font-size: 12px;
  color: var(--text-secondary);
  white-space: nowrap;
}
input[type='range'] {
  width: 100%;
  accent-color: var(--accent-color);
}
</style>
