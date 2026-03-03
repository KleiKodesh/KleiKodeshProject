/**
 * Line View Virtual Items Composable
 * Handles virtual items array creation with highlighting
 */

import { computed, type Ref, type ComputedRef } from 'vue'
import type { BookLineViewerService } from '@/data/services/bookLineViewerService'
import type { Tab } from '@/data/types/Tab'
import { applyDiacriticsFilter } from '@/utils/hebrewTextProcessing'
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
    searchQuery: Ref<string>,
    matches: Ref<any[]>,
    currentMatch: Ref<any>,
    highlightMatches: (content: string, lineIndex: number) => string,
    globalSearchHighlightLineIndex: Ref<number | null>,
    globalSearchTerms: Ref<string>,
    globalSearchSnippet: Ref<string>
) {
    const virtualItems = computed<VirtualItem[]>(() => {
        const items: VirtualItem[] = []
        const lines = viewerState.lines.value
        const diacriticsState = myTab.value?.bookState?.diacriticsState
        const currentMatchValue = currentMatch.value
        const query = searchQuery.value

        // Build a set of line indices that have matches for quick lookup
        const linesWithMatches = new Set<number>()
        if (query) {
            matches.value.forEach((match) => {
                linesWithMatches.add(match.itemIndex)
            })
        }

        for (let i = 0; i < viewerState.totalLines.value; i++) {
            const line = lines[i]
            let processedContent = line || '\u00A0'

            // Apply diacritics filtering
            if (processedContent !== '\u00A0' && diacriticsState && diacriticsState > 0) {
                processedContent = applyDiacriticsFilter(processedContent, diacriticsState)
            }

            // Apply in-book search highlighting only to lines that have matches
            if (processedContent !== '\u00A0' && query && linesWithMatches.has(i)) {
                processedContent = highlightMatches(processedContent, i)
            }
            // Apply global search highlighting (separate from in-book search)
            else if (processedContent !== '\u00A0' && globalSearchTerms.value && i === globalSearchHighlightLineIndex.value) {
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
