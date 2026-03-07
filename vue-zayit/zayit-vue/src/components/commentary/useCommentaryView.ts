import { ref, computed, watch, nextTick } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import { useCommentaryContent } from './useCommentaryContent'
import type { Book } from '@/data/types/Book'
import type { CommentaryTreeNode } from './useCommentaryTree'

export function useCommentaryView(props: {
    bookId?: number
    selectedLineIndex?: number
    book?: Book
}) {
    const tabStore = useTabStore()
    const selectedBookId = ref<number>()
    const hasInitialized = ref(false)
    const previousLineIndex = ref<number>()

    const { commentaryGroups, isLoadingMetadata, loadCommentaryMetadata } = useCommentaryContent()

    const selectedConnectionTypeId = computed(() =>
        tabStore.activeTab?.bookState?.commentaryFilterConnectionTypeId
    )

    function handleSelectGroup(node: CommentaryTreeNode) {
        if (node.type === 'book' && node.bookId !== undefined) {
            selectedBookId.value = node.bookId
            return node.bookId
        }
    }

    function handleVisibleBookChanged(bookId: number) {
        selectedBookId.value = bookId
    }

    async function initializeCommentary(scrollToGroup: (bookId: number) => Promise<void>, restoreScrollPosition: (isFirstInit: boolean) => Promise<void>) {
        const bookId = props.bookId
        const lineIndex = props.selectedLineIndex
        const connectionTypeId = selectedConnectionTypeId.value

        if (bookId !== undefined && lineIndex !== undefined) {
            await loadCommentaryMetadata(bookId, lineIndex, connectionTypeId)

            if (!hasInitialized.value && commentaryGroups.value.length > 0) {
                hasInitialized.value = true
                const hasPersistedScroll = tabStore.activeTab?.bookState?.commentaryScrollElementIndex !== undefined

                if (!hasPersistedScroll) {
                    const defaultBookId = props.book?.defaultCommentatorBookId
                    const firstBookId = commentaryGroups.value[0]?.targetBookId
                    const targetBookId = defaultBookId || firstBookId

                    if (targetBookId) {
                        selectedBookId.value = targetBookId
                        for (let i = 0; i < 10; i++) {
                            await nextTick()
                            if (scrollToGroup) {
                                await scrollToGroup(targetBookId)
                                break
                            }
                            await new Promise(resolve => setTimeout(resolve, 50))
                        }
                    }
                } else {
                    await nextTick()
                    await restoreScrollPosition(false)
                }
            } else if (commentaryGroups.value.length > 0) {
                const isLineChange = previousLineIndex.value !== undefined && previousLineIndex.value !== lineIndex

                if (isLineChange && selectedBookId.value) {
                    const currentCommentaryExists = commentaryGroups.value.some(
                        group => group.targetBookId === selectedBookId.value
                    )

                    if (currentCommentaryExists) {
                        await nextTick()
                        await scrollToGroup(selectedBookId.value)
                    } else {
                        const defaultBookId = props.book?.defaultCommentatorBookId
                        const firstBookId = commentaryGroups.value[0]?.targetBookId
                        const targetBookId = defaultBookId || firstBookId

                        if (targetBookId) {
                            selectedBookId.value = targetBookId
                            await nextTick()
                            await scrollToGroup(targetBookId)
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
        selectedBookId,
        selectedConnectionTypeId,
        handleSelectGroup,
        handleVisibleBookChanged,
        initializeCommentary
    }
}
