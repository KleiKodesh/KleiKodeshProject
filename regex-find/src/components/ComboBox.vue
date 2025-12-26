<template>
  <div class="combobox" :class="{ 'is-open': isOpen }">
    <div class="combobox-container">
      <input
        ref="inputRef"
        v-model="inputValue"
        type="text"
        class="combobox-input"
        :placeholder="placeholder"
        :title="title"
        @input="handleInput"
        @focus="handleFocus"
        @blur="handleBlur"
        @keydown="handleKeyDown"
      />
      <div class="combobox-arrow" @mousedown.prevent="toggleDropdown">
        <svg viewBox="0 0 24 24" width="12" height="12">
          <path d="M7,10L12,15L17,10H7Z" fill="#666" />
        </svg>
      </div>
    </div>
    
    <div v-if="isOpen" class="combobox-dropdown">
      <div
        v-for="(option, index) in filteredOptions"
        :key="option.value"
        class="combobox-option"
        :class="{ 'is-highlighted': index === highlightedIndex }"
        @mousedown.prevent="selectOption(option)"
        @mouseenter="highlightedIndex = index"
      >
        {{ option.label }}
      </div>
      <div v-if="filteredOptions.length === 0" class="combobox-empty">
        אין אפשרויות
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'

interface ComboBoxOption {
  value: string
  label: string
}

interface Props {
  modelValue: string
  options: ComboBoxOption[]
  placeholder?: string
  title?: string
}

const props = withDefaults(defineProps<Props>(), {
  placeholder: '',
  title: ''
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const inputRef = ref<HTMLInputElement>()
const inputValue = ref(props.modelValue)
const isOpen = ref(false)
const highlightedIndex = ref(-1)

const filteredOptions = computed(() => {
  if (!inputValue.value) return props.options
  
  const searchTerm = inputValue.value.toLowerCase()
  return props.options.filter(option => 
    option.label.toLowerCase().includes(searchTerm) ||
    option.value.toLowerCase().includes(searchTerm)
  )
})

watch(() => props.modelValue, (newValue) => {
  inputValue.value = newValue
})

watch(inputValue, (newValue) => {
  emit('update:modelValue', newValue)
})

function handleInput() {
  if (!isOpen.value) {
    isOpen.value = true
  }
  highlightedIndex.value = -1
}

function handleFocus() {
  isOpen.value = true
}

function handleBlur() {
  setTimeout(() => {
    isOpen.value = false
    highlightedIndex.value = -1
  }, 150)
}

function toggleDropdown() {
  isOpen.value = !isOpen.value
  if (isOpen.value) {
    nextTick(() => {
      inputRef.value?.focus()
    })
  }
}

function selectOption(option: ComboBoxOption) {
  inputValue.value = option.value
  isOpen.value = false
  highlightedIndex.value = -1
  inputRef.value?.focus()
}

function handleKeyDown(event: KeyboardEvent) {
  if (!isOpen.value && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
    isOpen.value = true
    highlightedIndex.value = 0
    event.preventDefault()
    return
  }

  if (!isOpen.value) return

  switch (event.key) {
    case 'ArrowDown':
      event.preventDefault()
      highlightedIndex.value = Math.min(highlightedIndex.value + 1, filteredOptions.value.length - 1)
      break
    case 'ArrowUp':
      event.preventDefault()
      highlightedIndex.value = Math.max(highlightedIndex.value - 1, -1)
      break
    case 'Enter':
      event.preventDefault()
      if (highlightedIndex.value >= 0 && highlightedIndex.value < filteredOptions.value.length) {
        const selectedOption = filteredOptions.value[highlightedIndex.value]
        if (selectedOption) {
          selectOption(selectedOption)
        }
      } else {
        isOpen.value = false
      }
      break
    case 'Escape':
      event.preventDefault()
      isOpen.value = false
      highlightedIndex.value = -1
      break
  }
}
</script>

<style scoped>
.combobox {
  position: relative;
  width: 100%;
}

.combobox-container {
  position: relative;
  display: block;
  border: 1px solid #d2d0ce;
  border-radius: 4px;
  background: white;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
  min-width: 120px;
  box-sizing: border-box;
  height: 28px;
}

.combobox-container:hover {
  border-color: #a19f9d;
}

.combobox-container:focus-within {
  border-color: #0078d4;
  box-shadow: 0 0 0 0.5px #0078d4;
}

.combobox-input {
  position: absolute;
  top: 0;
  left: 24px;
  right: 0;
  bottom: 0;
  border: none;
  outline: none;
  padding: 6px 8px;
  font-size: 12px;
  font-family: inherit;
  background: transparent;
}

.combobox-input::placeholder {
  color: #a19f9d;
}

.combobox-arrow {
  position: absolute;
  top: 0;
  left: 0;
  width: 24px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  box-sizing: border-box;
  cursor: pointer;
}

.combobox-arrow:hover {
  background: #f0f0f0;
}

.combobox-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  background: white;
  border: 1px solid #d2d0ce;
  border-top: none;
  border-radius: 0 0 4px 4px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
  max-height: 200px;
  overflow-y: auto;
  z-index: 1000;
}

.combobox-option {
  padding: 6px 8px;
  cursor: pointer;
  font-size: 12px;
}

.combobox-option:hover,
.combobox-option.is-highlighted {
  background: #f0f0f0;
}

.combobox-option:active {
  background: #e0e0e0;
}

.combobox-empty {
  padding: 6px 8px;
  font-size: 12px;
  color: #a19f9d;
  font-style: italic;
}
</style>