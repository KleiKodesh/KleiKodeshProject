<template>
  <div class="dropdown" :class="{ 'is-open': isOpen }">
    <div class="dropdown-container" @click="toggleDropdown">
      <span class="dropdown-text">{{ selectedLabel }}</span>
      <div class="dropdown-arrow">
        <svg viewBox="0 0 24 24" width="12" height="12">
          <path d="M7,10L12,15L17,10H7Z" fill="#666" />
        </svg>
      </div>
    </div>
    
    <div v-if="isOpen" class="dropdown-menu">
      <div
        v-for="option in options"
        :key="option.value"
        class="dropdown-option"
        :class="{ 'is-selected': option.value === modelValue }"
        @click="selectOption(option)"
      >
        {{ option.label }}
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'

interface DropdownOption {
  value: string
  label: string
}

interface Props {
  modelValue: string
  options: DropdownOption[]
  title?: string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const isOpen = ref(false)

const selectedLabel = computed(() => {
  const selected = props.options.find(option => option.value === props.modelValue)
  return selected?.label || ''
})

function toggleDropdown() {
  isOpen.value = !isOpen.value
}

function selectOption(option: DropdownOption) {
  emit('update:modelValue', option.value)
  isOpen.value = false
}

// Close dropdown when clicking outside
function handleClickOutside(event: Event) {
  const target = event.target as Element
  if (!target.closest('.dropdown')) {
    isOpen.value = false
  }
}

// Add/remove click outside listener
function setupClickOutside() {
  if (isOpen.value) {
    document.addEventListener('click', handleClickOutside)
  } else {
    document.removeEventListener('click', handleClickOutside)
  }
}

// Watch for open/close changes
import { watch, onUnmounted } from 'vue'

watch(isOpen, setupClickOutside)

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
})
</script>

<style scoped>
.dropdown {
  position: relative;
  width: auto;
  min-width: fit-content;
}

.dropdown-container {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
  border: 1px solid #d2d0ce;
  border-radius: 4px;
  background: white;
  padding: 5px 1px 5px 6px;
  font-size: 12px;
  cursor: pointer;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
  height: 28px;
  box-sizing: border-box;
  width: auto;
  min-width: 80px;
}

.dropdown-container:hover {
  border-color: #a19f9d;
}

.dropdown-text {
  text-align: right;
  white-space: nowrap;
  padding-right: 4px;
}

.dropdown-arrow {
  width: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  margin-right: -2px;
}

.dropdown-menu {
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

.dropdown-option {
  padding: 4px 6px;
  cursor: pointer;
  font-size: 12px;
  text-align: right;
}

.dropdown-option:hover {
  background: #f0f0f0;
}

.dropdown-option.is-selected {
  background: #e3f2fd;
  font-weight: 500;
}

.dropdown-option:active {
  background: #e0e0e0;
}
</style>