<template>
    <div ref="toolbarRef" class="commentary-toolbar selectable">
        <h3 v-if="!shouldShowNav" class="commentary-toolbar-title ellipsis">{{ displayPath }}</h3>

        <CommentaryHeaderNav v-else
                             :has-previous="hasPrevious"
                             :has-next="hasNext"
                             :show-book-button="bookId !== undefined && lineIndex !== undefined"
                             :commentary-title="displayPath"
                             :available-books="availableBooks"
                             @navigate-previous="emit('navigate-previous')"
                             @navigate-next="emit('navigate-next')"
                             @navigate-previous-line="emit('navigate-previous-line')"
                             @navigate-next-line="emit('navigate-next-line')"
                             @navigate-to-book="emit('click')"
                             @select-commentary="(bookId) => emit('select-commentary', bookId)"
                             @input-focus="isInputFocused = true"
                             @input-blur="handleInputBlur" />
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
}>()

const emit = defineEmits<{
    (e: 'click'): void
    (e: 'navigate-previous'): void
    (e: 'navigate-next'): void
    (e: 'navigate-previous-line'): void
    (e: 'navigate-next-line'): void
    (e: 'select-commentary', bookId: number): void
    (e: 'focus-content'): void
}>()

const displayPath = computed(() => props.path.join(' > '))

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
}
</style>
