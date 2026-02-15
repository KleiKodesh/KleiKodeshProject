<template>
    <div ref="filterContainer"
         class="connection-type-filter">
        <button class="filter-toggle-btn"
                @click="toggleDropdown"
                :title="selectedLabel"
                :disabled="availableOptions.length <= 1"
                ref="toggleButton">
            <Icon icon="fluent:filter-28-regular"
                  class="filter-icon" />
        </button>

        <div v-if="isOpen"
             class="filter-dropdown"
             @click.stop>
            <div v-for="option in availableOptions"
                 :key="option.value"
                 class="filter-option"
                 :class="{ 'selected': option.value === selectedConnectionTypeId }"
                 @click="selectOption(option.value)">
                {{ option.label }}
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { Icon } from '@iconify/vue'
import type { Book } from '../types/Book'
// import { SHOW_ALL_LABEL } from '../types/ConnectionType'
import { commentaryService } from '../services/commentaryService'
import { useClickOutside } from '../composables/useClickOutside'

interface FilterOption {
    label: string
    value: number
}

const props = defineProps<{
    book?: Book
    selectedConnectionTypeId?: number
    // Optional: parent can provide precomputed available options (per-line)
    availableOptions?: Array<{ label: string; value: number }>
}>()

const emit = defineEmits<{
    filterChange: [connectionTypeId: number]
}>()

const isOpen = ref(false)
const toggleButton = ref<HTMLElement>()
const filterContainer = ref<HTMLElement>()

// Use touch-friendly click outside composable
useClickOutside(filterContainer, () => {
    if (isOpen.value) {
        isOpen.value = false
    }
})

// Compute available options based on provided prop or book flags using the service
const availableOptions = computed<FilterOption[]>(() => {
    if (props.availableOptions && Array.isArray(props.availableOptions)) {
        return props.availableOptions as FilterOption[]
    }
    if (!props.book) return []
    return commentaryService.getAvailableFilterOptions(props.book)
})

// Get selected label for display
const selectedLabel = computed(() => {
    const option = availableOptions.value.find(opt => opt.value === props.selectedConnectionTypeId)
    return option?.label || ''
})

const toggleDropdown = () => {
    isOpen.value = !isOpen.value
}

const selectOption = (connectionTypeId: number) => {
    emit('filterChange', connectionTypeId)
    isOpen.value = false
}
</script>

<style scoped>
.connection-type-filter {
    position: relative;
    display: flex;
    align-items: center;
}

.filter-toggle-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 4px 6px;
    background: transparent;
    border: none;
    border-radius: 3px;
    color: var(--text-primary);
    cursor: pointer;
    font-size: 12px;
    min-height: 28px;
    min-width: 28px;
    height: 28px;
    width: 28px;
}

.filter-toggle-btn:hover {
    background: var(--hover-bg);
}

.filter-toggle-btn:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.filter-toggle-btn:disabled:hover {
    background: transparent;
}

.filter-icon {
    width: 16px;
    height: 16px;
    flex-shrink: 0;
}

.filter-dropdown {
    position: absolute;
    top: calc(100% + 4px);
    right: 0;
    background: var(--bg-primary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
    z-index: 1000;
    min-width: 120px;
    max-width: 200px;
    direction: rtl;
}

.filter-option {
    padding: 8px 12px;
    cursor: pointer;
    font-size: 13px;
    color: var(--text-primary);
    white-space: nowrap;
    border-bottom: 1px solid var(--border-color);
}

.filter-option:last-child {
    border-bottom: none;
}

.filter-option:hover {
    background: var(--hover-bg);
}

.filter-option.selected {
    background: var(--accent-color);
    color: white;
}

.filter-option.selected:hover {
    background: var(--accent-color);
}
</style>