/**
 * Generic scroll position management composable
 * Handles scroll position persistence and restoration using element-based tracking
 * Reusable across different virtualized views (lines, commentary, etc.)
 */

import { type Ref } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import { scrollToElement } from './useScrollToElement'

export interface ScrollPositionState {
    elementIndex: number
    offset: number
}

export function useScrollPosition(
    scrollContainer: Ref<HTMLElement | null>,
    options: {
        stateKey: 'lineScrollElementIndex'
        offsetKey: 'lineScrollOffset'
        elementSelector: string
        tabId?: Ref<number | undefined>
    }
) {
    const tabStore = useTabStore()
    const { stateKey, offsetKey, elementSelector, tabId } = options

    function getTargetTab() {
        if (tabId?.value !== undefined) {
            return tabStore.tabs.find(t => t.id === tabId.value)
        }
        return tabStore.activeTab
    }

    function saveScrollPosition() {
        const targetTab = getTargetTab()
        if (!scrollContainer.value || !targetTab?.bookState) return

        const containerRect = scrollContainer.value.getBoundingClientRect()
        const topY = containerRect.top + 50

        const elements = scrollContainer.value.querySelectorAll(elementSelector)
        for (let i = 0; i < elements.length; i++) {
            const rect = (elements[i] as HTMLElement).getBoundingClientRect()
            if (rect.top <= topY && rect.bottom > topY) {
                targetTab.bookState[stateKey] = i
                targetTab.bookState[offsetKey] = topY - rect.top
                return
            }
        }
    }

    async function restoreScrollPosition(isFirstInit: boolean) {
        if (!scrollContainer.value || isFirstInit) return

        const targetTab = getTargetTab()
        const elementIndex = targetTab?.bookState?.[stateKey]
        const offset = targetTab?.bookState?.[offsetKey]
        if (elementIndex === undefined) return

        let targetElement: HTMLElement | null = null
        for (let attempt = 0; attempt < 10; attempt++) {
            await new Promise(resolve => requestAnimationFrame(resolve))
            const elements = scrollContainer.value.querySelectorAll(elementSelector)
            if (elementIndex < elements.length) {
                targetElement = elements[elementIndex] as HTMLElement
                break
            }
            if (attempt < 9) await new Promise(resolve => setTimeout(resolve, 50))
        }

        if (!targetElement) return

        const originalVisibility = targetElement.style.contentVisibility
        targetElement.style.contentVisibility = 'visible'

        await new Promise(resolve => requestAnimationFrame(resolve))
        await new Promise(resolve => requestAnimationFrame(resolve))
        await scrollToElement(targetElement, { block: 'nearest' })

        if (offset !== undefined && offset !== 0) {
            for (let i = 0; i < 5; i++) {
                await new Promise(resolve => requestAnimationFrame(resolve))
                const containerRect = scrollContainer.value.getBoundingClientRect()
                const elementRect = targetElement.getBoundingClientRect()
                const adjustment = offset - (containerRect.top + 50 - elementRect.top)
                if (Math.abs(adjustment) < 2) break
                scrollContainer.value.scrollTop += adjustment
                if (i < 4) await new Promise(resolve => setTimeout(resolve, 20))
            }
        }

        targetElement.style.contentVisibility = originalVisibility
    }

    return {
        saveScrollPosition,
        restoreScrollPosition
    }
}
