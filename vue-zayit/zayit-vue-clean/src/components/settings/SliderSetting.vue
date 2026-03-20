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

const display = computed(() => props.suffix ? `${props.modelValue}${props.suffix}` : props.modelValue)
</script>

<template>
  <div class="setting-row">
    <label class="setting-label">{{ label }}</label>
    <div class="setting-control">
      <input type="range" :value="modelValue" :min="min" :max="max" :step="step"
        @input="emit('update:modelValue', Number(($event.target as HTMLInputElement).value))" />
      <span class="value">{{ display }}</span>
    </div>
  </div>
</template>

<style scoped>
.setting-row { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
.setting-label { font-size: 11px; color: var(--text-secondary); }
.setting-control { display: flex; align-items: center; gap: 6px; }
.value { flex-shrink: 0; width: 44px; font-size: 12px; color: var(--text-secondary); text-align: end; white-space: nowrap; }
input[type=range] { flex: 1; min-width: 0; width: 0; accent-color: var(--accent-color); }
</style>
