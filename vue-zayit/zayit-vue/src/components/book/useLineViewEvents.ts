/**
 * Line View Events Composable
 * Handles line click events
 */

import { ref, watch, type ComputedRef } from 'vue'
import type { Tab } from '@/data/types/Tab'
import type { TocEntry } from '@/data/types/BookToc'
import type { BookLineViewerService } from '@/data/services/bookLineViewerService'

export function useLineViewEvents(
    myTab: ComputedRef<Tab | undefined>,
    flatTocEntries: ComputedRef<TocEntry[] | undefined>,
    viewerState: BookLineViewerService,
    emit: {
        (e: 'lineClick', lineIndex: number): void
    }
) {
    const selectedLineIndex = ref<number | null>(null)

    // WATCHERS - Line Selection
    // Watch for selectedLineIndex changes to restore selection
    watch(() => myTab.value?.bookState?.selectedLineIndex, (newIndex) => {
        if (newIndex !== undefined) {
            selectedLineIndex.value = newIndex
        }
    }, { immediate: true })

    // EVENT HANDLERS - Line Click
    function handleLineClick(lineIndex: number) {
        selectedLineIndex.value = lineIndex

        // Check if this line is a TOC entry (but NOT an alt TOC entry)
        const tocEntry = flatTocEntries.value?.find(toc => toc.lineIndex === lineIndex && !toc.isAltToc)

        // Save selected line to tab state
        if (myTab.value?.bookState) {
            myTab.value.bookState.selectedLineIndex = lineIndex

            // If it's a regular TOC entry (not alt TOC), store the TOC ID so commentary view can load all related links
            if (tocEntry) {
                myTab.value.bookState.selectedTocEntryId = tocEntry.id
            } else {
                // Clear TOC ID for regular lines and alt TOC entries
                myTab.value.bookState.selectedTocEntryId = undefined
            }
        }

        emit('lineClick', lineIndex)
    }

    return {
        selectedLineIndex,
        handleLineClick
    }
}
