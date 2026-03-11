import { ref, computed, watch, nextTick } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import { useCommentaryContent } from './useCommentaryContent'
import type { Book } from '@/data/types/Book'
import type { CommentaryTreeNode } from './useCommentaryTree'

/**
 * Retry scrolling to a commentary group with exponential backoff
 * This handles cases where the DOM isn't ready yet
 */
async function retryScrollToGroup(
    bookId: number,
    scrollToGroup: (bookId: number) => Promise<void>,
    maxRetries = 10,
    delayMs = 50
): Promise<boolean> {
    for (let i = 0; i < maxRetries; i++) {
        await nextTick()
        try {
            await scrollToGroup(bookId)
            return true
        } catch (error) {
            if (i === maxRetries - 1) {
                console.warn(`Failed to scroll to commentary ${bookId} after ${maxRetries} retries`)
                return false
            }
            await new Promise(resolve => setTimeout(resolve, delayMs))
        }
    }
    return false
}

export function useCommentaryView(props: {
    bookId?: number
    selectedLineIndex?: number
    book?: Book
}) {
    const tabStore = useTabStore()
    const selectedBookId = ref<number>()
    const hasInitialized = ref(false)
    const previousLineIndex = ref<number>()

    const { commentaryGroups, isLoadingMetadata, isLoadingMore, loadingProgress, loadCommentaryMetadata, loadGroupContent, queueGroupLoad } = useCommentaryContent()

    const selectedConnectionTypeId = computed(() =>
        tabStore.activeTab?.bookState?.commentaryFilterConnectionTypeId
    )

    const selectedTocEntryId = computed(() =>
        tabStore.activeTab?.bookState?.selectedTocEntryId
    )

    // Current commentary book ID from store (single source of truth)
    const currentCommentaryBookId = computed({
        get: () => tabStore.activeTab?.bookState?.currentCommentaryBookId,
        set: (value) => {
            if (tabStore.activeTab?.bookState) {
                tabStore.activeTab.bookState.currentCommentaryBookId = value
            }
        }
    })

    function handleSelectGroup(node: CommentaryTreeNode) {
        if (node.type === 'book' && node.bookId !== undefined) {
            selectedBookId.value = node.bookId
            return node.bookId
        }
    }

    function handleVisibleBookChanged(bookId: number) {
        console.log('[Commentary] Visible book changed to:', bookId)
        selectedBookId.value = bookId
        currentCommentaryBookId.value = bookId
    }

    async function initializeCommentary() {
        const bookId = props.bookId
        const lineIndex = props.selectedLineIndex
        const connectionTypeId = selectedConnectionTypeId.value
        const tocEntryId = selectedTocEntryId.value
        const isVisible = tabStore.activeTab?.bookState?.showBottomPane || false

        if (bookId !== undefined && lineIndex !== undefined) {
            console.log('[Commentary] Loading metadata for line:', lineIndex, 'Current book:', currentCommentaryBookId.value)

            await loadCommentaryMetadata(bookId, lineIndex, connectionTypeId, tocEntryId, isVisible)

            if (commentaryGroups.value.length > 0) {
                console.log('[Commentary] Loaded groups:', commentaryGroups.value.map(g => ({ name: g.groupName, id: g.targetBookId })))

                // If no current book is set, initialize to default or first
                if (!currentCommentaryBookId.value || !hasInitialized.value) {
                    const defaultBookId = props.book?.defaultCommentatorBookId
                    const firstBookId = commentaryGroups.value[0]?.targetBookId
                    currentCommentaryBookId.value = defaultBookId || firstBookId
                    console.log('[Commentary] Initialized to:', currentCommentaryBookId.value)
                    hasInitialized.value = true
                }

                // Check if current book exists in new commentary list
                const currentExists = commentaryGroups.value.some(
                    group => group.targetBookId === currentCommentaryBookId.value
                )

                console.log('[Commentary] Current book exists in new line:', currentExists, 'Book ID:', currentCommentaryBookId.value)

                // If current book doesn't exist, fall back to default or first
                if (!currentExists) {
                    const defaultBookId = props.book?.defaultCommentatorBookId
                    const firstBookId = commentaryGroups.value[0]?.targetBookId
                    currentCommentaryBookId.value = defaultBookId || firstBookId
                    console.log('[Commentary] Fell back to:', currentCommentaryBookId.value)
                }

                // Trigger scroll by updating selectedBookId (watcher will handle scroll)
                selectedBookId.value = currentCommentaryBookId.value
            }

            previousLineIndex.value = lineIndex
        }
    }

    // Set current commentary book (used by all interactions)
    function setCurrentCommentary(bookId: number) {
        currentCommentaryBookId.value = bookId
        selectedBookId.value = bookId
    }

    return {
        commentaryGroups,
        isLoadingMetadata,
        isLoadingMore,
        loadingProgress,
        selectedBookId,
        selectedConnectionTypeId,
        selectedTocEntryId,
        currentCommentaryBookId,
        handleSelectGroup,
        handleVisibleBookChanged,
        initializeCommentary,
        setCurrentCommentary,
        loadGroupContent,
        queueGroupLoad
    }
}
