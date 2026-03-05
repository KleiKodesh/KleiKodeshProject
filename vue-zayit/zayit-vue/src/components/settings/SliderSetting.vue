<template>
  <div class="setting-group">
    <label class="setting-label flex-between">
      <span class="bold">{{ label }}</span>
      <span class="text-secondary setting-value">{{ displayValue }}</span>
    </label>
    <input type="range"
           :value="modelValue"
           @input="handleInput"
           :min="min"
           :max="max"
           :step="step"
           class="setting-slider" />
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
/* No custom styles needed - all using global utilities */
</style>
