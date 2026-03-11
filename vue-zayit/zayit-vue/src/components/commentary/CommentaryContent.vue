<template>
    <div ref="scrollContainer"
         class="commentary-scroll-container"
         tabindex="0"
         :style="containerStyles"
         @keydown="handleKeyDown"
         @contextmenu="handleContextMenu">
        <!-- Progress bar -->
        <ProgressBar :progress="loadingProgress" />

        <!-- Context Menu -->
        <ContextMenu ref="contextMenuRef"
                     :items="contextMenuItems" />

        <div v-if="isLoadingMetadata"
             class="commentary-loading flex-center">
            טוען רשימת מפרשים...
        </div>

        <div v-else-if="selectedLineIndex === undefined"
             class="commentary-empty flex-center">
            אנא בחר שורה להצגת מפרשים
        </div>

        <div v-else-if="commentaryGroups.length === 0"
             class="commentary-empty flex-center">
            אין מפרשים לשורה זו
        </div>

        <VList v-else
               ref="vListRef"
               class="commentary-groups"
               :data="virtualGroups"
               style="height: 100%; overflow-y: auto;">
            <template #default="{ item: group, index }">
                <div :data-book-id="group.bookNode.bookId"
                     class="commentary-group">
                    <!-- Sticky Toolbar with navigation -->
                    <CommentaryHeader :path="group.bookNode.path"
                                      :book-id="group.bookNode.bookId"
                                      :line-index="group.bookNode.lineIndex"
                                      :has-previous="index > 0"
                                      :has-next="index < virtualGroups.length - 1"
                                      :available-books="flattenedBooks"
                                      :is-dragging-selection="isDraggingSelection"
                                      :show-tree="showTree"
                                      @click="handleGroupClick(group.bookNode)"
                                      @navigate-previous="navigateToPrevious(index)"
                                      @navigate-next="navigateToNext(index)"
                                      @navigate-previous-line="emit('navigate-previous-line', group.bookNode.bookId)"
                                      @navigate-next-line="emit('navigate-next-line', group.bookNode.bookId)"
                                      @select-commentary="(bookId) => emit('select-commentary', bookId)"
                                      @focus-content="focusContent"
                                      @toggle-tree="emit('toggle-tree')" />

                    <div class="commentary-group-content">
                        <div v-if="!group.metadata?.links || group.metadata.links.length === 0"
                             class="commentary-links">
                            <div class="commentary-link">טוען תוכן....</div>
                        </div>
                        <div v-else
                             class="commentary-links">
                            <div v-for="(link, linkIndex) in group.transformedLinks"
                                 :key="linkIndex"
                                 :data-book-id="group.bookNode.bookId"
                                 :data-link-index="linkIndex"
                                 class="commentary-link selectable"
                                 v-html="link.transformedHtml" />
                        </div>
                    </div>
                </div>
            </template>
        </VList>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { useEventListener } from '@vueuse/core'
import { VList } from 'virtua/vue'
import { useCommentaryTree } from './useCommentaryTree'
import { useCommentaryScroll } from './useCommentaryScroll'
import { useCommentaryVirtualItems } from './useCommentaryVirtualItems'
import { useTabStore } from '@/data/stores/tabStore'
import CommentaryHeader from './CommentaryHeader.vue'
import ContextMenu from '@/components/shared/ContextMenu.vue'
import ProgressBar from '@/components/shared/ProgressBar.vue'
import type { CommentaryTreeNode } from './useCommentaryTree'
import type { CommentaryMetadata } from './useCommentaryContent'
import type { ContextMenuItem } from '@/components/shared/useContextMenu'

const props = defineProps<{
    commentaryGroups: CommentaryMetadata[]
    isLoadingMetadata: boolean
    loadingProgress: number
    bookId?: number
    selectedLineIndex?: number
    connectionTypeId?: number
    showTree?: boolean
    searchQuery?: string
    currentMatchBookId?: number | null
    currentMatchLinkIndex?: number | null
    currentMatchIndexInLink?: number
    loadGroupContent: (bookId: number, lineIndex: number) => Promise<void>
    queueGroupLoad: (bookId: number, lineIndex: number, priority?: boolean) => void
}>()

