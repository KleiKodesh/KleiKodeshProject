<template>
    <div role="treeitem"
         :aria-expanded="category.children.length > 0 || category.books.length > 0 ? isExpanded : undefined">
        <div class="flex-row hover-bg focus-accent c-pointer bold tree-node"
             tabindex="0"
             :style="{ paddingInlineStart: `${20 + depth * 20}px` }"
             @click="toggleExpand"
             @keydown.enter.stop="toggleExpand"
             @keydown.space.stop.prevent="toggleExpand">

            <Icon icon="fluent:chevron-left-24-regular"
                  v-if="category.children.length > 0 || category.books.length > 0"
                  :class="{ 'rotate-90': isExpanded }" />
            <span class="flex-110 line-1.4">{{ category.title }}</span>
        </div>

        <template v-if="isExpanded">
            <BookTreeCategoryNode v-for="(child, index) in category.children"
                                  :key="child.id"
                                  :ref="(el: any) => { if (el) childRefs[index] = el }"
                                  :category="child"
                                  :depth="depth + 1" />

            <BookTreeNode v-for="book in category.books"
                          :key="book.id"
                          :book="book"
                          :depth="depth + 1" />
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { Icon } from '@iconify/vue'
import type { Category } from '../types/BookCategoryTree'
import BookTreeNode from './BookTreeNode.vue'

type BookCategoryNodeInstance = {
    collapse: () => void
    reset: () => void
}

withDefaults(defineProps<{
    category: Category
    depth?: number
}>(), {
    depth: 0
})

const isExpanded = ref(false)
const childRefs = ref<BookCategoryNodeInstance[]>([])

const toggleExpand = () => {
    isExpanded.value = !isExpanded.value
}

const collapse = () => {
    isExpanded.value = false
}

const reset = () => {
    isExpanded.value = false
    // Recursively reset all children
    childRefs.value.forEach((child: BookCategoryNodeInstance) => {
        if (child && child.reset) {
            child.reset()
        }
    })
}

defineExpose({
    collapse,
    reset
})

</script>
