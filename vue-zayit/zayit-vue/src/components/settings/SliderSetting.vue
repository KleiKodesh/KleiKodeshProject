<template>
  <div class="setting-group">
    <label class="setting-label flex-between">
      <span class="bold">{{ label }}</span>
      <span class="text-secondary setting-value">{{ displayValue }}</span>
    </label>
    <input
      type="range"
      :value="modelValue"
      @input="handleInput"
      :min="min"
      :max="max"
      :step="step"
      class="setting-slider"
    />
  </div>
</template>

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

const emit = defineEmits<{
  (e: 'update:modelValue', value: number): void
}>()

const handleInput = (event: Event) => {
  const target = event.target as HTMLInputElement
  emit('update:modelValue', Number(target.value))
}

const displayValue = computed(() => {
  return props.suffix ? `${props.modelValue}${props.suffix}` : props.modelValue
})
</script>

<style scoped>
.setting-group {
  padding: 14px 16px;
  border-bottom: 1px solid var(--border-color);
}

.setting-label {
  font-size: 14px;
  margin-bottom: 10px;
}

.setting-value {
  font-size: 13px;
  font-weight: normal;
}

.setting-slider {
  width: 100%;
  height: 6px;
  background: var(--bg-secondary);
  border-radius: 3px;
  outline: none;
  -webkit-appearance: none;
  appearance: none;
}

.setting-slider::-webkit-slider-thumb {
  -webkit-appearance: none;
  width: 22px;
  height: 22px;
  background: var(--accent-color);
  border-radius: 50%;
  cursor: pointer;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.2);
}

.setting-slider::-moz-range-thumb {
  width: 22px;
  height: 22px;
  background: var(--accent-color);
  border-radius: 50%;
  cursor: pointer;
  border: none;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.2);
}
</style>
