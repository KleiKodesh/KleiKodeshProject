/**
 * Line View Center Line Observer Composable
 * Tracks the center line and updates TOC title
 */

import { ref, watch, onUnmounted, type Ref, type ComputedRef } from 'vue'
import type { TocEntry } from '@/data/types/BookToc'
import type { Tab } from '@/data/types/Tab'

export function useLineViewCenterObserver(
    scrollerElRef: Ref<HTMLElement | undefined>,
    myTab: ComputedRef<Tab | undefined>,
    flatTocEntries: Ref<TocEntry[] | undefined>,
    emit: (event: 'centerLineChanged', lineIndex: number) => void,
    emitTocEntry: (event: 'currentTocEntryChanged', tocEntryId: number | undefined) => void
) {
    const centerLineObserver = ref<IntersectionObserver | null>(null)
    const currentCenterLineIndex = ref<number | null>(null)

    function updateTabTitleWithToc(lineIndex: number) {
        if (!flatTocEntries.value || flatTocEntries.value.length === 0) return

        // Find the TOC entry for this line (skip alt TOC entries)
        let currentTocEntry: TocEntry | undefined
        for (let i = flatTocEntries.value.length - 1; i >= 0; i--) {
            const entry = flatTocEntries.value[i]
            if (entry && !entry.isAltToc && entry.lineIndex <= lineIndex) {
                currentTocEntry = entry
                break
            }
        }

        // Update tab title if we have a TOC entry
        if (currentTocEntry && myTab.value) {
            const bookTitle = myTab.value.bookState?.bookTitle || ''
            myTab.value.title = `${bookTitle} - ${currentTocEntry.text}`

            // Emit the current TOC entry ID
            emitTocEntry('currentTocEntryChanged', currentTocEntry.id)
        } else if (myTab.value?.bookState?.bookTitle) {
            // No TOC entry found, use just book title
            myTab.value.title = myTab.value.bookState.bookTitle
            emitTocEntry('currentTocEntryChanged', undefined)
        }
    }

    function setupCenterLineObserver() {
        const scrollerEl = scrollerElRef.value
        if (!scrollerEl) return

        // Clean up existing observer
        if (centerLineObserver.value) {
            centerLineObserver.value.disconnect()
        }

        // Create new observer to detect center line
        centerLineObserver.value = new IntersectionObserver(
            (entries) => {
                entries.forEach(entry => {
                    if (entry.isIntersecting) {
                        const lineIndex = parseInt(entry.target.getAttribute('data-index') || '0', 10)
                        if (lineIndex !== currentCenterLineIndex.value) {
                            currentCenterLineIndex.value = lineIndex
                            emit('centerLineChanged', lineIndex)
                            updateTabTitleWithToc(lineIndex)
                        }
                    }
                })
            },
            {
                root: scrollerEl,
                rootMargin: '-45% 0px -45% 0px', // Center 10% of viewport
                threshold: 0
            }
        )

        // Observe all line items
        const lineItems = scrollerEl.querySelectorAll('[data-index]')
        lineItems.forEach(item => {
            centerLineObserver.value?.observe(item)
        })
    }

    // Watch for scroller element changes
    watch(scrollerElRef, (newEl) => {
        if (newEl) {
            // Delay setup to ensure DOM is ready
            setTimeout(() => {
                setupCenterLineObserver()
            }, 100)
        }
    })

    // Cleanup on unmount
    onUnmounted(() => {
        if (centerLineObserver.value) {
            centerLineObserver.value.disconnect()
        }
    })

    return {
        currentCenterLineIndex,
        setupCenterLineObserver
    }
}
