<template>
    <div class="category-node">
        <div class="category-header"
             :style="{ paddingInlineStart: `${8 + depth * 20}px` }"
             @click="toggleExpand">
            <Icon v-if="category.children.length > 0 || category.books.length > 0"
                  icon="fluent:chevron-left-24-regular"
                  :class="{ 'rotate-90': isExpanded }"
                  class="chevron-icon" />
            <input type="checkbox"
                   :checked="isChecked"
                   :indeterminate.prop="isIndeterminate"
                   class="category-checkbox"
                   @click.stop
                   @change="handleCategoryToggle" />
            <span class="category-title">{{ category.title }}</span>
            <span v-if="totalResultCount > 0"
                  class="result-count">({{ totalResultCount }})</span>
        </div>

        <template v-if="isExpanded">
            <CheckedCategoryNode v-for="child in category.children"
                                 :key="child.id"
                                 :category="child"
                                 :depth="depth + 1"
                                 :checked-book-ids="checkedBookIds"
                                 :result-counts="resultCounts"
                                 @toggle-book="$emit('toggleBook', $event)"
                                 @toggle-category="(cat, checked) => $emit('toggleCategory', cat, checked)" />

            <div v-for="book in category.books"
                 :key="book.id"
                 class="book-node"
                 :style="{ paddingInlineStart: `${28 + (depth + 1) * 20}px` }"
                 @click="handleBookToggle(book.id)">
                <input type="checkbox"
                       :checked="checkedBookIds.has(book.id)"
                       class="book-checkbox"
                       @click.stop
                       @change="handleBookToggle(book.id)" />
                <Icon icon="fluent:book-open-24-regular"
                      class="book-icon" />
                <span class="book-title">{{ book.title }}</span>
                <span v-if="resultCounts.get(book.id)"
                      class="result-count">({{ resultCounts.get(book.id) }})</span>
            </div>
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { Icon } from '@iconify/vue'
import type { Category } from '../types/BookCategoryTree'

const props = withDefaults(
    defineProps<{
        category: Category
        depth?: number
        checkedBookIds: Set<number>
        resultCounts: Map<number, number>
    }>(),
    {
        depth: 0
    }
)

const emit = defineEmits<{
    toggleBook: [bookId: number]
    toggleCategory: [category: Category, checked: boolean]
}>()

const isExpanded = ref(false)

const toggleExpand = () => {
    isExpanded.value = !isExpanded.value
}

// Get all book IDs in this category and its children
const getAllBookIds = (cat: Category): number[] => {
    const bookIds = cat.books.map(b => b.id)
    cat.children.forEach(child => {
        bookIds.push(...getAllBookIds(child))
    })
    return bookIds
}

const allBookIds = computed(() => getAllBookIds(props.category))

// Calculate total result count for this category and its children
const totalResultCount = computed(() => {
    let count = 0
    allBookIds.value.forEach(bookId => {
        count += props.resultCounts.get(bookId) || 0
    })
    return count
})

const isChecked = computed(() => {
    if (allBookIds.value.length === 0) return false
    return allBookIds.value.every(id => props.checkedBookIds.has(id))
})

const isIndeterminate = computed(() => {
    if (allBookIds.value.length === 0) return false
    const checkedCount = allBookIds.value.filter(id => props.checkedBookIds.has(id)).length
    return checkedCount > 0 && checkedCount < allBookIds.value.length
})

const handleCategoryToggle = () => {
    emit('toggleCategory', props.category, !isChecked.value)
}

const handleBookToggle = (bookId: number) => {
    emit('toggleBook', bookId)
}
</script>

<style scoped>
.category-node {
    user-select: none;
}

.category-header {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 4px 8px;
    cursor: pointer;
    transition: background-color 0.15s ease;
}

.category-header:hover {
    background-color: var(--color-hover);
}

.chevron-icon {
    width: 16px;
    height: 16px;
    transition: transform 0.2s ease;
    flex-shrink: 0;
    cursor: pointer;
}

.chevron-icon:hover {
    color: var(--accent-color);
}

.rotate-90 {
    transform: rotate(-90deg);
}

.category-checkbox,
.book-checkbox {
    width: 16px;
    height: 16px;
    cursor: pointer;
    flex-shrink: 0;
    transition: transform 0.15s ease;
}

.category-checkbox:hover,
.book-checkbox:hover {
    transform: scale(1.1);
}

.category-title {
    font-weight: 500;
    flex: 1;
    line-height: 1.4;
}

.book-node {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 4px 8px;
    cursor: pointer;
    transition: background-color 0.15s ease;
}

.book-node:hover {
    background-color: var(--color-hover);
}

.book-icon {
    width: 16px;
    height: 16px;
    flex-shrink: 0;
    transition: color 0.15s ease;
}

.book-node:hover .book-icon {
    color: var(--accent-color);
}

.book-title {
    flex: 1;
    line-height: 1.4;
}

.result-count {
    color: var(--color-text-secondary);
    font-size: 0.9em;
    margin-inline-start: 4px;
}
</style>
