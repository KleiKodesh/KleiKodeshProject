<template>
    <div ref="filterContainer" class="category-filter">
        <button class="filter-toggle-btn"
                @click="toggleDropdown"
                :title="selectedLabel"
                :disabled="availableCategories.length === 0"
                ref="toggleButton">
            <Icon icon="fluent:folder-28-regular" class="filter-icon" />
        </button>

        <div v-if="isOpen" class="filter-dropdown" @click.stop>
            <div class="filter-option"
                 :class="{ 'selected': selectedCategoryId === undefined }"
                 @click="selectCategory(undefined)">
                הכל
            </div>
            <div v-for="category in availableCategories"
                 :key="category.id"
                 class="filter-option"
                 :class="{ 'selected': category.id === selectedCategoryId }"
                 @click="selectCategory(category.id)">
                {{ category.name }}
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { Icon } from '@iconify/vue'
import { onClickOutside } from '@vueuse/core'

interface Category {
    id: string | number // Can be string for label-based filtering or number for ID-based
    name: string
}

const props = defineProps<{
    availableCategories: Category[]
    selectedCategoryId?: number | string
}>()

const emit = defineEmits<{
    categoryChange: [categoryId: number | string | undefined]
}>()

const isOpen = ref(false)
const toggleButton = ref<HTMLElement>()
const filterContainer = ref<HTMLElement>()

onClickOutside(filterContainer, () => {
    if (isOpen.value) {
        isOpen.value = false
    }
})

const selectedLabel = computed(() => {
    if (props.selectedCategoryId === undefined) return 'הכל'
    const category = props.availableCategories.find(c => c.id === props.selectedCategoryId)
    return category?.name || 'הכל'
})

const toggleDropdown = () => {
    isOpen.value = !isOpen.value
}

const selectCategory = (categoryId: number | string | undefined) => {
    emit('categoryChange', categoryId)
    isOpen.value = false
}
</script>

<style scoped>
.category-filter {
    position: relative;
    display: flex;
    align-items: center;
}

.filter-toggle-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0;
    background: transparent;
    border: none;
    border-radius: 3px;
    color: var(--text-primary);
    cursor: pointer;
    font-size: 12px;
    min-height: 32px;
    min-width: 32px;
    height: 32px;
    width: 32px;
    touch-action: manipulation;
    transition: transform 0.1s ease, background-color 0.1s ease;
}

.filter-toggle-btn:hover:not(:disabled) {
    background: var(--hover-bg);
}

.filter-toggle-btn:active:not(:disabled) {
    transform: scale(0.95);
    background: var(--active-bg, var(--hover-bg));
}

@media (hover: none) and (pointer: coarse) {
    .filter-toggle-btn {
        min-height: 36px;
        min-width: 36px;
        height: 36px;
        width: 36px;
    }
}

.filter-toggle-btn:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.filter-icon {
    width: 16px;
    height: 16px;
    flex-shrink: 0;
}

@media (hover: none) and (pointer: coarse) {
    .filter-icon {
        width: 18px;
        height: 18px;
    }
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
    min-width: 150px;
    max-width: 250px;
    max-height: 400px;
    overflow-y: auto;
    direction: rtl;
}

.filter-option {
    padding: 6px 10px;
    cursor: pointer;
    font-size: 12px;
    color: var(--text-primary);
    white-space: nowrap;
    border-bottom: 1px solid var(--border-color);
    min-height: 32px;
    display: flex;
    align-items: center;
    touch-action: manipulation;
    transition: background-color 0.1s ease;
}

@media (hover: none) and (pointer: coarse) {
    .filter-option {
        min-height: 36px;
        padding: 8px 12px;
    }
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
