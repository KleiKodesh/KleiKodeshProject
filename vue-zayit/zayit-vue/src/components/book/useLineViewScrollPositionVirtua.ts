import { computed, nextTick, type Ref, type ComputedRef } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'

// Scroll position persistence for virtualized lists
// SAVE: 1) Get current scrollOffset from Virtua 2) Find which item index contains that offset 3) Calculate offset within that item 4) Store both to tab state
// RESTORE: 1) Read saved index and offset from tab state 2) Scroll to saved index 3) Apply saved offset within that item
// MAGIC: Works despite virtualization because we save relative position (item + offset), not absolute pixels

export function useLineViewScrollPositionVirtua(
    virtuaRef: Ref<any>,
    tabId: ComputedRef<number | undefined>
) {
    const tabStore = useTabStore()
    const myTab = computed(() => tabStore.tabs.find(t => t.id === tabId.value))
    const containerStyles = computed(() => ({ fontSize: `${myTab.value?.bookState?.zoom || 100}%` }))

    // Find which item index contains a given scroll offset (only searches ~20-50 rendered items, not all 10k lines)
    function findItemIndex(scrollOffset: number): number | undefined {
        if (!virtuaRef.value) return undefined
        const container = virtuaRef.value.$el as HTMLElement
        if (!container) return undefined
        const items = Array.from(container.querySelectorAll('[data-line-index]'))
        if (items.length === 0) return undefined

        for (let i = 0; i < items.length; i++) {
            const item = items[i] as HTMLElement
            const itemIndex = parseInt(item.getAttribute('data-line-index') || '0', 10)
            const itemOffset = virtuaRef.value.getItemOffset(itemIndex)
            const itemHeight = item.offsetHeight
            if (scrollOffset >= itemOffset && scrollOffset < itemOffset + itemHeight) return itemIndex
        }
        return undefined
    }

    function saveScrollPosition() {
        if (!virtuaRef.value || !myTab.value?.bookState) return
        const scrollOffset = virtuaRef.value.scrollOffset
        const topItemIndex = findItemIndex(scrollOffset)
        if (topItemIndex === undefined || topItemIndex === null) return
        const itemOffset = virtuaRef.value.getItemOffset(topItemIndex)
        const offset = scrollOffset - itemOffset
        myTab.value.bookState.lineScrollElementIndex = topItemIndex
        myTab.value.bookState.lineScrollOffset = offset
    }

    async function restoreScrollPosition() {
        if (!virtuaRef.value || !myTab.value?.bookState) return
        const savedIndex = myTab.value.bookState.lineScrollElementIndex
        const savedOffset = myTab.value.bookState.lineScrollOffset || 10
        if (savedIndex === undefined) return
        const container = virtuaRef.value.$el as HTMLElement
        if (!container || !container.parentElement) return

        await nextTick()
        virtuaRef.value.scrollToIndex(savedIndex, { align: 'start' })
        await nextTick()
        await new Promise(resolve => setTimeout(resolve, 500))

        const itemOffset = virtuaRef.value.getItemOffset(savedIndex)
        const targetPosition = itemOffset + savedOffset
        virtuaRef.value.scrollTo(targetPosition)
        await nextTick()
        await new Promise(resolve => setTimeout(resolve, 600))
    }

    async function scrollToLine(lineIndex: number) {
        if (!virtuaRef.value) return
        await nextTick()
        virtuaRef.value.scrollToIndex(lineIndex, { align: 'start' })
        await nextTick()
    }

    function detectVisibleLine(emit: any) {
        if (!virtuaRef.value) return
        const viewportSize = virtuaRef.value.viewportSize
        const scrollOffset = virtuaRef.value.scrollOffset
        const centerOffset = scrollOffset + viewportSize / 2
        const centerLine = findItemIndex(centerOffset)
        emit('centerLineChanged', centerLine)
    }

    return { containerStyles, saveScrollPosition, restoreScrollPosition, scrollToLine, detectVisibleLine, findItemIndex }
}
