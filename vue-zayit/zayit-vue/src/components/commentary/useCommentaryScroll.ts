import { computed, nextTick, type Ref } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { hasConnections } from '@/data/types/Book'
import type { CommentaryTreeNode } from './useCommentaryTree'

export function useCommentaryScroll(
    scrollContainer: Ref<HTMLElement | null>,
    vListRef: Ref<any>,
    virtualGroups: Ref<any[]>
) {
    const tabStore = useTabStore()
    const categoryTreeStore = useCategoryTreeStore()

    const currentZoom = computed(() => tabStore.activeTab?.bookState?.zoom || 100)

    const containerStyles = computed(() => ({
        fontSize: `calc(var(--commentary-font-size, 100%) * ${currentZoom.value / 100})`
    }))

    function saveScrollPosition() {
        if (!vListRef.value || !tabStore.activeTab?.bookState) return

        const scrollOffset = vListRef.value.scrollOffset
        const topGroupIndex = vListRef.value.findItemIndex(scrollOffset)

        tabStore.activeTab.bookState.commentaryScrollElementIndex = topGroupIndex
    }

    async function restoreScrollPosition() {
        if (!vListRef.value || !tabStore.activeTab?.bookState) return

        const savedGroupIndex = tabStore.activeTab.bookState.commentaryScrollElementIndex
        if (savedGroupIndex !== undefined && savedGroupIndex >= 0) {
            await nextTick()
            vListRef.value.scrollToIndex(savedGroupIndex, { align: 'start' })
        }
    }

    async function scrollToGroup(bookId: number) {
        if (!vListRef.value) return

        const targetIndex = virtualGroups.value.findIndex(g => g.bookNode.bookId === bookId)
        if (targetIndex === -1) return

        await nextTick()
        vListRef.value.scrollToIndex(targetIndex, { align: 'start' })
    }

    function detectVisibleGroup(emit: (event: 'visible-book-changed', bookId: number) => void) {
        if (!vListRef.value) return

        const scrollOffset = vListRef.value.scrollOffset
        const viewportSize = vListRef.value.viewportSize
        const centerOffset = scrollOffset + viewportSize / 2
        const centerGroupIndex = vListRef.value.findItemIndex(centerOffset)

        const centerGroup = virtualGroups.value[centerGroupIndex]
        if (centerGroup?.bookNode?.bookId) {
            emit('visible-book-changed', centerGroup.bookNode.bookId)
        }
    }

    function handleGroupClick(node: CommentaryTreeNode) {
        if (node.bookId !== undefined && node.lineIndex !== undefined) {
            const targetBook = categoryTreeStore.allBooks.find(book => book.id === node.bookId)
            const targetHasConnections = targetBook ? hasConnections(targetBook) : false

            tabStore.openBookInNewTab(
                node.hebrewName,
                node.bookId,
                targetHasConnections,
                node.lineIndex,
                true
            )
        }
    }

    return {
        currentZoom,
        containerStyles,
        saveScrollPosition,
        restoreScrollPosition,
        scrollToGroup,
        detectVisibleGroup,
        handleGroupClick
    }
}
