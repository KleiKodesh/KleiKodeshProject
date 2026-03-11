/**
 * Line View Scroll Position Composable
 * Handles scroll position persistence, restoration, and navigation for line view
 * Uses simple index-based restoration (reliable with virtualization)
 */

import { computed, nextTick, type Ref } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import type { VirtualizerHandle } from 'virtua/vue'

export function useLineViewScrollPosition(virtuaRef: Ref<VirtualizerHandle | null>, tabId: Ref<number | undefined>) {
    const tabStore = useTabStore()

    const currentZoom = computed(() => tabStore.activeTab?.bookState?.zoom || 100)

    const containerStyles = computed(() => ({
        backgroundColor: 'var(--reading-bg-primary)',
        color: 'var(--reading-text-primary)',
        fontSize: `calc(var(--font-size, 100%) * ${currentZoom.value / 100})`
    }))

    function saveScrollPosition() {
        if (!virtuaRef.value || !tabId.value) return

        const tab = tabStore.tabs.find(t => t.id === tabId.value)
        if (!tab?.bookState) return

        // Save the top visible line index
        const scrollOffset = virtuaRef.value.scrollOffset
        const topLineIndex = virtuaRef.value.findItemIndex(scrollOffset)

        tab.bookState.lineScrollElementIndex = topLineIndex
    }

    async function restoreScrollPosition() {
        if (!virtuaRef.value || !tabId.value) return

        const tab = tabStore.tabs.find(t => t.id === tabId.value)
        if (!tab?.bookState) return

        const savedLineIndex = tab.bookState.lineScrollElementIndex
        if (savedLineIndex !== undefined && savedLineIndex >= 0) {
            await nextTick()
            virtuaRef.value.scrollToIndex(savedLineIndex, { align: 'start' })
        }
    }

    async function scrollToLine(lineIndex: number) {
        if (!virtuaRef.value) return

        await nextTick()
        virtuaRef.value.scrollToIndex(lineIndex, { align: 'start' })
    }

    function detectVisibleLine(emit: (event: 'centerLineChanged', lineIndex: number) => void) {
        if (!virtuaRef.value) return

        const scrollOffset = virtuaRef.value.scrollOffset
        const viewportSize = virtuaRef.value.viewportSize
        const centerOffset = scrollOffset + viewportSize / 2
        const centerLineIndex = virtuaRef.value.findItemIndex(centerOffset)

        emit('centerLineChanged', centerLineIndex)
    }

    return {
        containerStyles,
        saveScrollPosition,
        restoreScrollPosition,
        scrollToLine,
        detectVisibleLine
    }
}
