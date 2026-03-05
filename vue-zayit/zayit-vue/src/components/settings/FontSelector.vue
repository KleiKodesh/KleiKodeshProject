<template>
  <div class="setting-group">
    <label class="setting-label flex-between bold">{{ label }}</label>
    <div class="c-pointer custom-select flex-row"
         ref="dropdownRef"
         @click="toggleDropdown"
         tabindex="0">
      <div class="select-display">{{ displayName }}</div>
      <div class="select-arrow">{{ isOpen ? '▲' : '▼' }}</div>
      <div v-if="isOpen"
           class="select-dropdown"
           :style="dropdownStyles"
           @click.stop>
        <div v-for="font in availableFonts"
             :key="font"
             class="c-pointer select-option"
             :class="{ selected: modelValue.includes(font) }"
             @click="selectFont(font)">
          {{ font }}
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { onClickOutside } from '@vueuse/core'

const props = defineProps<{
  label: string
  modelValue: string
  availableFonts: string[]
  fontType: 'sans-serif' | 'serif'
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'toggle'): void
}>()

const dropdownRef = ref<HTMLElement | null>(null)
const isOpen = ref(false)

onClickOutside(dropdownRef, () => {
  isOpen.value = false
})

const toggleDropdown = () => {
  isOpen.value = !isOpen.value
  emit('toggle')
}

const selectFont = (font: string) => {
  emit('update:modelValue', `'${font}', ${props.fontType}`)
  isOpen.value = false
}

const displayName = computed(() => {
  const match = props.modelValue.match(/'([^']+)'/)
  return match ? match[1] : props.modelValue
})

const dropdownStyles = computed(() => ({
  maxHeight: '200px',
  top: '100%'
}))

defineExpose({ isOpen })
</script>

<style scoped>
.custom-select {
  position: relative;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  direction: rtl;
  height: 42px;
  padding: 0 12px;
  user-select: none;
}

.custom-select:hover {
  border-color: var(--accent-color);
}

.select-display {
  flex: 1;
  font-size: 14px;
}

.select-arrow {
  font-size: 10px;
  color: var(--text-secondary);
  margin-left: 8px;
}

.select-dropdown {
  position: absolute;
  left: 0;
  right: 0;
  margin-top: 4px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  overflow-y: auto;
  z-index: 1001;
}

.select-option {
  padding: 10px 12px;
  font-size: 13px;
}

.select-option:hover {
  background: var(--hover-bg);
}

.select-option.selected {
  background: var(--accent-bg);
  color: var(--accent-color);
  font-weight: 500;
}
</style>
