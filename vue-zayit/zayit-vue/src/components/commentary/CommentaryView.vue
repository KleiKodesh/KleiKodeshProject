<template>
    <div class="flex-row height-fill">
        <CommentaryTreePanel v-if="showTree"
                             ref="commentaryTreePanelRef"
                             :commentary-groups="commentaryGroups"
                             :selected-book-id="selectedBookId"
                             class="commentary-tree-panel"
                             @select="onSelectGroup" />

        <CommentaryContent ref="commentaryContentRef"
                           :commentary-groups="commentaryGroups"
                           :is-loading-metadata="isLoadingMetadata"
                           :loading-progress="loadingProgress"
                           :book-id="props.bookId"
                           :selected-line-index="props.selectedLineIndex"
                           :connection-type-id="selectedConnectionTypeId"
                           :show-tree="showTree"
                           :search-query="commentarySearchQuery"
                           :current-match-book-id="currentMatchBookId"
                           :current-match-link-index="currentMatchLinkIndex"
                           :current-match-index-in-link="currentMatchIndexInLink"
                           :load-group-content="loadGroupContent"
                           :queue-group-load="queueGroupLoad"
                           class="flex-110"
                           @visible-book-changed="(bookId) => !isProgrammaticNavigation && handleVisibleBookChanged(bookId)"
                           @navigate-previous-line="(bookId) => emit('navigate-previous-line', bookId)"
                           @navigate-next-line="(bookId) => emit('navigate-next-line', bookId)"
                           @select-commentary="onSelectCommentary"
                           @select-commentary-with-filter="onSelectCommentaryWithFilter"
                           @toggle-tree="handleToggleTree" />
    </div>
</template>

<script setup lang="ts">
import { ref, watch, nextTick, computed } from 'vue'
import CommentaryContent from './CommentaryContent.vue'
import CommentaryTreePanel from './CommentaryTreePanel.vue'
import { useCommentaryView } from './useCommentaryView'
import { useCommentarySearch } from './useCommentarySearch'
import type { Book } from '@/data/types/Book'
import type { TocEntry } from '@/data/types/BookToc'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = defineProps<{
    bookId?: number
    selectedLineIndex?: number
    book?: Book
    flatTocEntries?: TocEntry[]
}>()

const emit = defineEmits<{
    (e: 'clearOtherSelections'): void
    (e: 'navigate-line', newIndex: number, tocEntryId?: number): void
    (e: 'navigate-previous-line', bookId?: number): void
    (e: 'navigate-next-line', bookId?: number): void
}>()

const commentaryContentRef = ref<any>()
const commentaryTreePanelRef = ref<any>()
const showTree = ref(false) // Default to hidden
const pendingScrollToBookId = ref<number | null>(null) // Track pending scroll after filter change
const isProgrammaticNavigation = ref(false) // Flag to prevent sync loops during programmatic navigation

const composableStart = performance.now()
const {
    commentaryGroups,
    isLoadingMetadata,
    loadingProgress,
    selectedBookId,
    selectedConnectionTypeId,
    selectedTocEntryId,
    currentCommentaryBookId,
    handleSelectGroup,
    handleVisibleBookChanged,
    initializeCommentary,
    setCurrentCommentary,
    setConnectionTypeFilter,
    loadGroupContent,
    queueGroupLoad
} = useCommentaryView(props)

// Get scroll container from commentary content
const scrollContainer = computed(() => commentaryContentRef.value?.$el as HTMLElement | null)

// Commentary search
const {
    searchQuery: commentarySearchQuery,
    currentMatch: commentarySearchCurrentMatch,
    currentMatchIndex: commentarySearchCurrentMatchIndex,
    totalMatches: commentarySearchTotalMatches,
    performSearch: performCommentarySearch,
    nextMatch: nextCommentarySearchMatch,
    previousMatch: previousCommentarySearchMatch,
    clearSearch: clearCommentarySearch
} = useCommentarySearch(commentaryGroups, scrollContainer)

const currentMatchBookId = computed(() => commentarySearchCurrentMatch.value?.bookId ?? null)
const currentMatchLinkIndex = computed(() => commentarySearchCurrentMatch.value?.linkIndex ?? null)
const currentMatchIndexInLink = computed(() => commentarySearchCurrentMatch.value?.matchIndex ?? 0)

function onSelectGroup(node: CommentaryTreeNode) {
    const bookId = handleSelectGroup(node)
    if (bookId) {
        // Get the connectionTypeId from the selected node's metadata
        const selectedGroup = commentaryGroups.value.find(g => g.targetBookId === bookId)
        const nodeConnectionTypeId = selectedGroup?.connectionTypeId
        
        console.log('[Commentary] Selected node:', { bookId, nodeConnectionTypeId, currentFilter: selectedConnectionTypeId.value })
        
        // Set programmatic navigation flag
        isProgrammaticNavigation.value = true
        
        // If the selected node has a different connection type than current filter, update filter first
        if (nodeConnectionTypeId !== undefined && nodeConnectionTypeId !== selectedConnectionTypeId.value) {
            console.log('[Commentary] Updating filter to:', nodeConnectionTypeId)
            // Set pending scroll target
            pendingScrollToBookId.value = bookId
            // Update the filter - this will trigger the watch which will handle scrolling
            setConnectionTypeFilter(nodeConnectionTypeId)
        } else {
            // Same filter or no filter, just scroll
            console.log('[Commentary] Same filter, scrolling to:', bookId)
            setCurrentCommentary(bookId)
            // Clear flag after a delay
            setTimeout(() => {
                isProgrammaticNavigation.value = false
            }, 500)
        }
    }
}

