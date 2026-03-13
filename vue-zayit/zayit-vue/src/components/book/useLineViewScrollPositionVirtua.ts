import { computed, nextTick, type Ref, type ComputedRef } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'

export function useLineViewScrollPositionVirtua(
    virtuaRef: Ref<any>,
    tabId: ComputedRef<number | undefined>
) {
    const tabStore = useTabStore()
    const myTab = computed(() => tabStore.tabs.find(t => t.id === tabId.value))

    const containerStyles = computed(() => ({
        fontSize: `${myTab.value?.bookState?.zoom || 100}%`
    }))

    // Helper to find item index at a given scroll offset
    function findItemIndex(scrollOffset: number): number | undefined {
        if (!virtuaRef.value) return undefined

        // Binary search to find the item at the scroll offset
        const container = virtuaRef.value.$el as HTMLElement
        if (!container) return undefined

        const items = Array.from(container.querySelectorAll('[data-line-index]'))
        if (items.length === 0) return undefined

        // Find the first item whose bottom edge is below the scroll offset
        for (let i = 0; i < items.length; i++) {
            const item = items[i] as HTMLElement
            const itemIndex = parseInt(item.getAttribute('data-line-index') || '0', 10)
            const itemOffset = virtuaRef.value.getItemOffset(itemIndex)
            const itemHeight = item.offsetHeight

            if (scrollOffset >= itemOffset && scrollOffset < itemOffset + itemHeight) {
                return itemIndex
            }
        }

        return undefined
    }

    function saveScrollPosition() {
        if (!virtuaRef.value || !myTab.value?.bookState) return

        const scrollOffset = virtuaRef.value.scrollOffset
        const topItemIndex = findItemIndex(scrollOffset)

        if (topItemIndex === undefined || topItemIndex === null) return

        // Calculate offset within the item using Virtua's API
        const itemOffset = virtuaRef.value.getItemOffset(topItemIndex)
        const offset = scrollOffset - itemOffset

        myTab.value.bookState.lineScrollElementIndex = topItemIndex
        myTab.value.bookState.lineScrollOffset = offset

        console.log('[ScrollPosition] SAVED - Index:', topItemIndex, 'Offset:', offset, 'TabId:', tabId.value)
    }

    async function restoreScrollPosition() {
        console.log('[ScrollPosition] restoreScrollPosition() called')

        if (!virtuaRef.value || !myTab.value?.bookState) return

        const savedIndex = myTab.value.bookState.lineScrollElementIndex
        const savedOffset = myTab.value.bookState.lineScrollOffset || 10

        if (savedIndex === undefined) return

        const container = virtuaRef.value.$el as HTMLElement
        if (!container || !container.parentElement) return

        await nextTick()

        console.log('[ScrollPosition] Calling scrollToIndex(' + savedIndex + ')')
        virtuaRef.value.scrollToIndex(savedIndex, { align: 'start' })

        await nextTick()
        await new Promise(resolve => setTimeout(resolve, 500))

        const itemOffset = virtuaRef.value.getItemOffset(savedIndex)
        const targetPosition = itemOffset + savedOffset

        console.log('[ScrollPosition] Item ' + savedIndex + ' - offset:', itemOffset, 'target (offset+' + savedOffset + '):', targetPosition)
        virtuaRef.value.scrollTo(targetPosition)

        await nextTick()
        await new Promise(resolve => setTimeout(resolve, 600))

        console.log('[ScrollPosition] ✅ RESTORED - Index:', savedIndex, 'Offset:', savedOffset)
    }

    async function scrollToLine(lineIndex: number) {
        console.log('[ScrollPosition] scrollToLine() called - Index:', lineIndex)
        if (!virtuaRef.value) {
            console.log('[ScrollPosition] No virtua ref - skipping scroll')
            return
        }
        await nextTick()
        virtuaRef.value.scrollToIndex(lineIndex, { align: 'start' })
        await nextTick()
        console.log('[ScrollPosition] ✅ Scrolled to line:', lineIndex)
    }

    function detectVisibleLine(emit: any) {
        if (!virtuaRef.value) return
        const viewportSize = virtuaRef.value.viewportSize
        const scrollOffset = virtuaRef.value.scrollOffset
        const centerOffset = scrollOffset + viewportSize / 2
        const centerLine = findItemIndex(centerOffset)
        emit('centerLineChanged', centerLine)
    }

    return {
        containerStyles,
        saveScrollPosition,
        restoreScrollPosition,
        scrollToLine,
        detectVisibleLine,
        findItemIndex
    }
}
