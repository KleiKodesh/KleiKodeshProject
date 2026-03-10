<template>
    <div ref="toolbarRef"
         class="commentary-toolbar selectable">
        <h3 v-if="!shouldShowNav"
            ref="titleRef"
            class="commentary-toolbar-title ellipsis">
            <span v-if="parentNode"
                  ref="parentRef"
                  v-show="showParent"
                  class="parent-node">{{ parentNode }} >&nbsp;</span>
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
                             @navigate-previous-line="emit('navigate-next-line')"
                             @navigate-next-line="emit('navigate-next-line')"
                             @navigate-to-book="emit('click')"
                             @select-commentary="(bookId) => emit('select-commentary', bookId)"
                             @input-focus="isInputFocused = true"
                             @input-blur="handleInputBlur"
                             @toggle-tree="emit('toggle-tree')" />
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, nextTick } from 'vue'
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

const bookTitle = computed(() => {
    const title = props.path[props.path.length - 1] || ''
    const parent = parentNode.value

    // Truncate only for מפרשים - remove "על" and everything after
    if (parent && parent.startsWith('מפרשים')) {
        const onIndex = title.indexOf('על')
        if (onIndex !== -1) {
            return title.substring(0, onIndex).trim()
        }
    }

    return title
})

const toolbarRef = ref<HTMLElement>()
const titleRef = ref<HTMLElement>()
const parentRef = ref<HTMLElement>()
const showNav = useElementHover(toolbarRef)
const isInputFocused = ref(false)
const showParent = ref(true)

const shouldShowNav = computed(() => {
    return (showNav.value || isInputFocused.value) && !props.isDraggingSelection
})

function handleInputBlur() {
    isInputFocused.value = false
    emit('focus-content')
}

function checkOverflow() {
    if (!parentRef.value || !titleRef.value) return

    const titleWidth = titleRef.value.offsetWidth
    const titleScrollWidth = titleRef.value.scrollWidth

    // If title content overflows, hide the parent
    showParent.value = titleScrollWidth <= titleWidth
}

let resizeObserver: ResizeObserver | null = null

watch([titleRef, () => props.path], async () => {
    await nextTick()

    if (resizeObserver) {
        resizeObserver.disconnect()
    }

    if (titleRef.value) {
        showParent.value = true
        await nextTick()
        checkOverflow()

        resizeObserver = new ResizeObserver(() => {
            showParent.value = true
            nextTick(() => checkOverflow())
        })
        resizeObserver.observe(titleRef.value)
    }
}, { immediate: true })

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
    overflow: hidden;
}

.parent-node {
    color: var(--text-secondary);
    white-space: nowrap;
    flex-shrink: 0;
}

.book-name {
    white-space: nowrap;
    flex-shrink: 0;
}
</style>