// Load visible items after groups change
watch(() => props.commentaryGroups, async (newGroups, oldGroups) => {
    if (newGroups.length > 0 && newGroups !== oldGroups) {
        await nextTick()
        handleScroll()
    }
}, { deep: false })



const emit = defineEmits<{
    (e: 'visible-book-changed', bookId: number): void
    (e: 'navigate-previous-line', bookId?: number): void
    (e: 'navigate-next-line', bookId?: number): void
    (e: 'select-commentary', bookId: number): void
    (e: 'toggle-tree'): void
}>()

const scrollContainer = ref<HTMLElement | null>(null)
const vListRef = ref<InstanceType<typeof VList> | null>(null)
const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const isDraggingSelection = ref(false)
const tabStore = useTabStore()
const { flattenedBooks } = useCommentaryTree(computed(() => props.commentaryGroups))

const currentDiacriticsState = computed(() => tabStore.currentDiacriticsState)

const commentaryGroupsMap = computed(() => {
    const map = new Map<string, typeof props.commentaryGroups[0]>()
    props.commentaryGroups.forEach(g => map.set(g.groupName, g))
    return map
})

// Create virtual items with transformed content (same pattern as LineView)
const { virtualGroups } = useCommentaryVirtualItems(
    flattenedBooks,
    commentaryGroupsMap,
    currentDiacriticsState,
    computed(() => props.searchQuery || ''),
    computed(() => props.currentMatchBookId ?? null),
    computed(() => props.currentMatchLinkIndex ?? null),
    computed(() => props.currentMatchIndexInLink ?? 0)
)

const {
    containerStyles,
    saveScrollPosition,
    restoreScrollPosition,
    scrollToGroup,
    detectVisibleGroup,
    handleGroupClick
} = useCommentaryScroll(scrollContainer, vListRef, virtualGroups)

// Context menu items
const contextMenuItems = computed<ContextMenuItem[]>(() => [
    {
        label: 'העתק',
        action: () => document.execCommand('copy')
    },
    {
        label: 'בחר הכל',
        action: selectAllInContainer
    }
])

function handleScroll() {
    detectVisibleGroup(emit)
    saveScrollPosition()

    // Reprioritize visible groups in the queue
    if (!scrollContainer.value) return

    const containerRect = scrollContainer.value.getBoundingClientRect()
    const groups = scrollContainer.value.querySelectorAll('.commentary-group')

    groups.forEach(groupElement => {
        const rect = groupElement.getBoundingClientRect()
        const isVisible = rect.top < containerRect.bottom + 500 && rect.bottom > containerRect.top - 500

        if (isVisible) {
            const bookIdStr = (groupElement as HTMLElement).dataset.bookId
            if (bookIdStr) {
                const bookId = Number(bookIdStr)
                const group = props.commentaryGroups.find(g => g.targetBookId === bookId)

                if (group && group.targetBookId && group.targetLineIndex !== undefined) {
                    const hasContent = group.links && group.links.length > 0

                    // Only reprioritize if content not already loaded
                    if (!hasContent) {
                        // Reprioritize in queue (moves to front if already queued)
                        props.queueGroupLoad(group.targetBookId, group.targetLineIndex, true)
                    }
                }
            }
        }
    })
}

async function navigateToPrevious(currentIndex: number) {
    if (currentIndex > 0) {
        const previousBook = flattenedBooks.value[currentIndex - 1]
        if (previousBook?.bookId) {
            await scrollToGroup(previousBook.bookId)
            focusContent()
        }
    }
}

async function navigateToNext(currentIndex: number) {
    if (currentIndex < flattenedBooks.value.length - 1) {
        const nextBook = flattenedBooks.value[currentIndex + 1]
        if (nextBook?.bookId) {
            await scrollToGroup(nextBook.bookId)
            focusContent()
        }
    }
}

