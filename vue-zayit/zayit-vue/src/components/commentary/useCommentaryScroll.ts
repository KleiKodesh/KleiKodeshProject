import { computed, nextTick, type Ref } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { hasConnections } from '@/data/types/Book'
import type { CommentaryTreeNode } from './useCommentaryTree'

// Scroll position persistence for virtualized commentary list
// SAVE: 1) Get scrollOffset 2) Find group index at that offset 3) Calculate offset within group 4) Store to tab state
// RESTORE: 1) Read saved index and offset 2) Scroll to saved group 3) Apply saved offset within that group
// MAGIC: Works despite virtualization by saving relative position (group + offset), not absolute pixels

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

    // Find which group index contains a given scroll offset (only searches ~20-50 rendered groups)
    function findGroupIndex(scrollOffset: number): number | undefined {
        if (!vListRef.value) return undefined
        const container = vListRef.value.$el as HTMLElement
        if (!container) return undefined
        const items = Array.from(container.querySelectorAll('[data-group-index]'))
        if (items.length === 0) return undefined

        for (let i = 0; i < items.length; i++) {
            const item = items[i] as HTMLElement
            const groupIndex = parseInt(item.getAttribute('data-group-index') || '0', 10)
            const itemOffset = vListRef.value.getItemOffset(groupIndex)
            const itemHeight = item.offsetHeight
            if (scrollOffset >= itemOffset && scrollOffset < itemOffset + itemHeight) return groupIndex
        }
        return undefined
    }

    function saveScrollPosition() {
        if (!vListRef.value || !tabStore.activeTab?.bookState) return
        const scrollOffset = vListRef.value.scrollOffset
        const topGroupIndex = findGroupIndex(scrollOffset)
        if (topGroupIndex === undefined || topGroupIndex === null) return
        const itemOffset = vListRef.value.getItemOffset(topGroupIndex)
        const offset = scrollOffset - itemOffset
        tabStore.activeTab.bookState.commentaryScrollElementIndex = topGroupIndex
        tabStore.activeTab.bookState.commentaryScrollOffset = offset
    }

    async function restoreScrollPosition() {
        if (!vListRef.value || !tabStore.activeTab?.bookState) return
        const savedIndex = tabStore.activeTab.bookState.commentaryScrollElementIndex
        const savedOffset = tabStore.activeTab.bookState.commentaryScrollOffset || 10
        if (savedIndex === undefined) return

        await nextTick()
        vListRef.value.scrollToIndex(savedIndex, { align: 'start' })
        await nextTick()
        await new Promise(resolve => setTimeout(resolve, 500))

        const itemOffset = vListRef.value.getItemOffset(savedIndex)
        const targetPosition = itemOffset + savedOffset
        vListRef.value.scrollTo(targetPosition)
        await nextTick()
        await new Promise(resolve => setTimeout(resolve, 600))
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
        const centerGroupIndex = findGroupIndex(centerOffset)
        if (centerGroupIndex === undefined) return
        const centerGroup = virtualGroups.value[centerGroupIndex]
        if (centerGroup?.bookNode?.bookId) {
            emit('visible-book-changed', centerGroup.bookNode.bookId)
        }
    }

    function handleGroupClick(node: CommentaryTreeNode) {
        if (node.bookId !== undefined && node.lineIndex !== undefined) {
            const targetBook = categoryTreeStore.allBooks.find(book => book.id === node.bookId)
            const targetHasConnections = targetBook ? hasConnections(targetBook) : false
            tabStore.openBookInNewTab(node.hebrewName, node.bookId, targetHasConnections, node.lineIndex, true)
        }
    }

    return { currentZoom, containerStyles, saveScrollPosition, restoreScrollPosition, scrollToGroup, detectVisibleGroup, handleGroupClick }
}