async function onSelectCommentary(bookId: number) {
    setCurrentCommentary(bookId)
    // Move focus back to content after navigation
    commentaryContentRef.value?.focusContent()
}

async function onSelectCommentaryWithFilter(bookId: number, connectionTypeId: number) {
    console.log('[Commentary] Selected from header with filter:', { bookId, connectionTypeId, currentFilter: selectedConnectionTypeId.value })
    
    // Set programmatic navigation flag
    isProgrammaticNavigation.value = true
    
    // Set pending scroll target
    pendingScrollToBookId.value = bookId
    
    // Update the filter - this will trigger the watch which will handle scrolling
    setConnectionTypeFilter(connectionTypeId)
}

async function handleToggleTree() {
    showTree.value = !showTree.value
    if (showTree.value) {
        await nextTick()
        commentaryTreePanelRef.value?.scrollToSelected()
    }
}

// Wait for a specific group's content to load
async function waitForGroupContent(bookId: number, maxWaitMs = 3000): Promise<boolean> {
    const startTime = Date.now()
    const checkInterval = 100 // Check every 100ms
    
    while (Date.now() - startTime < maxWaitMs) {
        const group = commentaryGroups.value.find(g => g.targetBookId === bookId)
        
        if (group && group.isLoaded && group.links.length > 0) {
            console.log(`[Commentary] Content loaded for group ${bookId}`)
            return true
        }
        
        // Wait before checking again
        await new Promise(resolve => setTimeout(resolve, checkInterval))
    }
    
    console.warn(`[Commentary] Timeout waiting for content to load for group ${bookId}`)
    return false
}

// Unified scroll function - single place for all scrolling logic with retry
async function scrollToCurrentCommentary(maxRetries = 5) {
    const bookId = currentCommentaryBookId.value || selectedBookId.value
    if (!bookId || !commentaryContentRef.value?.scrollToGroup) return

    for (let attempt = 0; attempt < maxRetries; attempt++) {
        await nextTick()
        
        try {
            await commentaryContentRef.value.scrollToGroup(bookId)
            console.log(`[Commentary] Successfully scrolled to ${bookId} on attempt ${attempt + 1}`)
            return true
        } catch (error) {
            console.warn(`[Commentary] Scroll attempt ${attempt + 1} failed for ${bookId}:`, error)
            
            if (attempt < maxRetries - 1) {
                // Wait before retrying, with exponential backoff
                await new Promise(resolve => setTimeout(resolve, 50 * (attempt + 1)))
            }
        }
    }
    
    console.error(`[Commentary] Failed to scroll to ${bookId} after ${maxRetries} attempts`)
    return false
}

// Watch for line/filter changes - load commentary metadata
watch(
    () => [props.bookId, props.selectedLineIndex, selectedConnectionTypeId.value, selectedTocEntryId.value] as const,
    async ([bookId, lineIndex, connectionTypeId], oldValues) => {
        const oldBookId = oldValues?.[0]
        const oldLineIndex = oldValues?.[1]
        const oldConnectionTypeId = oldValues?.[2]

        const isLineChange = bookId === oldBookId && lineIndex !== oldLineIndex
        const isFilterChange = connectionTypeId !== oldConnectionTypeId

        await initializeCommentary()

        // Wait for content to render
        await nextTick()
        await nextTick()

        if (isLineChange) {
            // Line changed - scroll to current commentary
            await scrollToCurrentCommentary()
        } else if (isFilterChange && pendingScrollToBookId.value) {
            // Filter changed and we have a pending scroll target
            console.log('[Commentary] Filter change complete, waiting for target content to load:', pendingScrollToBookId.value)
            const targetBookId = pendingScrollToBookId.value
            pendingScrollToBookId.value = null
            
            // Wait for the target group's content to load
            await waitForGroupContent(targetBookId)
            
            // Now scroll to it
            setCurrentCommentary(targetBookId)
            
            // Clear programmatic navigation flag after scroll completes
            setTimeout(() => {
                isProgrammaticNavigation.value = false
            }, 500)
        } else {
            // Book or filter changed - restore scroll position
            await commentaryContentRef.value?.restoreScrollPosition()
        }
    },
    { immediate: true }
)

// Watch currentCommentaryBookId - scroll when user changes commentary
watch(
    () => currentCommentaryBookId.value,
    () => scrollToCurrentCommentary()
)

defineExpose({
    commentaryContentRef,
    commentaryGroups,
    // Search methods
    performCommentarySearch,
    nextCommentarySearchMatch,
    previousCommentarySearchMatch,
    clearCommentarySearch,
    // Search state - expose as getters for reactivity
    get currentMatchIndex() {
        return commentarySearchCurrentMatchIndex.value
    },
    get totalMatches() {
        return commentarySearchTotalMatches.value
    }
})
</script>

<style scoped>
.commentary-tree-panel {
    flex: 0 1 auto;
    max-width: 35%;
    overflow: hidden;
    border-right: 1px solid var(--border-color, #ddd);
}
</style>
