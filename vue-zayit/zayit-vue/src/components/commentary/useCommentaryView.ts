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

    const { commentaryGroups, isLoadingMetadata, isLoadingMore, loadCommentaryMetadata, loadGroupContent, queueGroupLoad } = useCommentaryContent()

    const selectedConnectionTypeId = computed(() =>
        tabStore.activeTab?.bookState?.commentaryFilterConnectionTypeId
    )

    const selectedTocEntryId = computed(() =>
        tabStore.activeTab?.bookState?.selectedTocEntryId
    )

    function handleSelectGroup(node: CommentaryTreeNode) {
        if (node.type === 'book' && node.bookId !== undefined) {
            selectedBookId.value = node.bookId
            return node.bookId
        }
    }

    function handleVisibleBookChanged(bookId: number) {
        selectedBookId.value = bookId
        // Update the persisted current commentary book ID
        if (tabStore.activeTab?.bookState) {
            tabStore.activeTab.bookState.currentCommentaryBookId = bookId
        }
    }

    async function scrollToCommentary(bookId: number, scrollToGroup: (bookId: number) => Promise<void>) {
        selectedBookId.value = bookId
        // Update the persisted current commentary book ID
        if (tabStore.activeTab?.bookState) {
            tabStore.activeTab.bookState.currentCommentaryBookId = bookId
        }
        await retryScrollToGroup(bookId, scrollToGroup)
    }

    async function initializeCommentary(scrollToGroup: (bookId: number) => Promise<void>, restoreScrollPosition: (isFirstInit: boolean) => Promise<void>) {
        const bookId = props.bookId
        const lineIndex = props.selectedLineIndex
        const connectionTypeId = selectedConnectionTypeId.value
        const tocEntryId = selectedTocEntryId.value

        if (bookId !== undefined && lineIndex !== undefined) {
            await loadCommentaryMetadata(bookId, lineIndex, connectionTypeId, tocEntryId)

            if (!hasInitialized.value && commentaryGroups.value.length > 0) {
                hasInitialized.value = true
                const hasPersistedScroll = tabStore.activeTab?.bookState?.commentaryScrollElementIndex !== undefined

                if (!hasPersistedScroll) {
                    const defaultBookId = props.book?.defaultCommentatorBookId
                    const firstBookId = commentaryGroups.value[0]?.targetBookId
                    const targetBookId = defaultBookId || firstBookId

                    if (targetBookId) {
                        await scrollToCommentary(targetBookId, scrollToGroup)
                    }
                } else {
                    await nextTick()
                    await restoreScrollPosition(false)
                }
            } else if (commentaryGroups.value.length > 0) {
                const isLineChange = previousLineIndex.value !== undefined && previousLineIndex.value !== lineIndex

                if (isLineChange) {
                    // Get the currently selected commentary book ID
                    const currentBookId = tabStore.activeTab?.bookState?.currentCommentaryBookId || selectedBookId.value

                    if (currentBookId) {
                        // Check if the current commentary exists in the new line
                        const currentCommentaryExists = commentaryGroups.value.some(
                            group => group.targetBookId === currentBookId
                        )

                        if (currentCommentaryExists) {
                            // Stay on the current commentary
                            selectedBookId.value = currentBookId
                            await nextTick()
                            await scrollToGroup(currentBookId)
                        } else {
                            // Fall back to default or first
                            const defaultBookId = props.book?.defaultCommentatorBookId
                            const firstBookId = commentaryGroups.value[0]?.targetBookId
                            const targetBookId = defaultBookId || firstBookId

                            if (targetBookId) {
                                await scrollToCommentary(targetBookId, scrollToGroup)
                            }
                        }
                    } else {
                        // No current commentary, use default or first
                        const defaultBookId = props.book?.defaultCommentatorBookId
                        const firstBookId = commentaryGroups.value[0]?.targetBookId
                        const targetBookId = defaultBookId || firstBookId

                        if (targetBookId) {
                            await scrollToCommentary(targetBookId, scrollToGroup)
                        }
                    }
                } else {
                    await nextTick()
                    await restoreScrollPosition(false)
                }
            }

            previousLineIndex.value = lineIndex
        }
    }

    return {
        commentaryGroups,
        isLoadingMetadata,
        isLoadingMore,
        selectedBookId,
        selectedConnectionTypeId,
        selectedTocEntryId,
        handleSelectGroup,
        handleVisibleBookChanged,
        initializeCommentary,
        scrollToCommentary,
        loadGroupContent,
        queueGroupLoad
    }
}
