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
                           @visible-book-changed="handleVisibleBookChanged"
                           @navigate-previous-line="(bookId) => emit('navigate-previous-line', bookId)"
                           @navigate-next-line="(bookId) => emit('navigate-next-line', bookId)"
                           @select-commentary="onSelectCommentary"
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

const composableStart = performance.now()
const {
    commentaryGroups,
    isLoadingMetadata,
    loadingProgress,
    selectedBookId,
    selectedConnectionTypeId,
    selectedTocEntryId,
    handleSelectGroup,
    handleVisibleBookChanged,
    initializeCommentary,
    scrollToCommentary,
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
    if (bookId) commentaryContentRef.value?.scrollToGroup(bookId)
}

async function onSelectCommentary(bookId: number) {
    if (commentaryContentRef.value?.scrollToGroup) {
        await scrollToCommentary(bookId, (id) => commentaryContentRef.value!.scrollToGroup(id))
        // Move focus back to content after navigation
        commentaryContentRef.value?.focusContent()
    }
}

async function handleToggleTree() {
    showTree.value = !showTree.value
    if (showTree.value) {
        await nextTick()
        commentaryTreePanelRef.value?.scrollToSelected()
    }
}

watch(
    () => [props.bookId, props.selectedLineIndex, selectedConnectionTypeId.value, selectedTocEntryId.value] as const,
    () => initializeCommentary(
        (bookId) => commentaryContentRef.value?.scrollToGroup(bookId),
        (isFirstInit) => commentaryContentRef.value?.restoreScrollPosition(isFirstInit, queueGroupLoad)
    ),
    { immediate: true }
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
