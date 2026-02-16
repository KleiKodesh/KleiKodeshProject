<template>
    <div class="checked-tree-wrapper">
        <div class="tree-header">
            <input type="checkbox"
                   :checked="isAllChecked"
                   :indeterminate.prop="isIndeterminate"
                   class="root-checkbox"
                   @change="handleRootCheckboxToggle"
                   title="בחר/בטל הכל" />
            <span class="tree-title">סינון לפי ספרים</span>
            <button @click="$emit('close')"
                    class="close-button">
                <Icon icon="fluent:dismiss-24-regular" />
            </button>
        </div>
        <div class="checked-tree-container">
            <div v-if="categoryTreeStore.isLoading"
                 class="loading-state">
                <LoadingSpinner />
            </div>
            <div v-else
                 class="tree-content">
                <CheckedCategoryNode v-for="category in categoryTreeStore.categoryTree"
                                     :key="category.id"
                                     :category="category"
                                     :checked-book-ids="checkedBookIds"
                                     :result-counts="resultCounts"
                                     @toggle-book="toggleBook"
                                     @toggle-category="toggleCategory" />
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import { useCategoryTreeStore } from '../stores/categoryTreeStore'
import CheckedCategoryNode from './CheckedCategoryNode.vue'
import LoadingSpinner from './common/LoadingSpinner.vue'

const props = defineProps<{
    checkedBookIds: Set<number>
    resultCounts: Map<number, number>
}>()

const emit = defineEmits<{
    toggleBook: [bookId: number]
    toggleCategory: [category: any, checked: boolean]
    checkAll: []
    uncheckAll: []
    close: []
}>()

const categoryTreeStore = useCategoryTreeStore()

const toggleBook = (bookId: number) => {
    emit('toggleBook', bookId)
}

const toggleCategory = (category: any, checked: boolean) => {
    emit('toggleCategory', category, checked)
}

const isAllChecked = computed(() => {
    const totalBooks = categoryTreeStore.allBooks.length
    if (totalBooks === 0) return false
    return props.checkedBookIds.size === totalBooks
})

const isIndeterminate = computed(() => {
    const totalBooks = categoryTreeStore.allBooks.length
    if (totalBooks === 0) return false
    return props.checkedBookIds.size > 0 && props.checkedBookIds.size < totalBooks
})

const handleRootCheckboxToggle = () => {
    if (isAllChecked.value) {
        emit('uncheckAll')
    } else {
        emit('checkAll')
    }
}
</script>

<style scoped>
.checked-tree-wrapper {
    position: absolute;
    right: 0;
    top: 0;
    bottom: 64px;
    z-index: 101;
    height: auto;
    width: auto;
    min-width: 200px;
    max-width: 400px;
    display: flex;
    flex-direction: column;
    background-color: rgba(var(--bg-primary-rgb), 0.95);
    border-left: 0.5px solid var(--border-color);
}

.tree-header {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 4px 8px 4px 0;
    border-bottom: 0.5px solid var(--border-color);
    background-color: transparent;
}

.root-checkbox {
    width: 16px;
    height: 16px;
    cursor: pointer;
    flex-shrink: 0;
    transition: transform 0.15s ease;
}

.root-checkbox:hover {
    transform: scale(1.1);
}

.tree-title {
    flex: 1;
    font-weight: 600;
    font-size: 13px;
}

.close-button {
    padding: 1px;
    margin-left: 5px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    border-radius: 2px;
    transition: background-color 0.15s ease;
}

.close-button:hover {
    background-color: var(--hover-bg);
}

.close-button :deep(svg) {
    width: 14px;
    height: 14px;
}

.checked-tree-container {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow-y: auto;
}

.loading-state {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100%;
}

.tree-content {
    flex: 1;
    display: flex;
    flex-direction: column;
}
</style>
