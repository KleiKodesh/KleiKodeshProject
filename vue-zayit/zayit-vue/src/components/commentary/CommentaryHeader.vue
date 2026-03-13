<template>
    <div ref="toolbarRef"
         class="commentary-toolbar selectable"
         :title="!shouldShowNav ? 'לחץ לאפשרויות ניווט | Ctrl + לחיצה למעבר לספר' : ''"
         @mouseenter="handleMouseEnter"
         @mouseleave="handleMouseLeave"
         @click="handleHeaderClick">
        <h3 v-if="!shouldShowNav"
            ref="titleRef"
            class="commentary-toolbar-title ellipsis">
            <span v-if="parentNode"
                  ref="parentRef"
                  v-show="showParent"
                  class="parent-node">{{ parentNode }} >&nbsp;</span>
            <span class="book-name">{{ bookTitle }}</span>
        </h3>

        <button v-if="!shouldShowNav"
                class="header-search-btn c-pointer hover-bg"
                title="חיפוש במפרשים"
                @click.stop="emit('open-search')">
            <Icon icon="fluent:search-20-regular" />
        </button>

        <button v-if="!shouldShowNav"
                class="header-close-btn c-pointer hover-bg"
                title="סגור מפרשים"
                @click.stop="emit('close-commentary')">
            <Icon icon="fluent:minimize-20-regular" />
        </button>

        <button v-if="!shouldShowNav"
                class="hover-hint c-pointer hover-bg"
                title="ניווט"
                @click.stop="showNav = true">
            <Icon icon="fluent:more-circle-20-regular" />
        </button>

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
                             @toggle-tree="emit('toggle-tree')"
                             @close-commentary="emit('close-commentary')"
                             @open-search="emit('open-search')" />
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, nextTick, onUnmounted } from 'vue'
import { onClickOutside } from '@vueuse/core'
import { Icon } from '@iconify/vue'
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
    (e: 'close-commentary'): void
    (e: 'open-search'): void
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
    // Temporarily disabled for testing click behavior
    // if (hoverTimeout) {
    //     clearTimeout(hoverTimeout)
    // }
    // hoverTimeout = setTimeout(() => {
    //     showNav.value = true
    // }, 300)
}

function handleMouseLeave() {
    // Temporarily disabled for testing click behavior
    // if (hoverTimeout) {
    //     clearTimeout(hoverTimeout)
    //     hoverTimeout = null
    // }
    // if (!isInputFocused.value) {
    //     showNav.value = false
    // }
}

function handleInputBlur() {
    isInputFocused.value = false
    showNav.value = false
    emit('focus-content')
}

function handleHeaderClick(event: MouseEvent) {
    // Ctrl+click navigates to book
    if (event.ctrlKey || event.metaKey) {
        if (props.bookId !== undefined && props.lineIndex !== undefined) {
            emit('click')
        }
        return
    }

    // Regular click shows nav when hidden
    if (!shouldShowNav.value) {
        showNav.value = true
    }
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
    if (showNav.value) {
        showNav.value = false
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
    cursor: pointer;
}

.commentary-toolbar:has(.commentary-header-nav) {
    cursor: default;
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
    transition: color 0.2s;
}

.commentary-toolbar:hover:not(:has(.commentary-header-nav)) .commentary-toolbar-title {
    color: var(--accent-color);
}

.parent-node {
    color: var(--text-secondary);
    white-space: nowrap;
    flex-shrink: 0;
    transition: color 0.2s;
}

.commentary-toolbar:hover:not(:has(.commentary-header-nav)) .parent-node {
    color: var(--accent-color);
}

.book-name {
    white-space: nowrap;
    flex-shrink: 0;
    transition: color 0.2s;
}

.commentary-toolbar:hover:not(:has(.commentary-header-nav)) .book-name {
    color: var(--accent-color);
}

.hover-hint {
    width: calc(1.1rem * var(--commentary-font-size) / 100);
    height: calc(1.1rem * var(--commentary-font-size) / 100);
    display: flex;
    align-items: center;
    justify-content: center;
    background-color: transparent;
    border: none;
    cursor: pointer;
    padding: 0;
    color: var(--text-secondary);
    transition: color 0.2s, opacity 0.2s;
    flex-shrink: 0;
    opacity: 0.5;
    margin-right: auto;
    font-size: calc(0.9rem * var(--commentary-font-size) / 100);
}

.hover-hint:hover {
    opacity: 1;
    color: var(--accent-color);
}

.header-close-btn {
    width: calc(1.1rem * var(--commentary-font-size) / 100);
    height: calc(1.1rem * var(--commentary-font-size) / 100);
    display: flex;
    align-items: center;
    justify-content: center;
    background-color: transparent;
    border: none;
    cursor: pointer;
    padding: 0;
    color: var(--text-secondary);
    transition: color 0.2s, opacity 0.2s;
    flex-shrink: 0;
    opacity: 0;
    margin-left: 8px;
}

.commentary-toolbar:hover .header-close-btn {
    opacity: 0.5;
}

.header-close-btn:hover {
    opacity: 1 !important;
    color: var(--accent-color);
}

.header-search-btn {
    width: calc(1.1rem * var(--commentary-font-size) / 100);
    height: calc(1.1rem * var(--commentary-font-size) / 100);
    display: flex;
    align-items: center;
    justify-content: center;
    background-color: transparent;
    border: none;
    cursor: pointer;
    padding: 0;
    color: var(--text-secondary);
    transition: color 0.2s, opacity 0.2s;
    flex-shrink: 0;
    opacity: 0;
    margin-left: 8px;
}

.commentary-toolbar:hover .header-search-btn {
    opacity: 0.5;
}

.header-search-btn:hover {
    opacity: 1 !important;
    color: var(--accent-color);
}
</style>
