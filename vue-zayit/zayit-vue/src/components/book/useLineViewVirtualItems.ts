/**
 * Line View Virtual Items Composable
 * Handles virtual items array creation with transformations
 */

import { computed, type Ref, type ComputedRef } from 'vue'
import type { BookLineViewerService } from '@/data/services/bookLineViewerService'
import type { Tab } from '@/data/types/Tab'
import { transformText } from '@/components/shared/useTextTransformations'
import { highlightGlobalSearchWithSnippet } from '@/utils/searchHighlighting'

export interface VirtualItem {
    index: number
    content: string
    altTocEntries?: any[]
}

export function useLineViewVirtualItems(
    viewerState: BookLineViewerService,
    myTab: ComputedRef<Tab | undefined>,
    altTocByLineIndex: Ref<Map<number, any[]> | undefined>,
    globalSearchHighlightLineIndex: Ref<number | null>,
    globalSearchTerms: Ref<string>,
    globalSearchSnippet: Ref<string>,
    searchQuery: Ref<string>,
    currentMatchLineIndex: Ref<number | null>,
    currentMatchIndexInLine: Ref<number>
) {
    const virtualItems = computed<VirtualItem[]>(() => {
        const items: VirtualItem[] = []
        const lines = viewerState.lines.value
        const diacriticsState = myTab.value?.bookState?.diacriticsState

        for (let i = 0; i < viewerState.totalLines.value; i++) {
            const line = lines[i]
            let processedContent = line || '\u00A0'

            // Apply unified transformations (diacritics + search)
            processedContent = transformText(processedContent, {
                diacriticsState,
                searchQuery: searchQuery.value,
                isCurrentSearchMatch: i === currentMatchLineIndex.value,
                currentMatchIndex: i === currentMatchLineIndex.value ? currentMatchIndexInLine.value : undefined
            })

            // Apply global search highlighting (from external search results)
            if (processedContent !== '\u00A0' && globalSearchTerms.value && i === globalSearchHighlightLineIndex.value) {
                processedContent = highlightGlobalSearchWithSnippet(
                    processedContent,
                    globalSearchTerms.value,
                    globalSearchSnippet.value
                )
            }

            items.push({
                index: i,
                content: processedContent,
                altTocEntries: altTocByLineIndex.value?.get(i)
            })
        }

        return items
    })

    return {
        virtualItems
    }
}
