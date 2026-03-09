<template>
    <div ref="toolbarRef"
         class="commentary-toolbar selectable">
        <h3 v-if="!shouldShowNav"
            class="commentary-toolbar-title ellipsis">
            <span v-if="parentNode" class="parent-node">{{ parentNode }} >&nbsp;</span>
            <span class="book-name">{{ bookTitle }}</span>
        </h3>

        <CommentaryHeaderNav v-else
                             :has-previous="hasPrevious"
                             :has-next="hasNext"
                             :show-book-button="bookId !== undefined && lineIndex !== undefined"
                             :commentary-title="bookTitle"
                             :available-books="availableBooks"
                             :show-tree="showTree"
                             @navigate-previous="emit('navigate-previous')"
                             @navigate-next="emit('navigate-next')"
                             @navigate-previous-line="emit('navigate-previous-line')"
                             @navigate-next-line="emit('navigate-next-line')"
                             @navigate-to-book="emit('click')"
                             @select-commentary="(bookId) => emit('select-commentary', bookId)"
                             @input-focus="isInputFocused = true"
                             @input-blur="handleInputBlur"
                             @toggle-tree="emit('toggle-tree')" />
    </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useElementHover, onClickOutside } from '@vueuse/core'
import CommentaryHeaderNav from './CommentaryHeaderNav.vue'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = defineProps<{
    path: string[]
    bookId?: number
    lineIndex?: number
    hasPrevious?: boolean
    hasNext?: boolean
    availableBooks?: CommentaryTreeNode[]
    isDraggingSelection?: boolean
    showTree?: boolean
}>()

const emit = defineEmits<{
    (e: 'click'): void
    (e: 'navigate-previous'): void
    (e: 'navigate-next'): void
    (e: 'navigate-previous-line'): void
    (e: 'navigate-next-line'): void
    (e: 'select-commentary', bookId: number): void
    (e: 'focus-content'): void
    (e: 'toggle-tree'): void
}>()

const parentNode = computed(() => {
    return props.path.length > 1 ? props.path[0] : null
})

const bookTitle = computed(() => props.path[props.path.length - 1] || '')

const displayPath = computed(() => {
    const bookName = props.path[props.path.length - 1] || ''
    
    // If path has more than one element, show parent node > book name
    if (props.path.length > 1) {
        const parent = props.path[0]
        return `${parent} > ${bookName}`
    }
    
    return bookName
})

const toolbarRef = ref<HTMLElement>()
const showNav = useElementHover(toolbarRef)
const isInputFocused = ref(false)

const shouldShowNav = computed(() => {
    return (showNav.value || isInputFocused.value) && !props.isDraggingSelection
})

function handleInputBlur() {
    isInputFocused.value = false
    emit('focus-content')
}

// Hide nav when clicking outside
onClickOutside(toolbarRef, () => {
    if (isInputFocused.value) {
        isInputFocused.value = false
    }
})
</script>

<style scoped>
.commentary-toolbar {
    position: sticky;
    top: 0;
    background-color: var(--reading-bg-primary);
    padding: 8px 0;
    z-index: 10;
    display: flex;
    align-items: center;
    min-height: calc(1.1rem * var(--commentary-font-size) / 100 + 16px);
}

.commentary-toolbar-title {
    margin: 0;
    font-size: calc(1.1rem * var(--commentary-font-size) / 100);
    font-weight: 600;
    color: var(--reading-text-primary);
    direction: rtl;
    font-family: var(--commentary-header-font);
    flex: 1;
    min-width: 0;
    display: flex;
    align-items: center;
}

.parent-node {
    color: var(--text-secondary);
    flex-shrink: 1;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    min-width: 0;
}

.book-name {
    flex-shrink: 0;
    white-space: nowrap;
}
</style>
