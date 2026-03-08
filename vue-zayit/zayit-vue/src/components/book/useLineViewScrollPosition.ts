/**
 * Line View Scroll Position Composable
 * Handles scroll position persistence, restoration, and navigation for line view
 * Uses CSS content-visibility virtualization approach
 */

import { ref, computed, type Ref } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { scrollToElement, scrollToElementTop } from '@/components/shared/useScrollToElement'
import { useScrollPosition } from '@/components/shared/useScrollPosition'

export function useLineViewScrollPosition(scrollContainer: Ref<HTMLElement | null>, tabId: Ref<number | undefined>) {
    const tabStore = useTabStore()
    const settingsStore = useSettingsStore()

    const currentZoom = computed(() => tabStore.activeTab?.bookState?.zoom || 100)

    const containerStyles = computed(() => ({
        backgroundColor: 'var(--reading-bg-primary)',
        color: 'var(--reading-text-primary)',
        fontSize: `calc(var(--font-size, 100%) * ${currentZoom.value / 100})`
    }))

    const intrinsicSize = computed(() => {
        const zoom = currentZoom.value / 100
        const baseFontSize = 16
        const lineHeight = settingsStore.linePadding || 1.6
        const estimatedHeight = baseFontSize * lineHeight * zoom * 2
        return `auto ${Math.round(estimatedHeight)}px`
    })

    const { saveScrollPosition, restoreScrollPosition } = useScrollPosition(scrollContainer, {
        stateKey: 'lineScrollElementIndex',
        offsetKey: 'lineScrollOffset',
        elementSelector: '[data-line-index]',
        tabId
    })

    async function scrollToLine(lineIndex: number) {
        if (!scrollContainer.value) return
        const lineElement = scrollContainer.value.querySelector(`[data-line-index="${lineIndex}"]`) as HTMLElement
        if (!lineElement) return

        const originalVisibility = lineElement.style.contentVisibility
        lineElement.style.contentVisibility = 'visible'

        await new Promise(resolve => requestAnimationFrame(resolve))
        await scrollToElementTop(lineElement)

        await new Promise(resolve => requestAnimationFrame(resolve))
        const containerRect = scrollContainer.value.getBoundingClientRect()
        const elementRect = lineElement.getBoundingClientRect()
        if (Math.abs(elementRect.top - containerRect.top) >= 5) {
            await scrollToElementTop(lineElement)
        }

        lineElement.style.contentVisibility = originalVisibility
    }

    function detectVisibleLine(emit: (event: 'centerLineChanged', lineIndex: number) => void) {
        if (!scrollContainer.value) return
        const containerRect = scrollContainer.value.getBoundingClientRect()
        const centerY = containerRect.top + containerRect.height / 2
        const lines = scrollContainer.value.querySelectorAll('[data-line-index]')

        for (const line of lines) {
            const rect = line.getBoundingClientRect()
            if (rect.top <= centerY && rect.bottom > centerY) {
                const lineIndex = parseInt(line.getAttribute('data-line-index') || '0')
                emit('centerLineChanged', lineIndex)
                break
            }
        }
    }

    return {
        containerStyles,
        intrinsicSize,
        saveScrollPosition,
        restoreScrollPosition,
        scrollToLine,
        detectVisibleLine
    }
}
