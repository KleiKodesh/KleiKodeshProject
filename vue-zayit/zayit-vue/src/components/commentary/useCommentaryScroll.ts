import { ref, computed, type Ref } from 'vue'
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
        if (!scrollContainer.value || !tabStore.activeTab?.bookState) return

        const containerRect = scrollContainer.value.getBoundingClientRect()
        const topY = containerRect.top + 50

        const groups = scrollContainer.value.querySelectorAll('[data-book-id]')
        for (const group of groups) {
            const rect = group.getBoundingClientRect()
            if (rect.top <= topY && rect.bottom > topY) {
                const bookId = parseInt(group.getAttribute('data-book-id') || '0')
                tabStore.activeTab.bookState.currentCommentaryBookId = bookId
                return
            }
        }
    }

    async function restoreScrollPosition(isFirstInit: boolean) {
        if (!vListRef.value || isFirstInit) return

        const bookId = tabStore.activeTab?.bookState?.currentCommentaryBookId
        if (!bookId) return

        const targetIndex = virtualGroups.value.findIndex(g => g.bookNode.bookId === bookId)
        if (targetIndex === -1) return

        vListRef.value.scrollToIndex(targetIndex, { align: 'start' })
    }

    async function scrollToGroup(bookId: number) {
        if (!vListRef.value) return

        const targetIndex = virtualGroups.value.findIndex(g => g.bookNode.bookId === bookId)
        if (targetIndex === -1) return

        vListRef.value.scrollToIndex(targetIndex, { align: 'start' })
    }

    function detectVisibleGroup(emit: (event: 'visible-book-changed', bookId: number) => void) {
        if (!scrollContainer.value) {
            console.log('[Commentary] detectVisibleGroup - no scroll container')
            return
        }

        const topY = scrollContainer.value.getBoundingClientRect().top + 50
        const groups = scrollContainer.value.querySelectorAll('[data-book-id]')
        console.log('[Commentary] detectVisibleGroup - found groups:', groups.length, 'topY:', topY)

        for (const group of groups) {
            const rect = group.getBoundingClientRect()
            if (rect.top <= topY && rect.bottom > topY) {
                const bookId = parseInt(group.getAttribute('data-book-id') || '0')
                console.log('[Commentary] Found visible group:', bookId)
                if (bookId) {
                    emit('visible-book-changed', bookId)
                }
                break
            }
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
