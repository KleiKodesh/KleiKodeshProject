/**
 * Line View Scroll Composable
 * Handles scrolling to lines and global search highlighting
 */

import { ref, nextTick, type Ref } from 'vue'
import type { DynamicScroller } from 'vue-virtual-scroller'
import { scrollToElement } from '@/components/shared/useScrollToElement'

export function useLineViewScroll(
    scrollerRef: Ref<InstanceType<typeof DynamicScroller> | null>
) {
    const globalSearchHighlightLineIndex = ref<number | null>(null)
    const globalSearchTerms = ref<string>('')
    const globalSearchSnippet = ref<string>('')

    /**
     * Scroll to a specific line with optional pixel offset
     */
    async function scrollToLine(lineIndex: number, pixelOffset?: number) {
        if (!scrollerRef.value) return

        await nextTick()

        const scrollerEl = scrollerRef.value.$el as HTMLElement | undefined
        if (!scrollerEl) return

        // Hide scrolling during the double-call hack
        scrollerEl.style.overflow = 'hidden'
        scrollerEl.style.pointerEvents = 'none'

        try {
            // First call
            scrollerRef.value.scrollToItem(lineIndex)

            // Second call after delay
            setTimeout(() => {
                if (scrollerRef.value && scrollerEl) {
                    scrollerRef.value.scrollToItem(lineIndex)

                    // Apply pixel offset if provided
                    if (pixelOffset !== undefined && pixelOffset !== 0) {
                        setTimeout(() => {
                            if (scrollerEl) {
                                scrollerEl.scrollTop = scrollerEl.scrollTop - pixelOffset
                            }
                        }, 20)
                    }

                    // Re-enable scrolling
                    setTimeout(() => {
                        if (scrollerEl) {
                            scrollerEl.style.overflow = ''
                            scrollerEl.style.pointerEvents = ''
                        }
                    }, pixelOffset !== undefined ? 30 : 10)
                }
            }, 50)

        } catch (error) {
            scrollerEl.style.overflow = ''
            scrollerEl.style.pointerEvents = ''
        }
    }

    /**
     * Scroll to the first highlighted word in a line
     */
    async function scrollToFirstHighlightedWord(lineIndex: number) {
        const scrollerEl = scrollerRef.value?.$el
        if (!scrollerEl) return

        // Find the line element
        const lineEl = scrollerEl.querySelector(`[data-index="${lineIndex}"]`)
        if (!lineEl) return

        // Try to find the snippet background first (preferred)
        let targetElement = lineEl.querySelector('.global-search-snippet-bg')

        // Fallback to first highlighted word if no snippet background
        if (!targetElement) {
            targetElement = lineEl.querySelector('.global-search-highlight')
        }

        if (!targetElement) return

        // Use scrollToElement utility for consistent behavior
        await scrollToElement(targetElement as HTMLElement)
    }

    /**
     * Add fade animation class once (prevents re-triggering on DOM updates)
     */
    function addFadeAnimationOnce(lineIndex: number) {
        const scrollerEl = scrollerRef.value?.$el
        if (!scrollerEl) return

        const lineEl = scrollerEl.querySelector(`[data-index="${lineIndex}"]`)
        if (!lineEl) return

        const snippetBg = lineEl.querySelector('.global-search-snippet-bg')
        if (!snippetBg) return

        // Add animation class
        snippetBg.classList.add('fade-animation')

        // Remove class after animation completes (3s)
        setTimeout(() => {
            snippetBg.classList.remove('fade-animation')
        }, 3000)
    }

    /**
     * Scroll to line and highlight search terms with fade animation
     */
    async function scrollToLineWithFadeHighlight(
        lineIndex: number,
        searchTerms?: string,
        snippet?: string
    ) {
        // Set global search highlighting first so it's ready when line renders
        if (searchTerms) {
            globalSearchHighlightLineIndex.value = lineIndex
            globalSearchTerms.value = searchTerms
            globalSearchSnippet.value = snippet || ''
        }

        // Scroll to the line
        await scrollToLine(lineIndex)

        // Immediately scroll to the highlighted words after line is in view
        if (searchTerms) {
            await nextTick()
            await scrollToFirstHighlightedWord(lineIndex)

            // Add fade animation class once
            await nextTick()
            addFadeAnimationOnce(lineIndex)
        }
    }

    return {
        globalSearchHighlightLineIndex,
        globalSearchTerms,
        globalSearchSnippet,
        scrollToLine,
        scrollToLineWithFadeHighlight
    }
}
