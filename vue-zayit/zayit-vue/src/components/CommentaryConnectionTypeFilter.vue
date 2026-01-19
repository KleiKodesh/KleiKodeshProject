<template>
    <div class="connection-type-filter" v-if="shouldShowFilter">
        <button class="filter-toggle-btn"
                @click="toggleDropdown"
                :title="selectedLabel"
                ref="toggleButton">
            <Icon icon="fluent:filter-28-regular" class="filter-icon" />
        </button>
        
        <div v-if="isOpen" 
             class="filter-dropdown"
             @click.stop>
            <div v-for="option in availableOptions"
                 :key="option.value || 'all'"
                 class="filter-option"
                 :class="{ 'selected': (option.value === undefined && selectedConnectionTypeId === undefined) || (option.value !== undefined && option.value === selectedConnectionTypeId) }"
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
import { SHOW_ALL_LABEL } from '../types/ConnectionType'
import { commentaryService } from '../services/commentaryService'

interface FilterOption {
    label: string
    value: number | undefined
}

const props = defineProps<{
    book?: Book
    selectedConnectionTypeId?: number
}>()

const emit = defineEmits<{
    filterChange: [connectionTypeId: number | undefined]
}>()

const isOpen = ref(false)
const toggleButton = ref<HTMLElement>()

// Compute available options based on book flags using the service
const availableOptions = computed<FilterOption[]>(() => {
    if (!props.book) return []
    return commentaryService.getAvailableFilterOptions(props.book)
})

// Check if filter should be shown (only if multiple connection types exist)
const shouldShowFilter = computed(() => {
    if (!props.book) return false
    return commentaryService.shouldShowFilter(props.book)
})

// Get selected label for display
const selectedLabel = computed(() => {
    if (props.selectedConnectionTypeId === undefined) {
        return SHOW_ALL_LABEL
    }
    const option = availableOptions.value.find(opt => opt.value === props.selectedConnectionTypeId)
    return option?.label || SHOW_ALL_LABEL
})

const toggleDropdown = () => {
    isOpen.value = !isOpen.value
}

const selectOption = (connectionTypeId: number | undefined) => {
    emit('filterChange', connectionTypeId)
    isOpen.value = false
}

// Close dropdown when clicking outside
const handleClickOutside = (event: MouseEvent) => {
    if (toggleButton.value && !toggleButton.value.contains(event.target as Node)) {
        isOpen.value = false
    }
}

onMounted(() => {
    document.addEventListener('click', handleClickOutside)
})

onUnmounted(() => {
    document.removeEventListener('click', handleClickOutside)
})
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
    border: 1px solid var(--border-color);
    border-radius: 3px;
    color: var(--text-primary);
    cursor: pointer;
    font-size: 12px;
    height: 24px;
    min-width: 24px;
}

.filter-toggle-btn:hover {
    background: var(--hover-bg);
    border-color: var(--accent-color);
}

.filter-icon {
    width: 14px;
    height: 14px;
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