function focusContent() {
    if (scrollContainer.value) {
        scrollContainer.value.focus()
    }
}

function selectAllInContainer() {
    const container = scrollContainer.value
    if (!container) return

    const selection = window.getSelection()
    if (!selection) return

    const range = document.createRange()
    range.selectNodeContents(container)
    selection.removeAllRanges()
    selection.addRange(range)
}

function handleContextMenu(event: MouseEvent) {
    contextMenuRef.value?.show(event)
}

function handleSelectStart() {
    isDraggingSelection.value = true
}

function handleMouseUp() {
    isDraggingSelection.value = false
}

// Handle Ctrl+A to select all in commentary container only
useEventListener('keydown', (event: KeyboardEvent) => {
    const hasCtrlOrMeta = event.ctrlKey || event.metaKey

    // Ctrl+A: Select all in container (scoped to this container only)
    if (hasCtrlOrMeta && event.code === 'KeyA') {
        const container = scrollContainer.value
        if (container && document.activeElement === container) {
            event.preventDefault()
            selectAllInContainer()
        }
    }
})

function handleKeyDown(e: KeyboardEvent) {
    // Prevent spacebar from scrolling
    if (e.key === ' ' && e.target instanceof HTMLElement && e.target.tagName !== 'INPUT') {
        e.preventDefault()
    }
}

onMounted(() => {
    // Get the scrollable element from VList
    if (vListRef.value) {
        const vListElement = vListRef.value.$el as HTMLElement
        scrollContainer.value = vListElement

        if (scrollContainer.value) {
            scrollContainer.value.addEventListener('scroll', handleScroll, { passive: true })
            scrollContainer.value.addEventListener('selectstart', handleSelectStart)
            detectVisibleGroup(emit)

            // Trigger initial load for visible items
            handleScroll()
        }
    }
    document.addEventListener('mouseup', handleMouseUp)
})

onUnmounted(() => {
    if (scrollContainer.value) {
        scrollContainer.value.removeEventListener('scroll', handleScroll)
        scrollContainer.value.removeEventListener('selectstart', handleSelectStart)
    }
    document.removeEventListener('mouseup', handleMouseUp)
    saveScrollPosition()
})

defineExpose({
    scrollToGroup,
    restoreScrollPosition,
    focusContent,
    scrollContainer
})
</script>

<style scoped>
.commentary-scroll-container {
    height: 100%;
    position: relative;
    background-color: var(--reading-bg-primary);
    color: var(--reading-text-primary);
    outline: none;
}

.commentary-groups {
    padding: 0 12px 12px;
}

.commentary-loading,
.commentary-empty {
    height: 100%;
    color: var(--text-secondary);
}

.commentary-group {
    margin-bottom: 12px;
    content-visibility: auto;
    contain-intrinsic-size: auto 500px;
}

.commentary-group-content {
    padding-top: 0;
}

.commentary-group-loading,
.commentary-group-empty {
    color: var(--text-secondary);
    font-style: italic;
    padding: 8px 0;
    display: flex;
    justify-content: center;
}

.commentary-links {
    display: flex;
    flex-direction: column;
    gap: 8px;
}

.commentary-link {
    line-height: var(--commentary-line-height);
    direction: rtl;
    text-align: justify;
    font-family: var(--commentary-text-font);
    font-size: var(--commentary-font-size);
}

/* Search match highlighting */
.commentary-link :deep(mark) {
    background-color: rgba(255, 255, 0, 0.3);
    padding: 1px 0;
}

.commentary-link :deep(mark.current) {
    background-color: rgba(255, 165, 0, 0.5);
    font-weight: 600;
}

:root.dark .commentary-link :deep(mark) {
    background-color: rgba(255, 255, 0, 0.2);
}

:root.dark .commentary-link :deep(mark.current) {
    background-color: rgba(255, 165, 0, 0.4);
}
</style>
