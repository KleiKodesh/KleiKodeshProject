<template>
    <div class="flex-row height-fill">
        <CommentaryTreePanel :commentary-groups="commentaryGroups"
                             :selected-book-id="selectedBookId"
                             class="commentary-tree-panel"
                             @select="onSelectGroup" />

        <CommentaryContent ref="commentaryContentRef"
                           :commentary-groups="commentaryGroups"
                           :is-loading-metadata="isLoadingMetadata"
                           :book-id="props.bookId"
                           :selected-line-index="props.selectedLineIndex"
                           :connection-type-id="selectedConnectionTypeId"
                           class="flex-110"
                           @visible-book-changed="handleVisibleBookChanged"
                           @navigate-previous-line="(bookId) => emit('navigate-previous-line', bookId)"
                           @navigate-next-line="(bookId) => emit('navigate-next-line', bookId)"
                           @select-commentary="onSelectCommentary" />
    </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import CommentaryContent from './CommentaryContent.vue'
import CommentaryTreePanel from './CommentaryTreePanel.vue'
import { useCommentaryView } from './useCommentaryView'
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

const {
    commentaryGroups,
    isLoadingMetadata,
    selectedBookId,
    selectedConnectionTypeId,
    handleSelectGroup,
    handleVisibleBookChanged,
    initializeCommentary,
    scrollToCommentary
} = useCommentaryView(props)

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

watch(
    () => [props.bookId, props.selectedLineIndex, selectedConnectionTypeId.value] as const,
    () => initializeCommentary(
        (bookId) => commentaryContentRef.value?.scrollToGroup(bookId),
        (isFirstInit) => commentaryContentRef.value?.restoreScrollPosition(isFirstInit)
    ),
    { immediate: true }
)
</script>

<style scoped>
.commentary-tree-panel {
    flex: 0 1 auto;
    max-width: 30%;
    overflow: hidden;
    border-right: 1px solid var(--border-color, #ddd);
}
</style>
