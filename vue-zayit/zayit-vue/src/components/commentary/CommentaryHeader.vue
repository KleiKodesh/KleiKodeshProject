<template>
    <div ref="toolbarRef"
         class="commentary-toolbar selectable"
         @mouseenter="handleMouseEnter"
         @mouseleave="handleMouseLeave">
        <h3 v-if="!shouldShowNav"
            ref="titleRef"
            class="commentary-toolbar-title ellipsis">
            <span v-if="parentNode"
                  ref="parentRef"
                  v-show="showParent"
                  class="parent-node">{{ parentNode }} >&nbsp;</span>
            <span class="book-name">{{ bookTitle }}</span>
        </h3>

        <CommentaryHeaderNav v-if="shouldShowNav"
                             :has-previous="hasPrevious"
                             :has-next="hasNext"
                             :show-book-button="bookId !== undefined && lineIndex !== undefined"
                             :commentary-title="bookTitle"
                             :available-books="availableBooks"
                             :commentary-groups="commentaryGroups"
                             :connection-type-id="connectionTypeId"
                             :show-tree="showTree"
                             @navigate-previous="emit('navigate-previous')"
                             @navigate-next="emit('navigate-next')"
                             @navigate-previous-line="emit('navigate-previous-line')"
                             @navigate-next-line="emit('navigate-next-line')"
                             @navigate-to-book="emit('click')"
                             @select-commentary="(bookId) => emit('select-commentary', bookId)"
                             @select-commentary-with-filter="(bookId, connectionTypeId) => emit('select-commentary-with-filter', bookId, connectionTypeId)"
                             @input-focus="isInputFocused = true"
                             @input-blur="handleInputBlur"
                             @toggle-tree="emit('toggle-tree')" />
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, nextTick, onUnmounted } from 'vue'
import { onClickOutside } from '@vueuse/core'
import CommentaryHeaderNav from './CommentaryHeaderNav.vue'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = defineProps<{
    path: string[]
    bookId?: number
    lineIndex?: number
    hasPrevious?: boolean
    hasNext?: boolean
    availableBooks?: CommentaryTreeNode[]
    commentaryGroups?: any[]
    connectionTypeId?: number
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
    (e: 'select-commentary-with-filter', bookId: number, connectionTypeId: number): void
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
const showNav = ref(false)
const isInputFocused = ref(false)
const showParent = ref(true)
let hoverTimeout: ReturnType<typeof setTimeout> | null = null

const shouldShowNav = computed(() => {
    return (showNav.value || isInputFocused.value) && !props.isDraggingSelection
})

function handleMouseEnter() {
    if (hoverTimeout) {
        clearTimeout(hoverTimeout)
    }
    hoverTimeout = setTimeout(() => {
        showNav.value = true
    }, 300)
}

function handleMouseLeave() {
    if (hoverTimeout) {
        clearTimeout(hoverTimeout)
        hoverTimeout = null
    }
    if (!isInputFocused.value) {
        showNav.value = false
    }
}

function handleInputBlur() {
    isInputFocused.value = false
    showNav.value = false
    emit('focus-content')
}

onUnmounted(() => {
    if (hoverTimeout) {
        clearTimeout(hoverTimeout)
    }
})

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
