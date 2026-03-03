/**
 * Bloom Search Composable
 * Handles search execution, cancellation, caching, and streaming results
 */

import { ref } from 'vue'
import { bloomSearchService } from '@/data/services/bloomSearchService'
import { bloomSearchCacheService } from '@/data/services/bloomSearchCacheService'
import { webviewBridge } from '@/data/services/webviewBridge'
import type { BloomSearchResult } from '@/data/types/BloomSearch'

// Sample data for dev mode
const sampleResults: BloomSearchResult[] = [
    {
        lineId: 1,
        bookId: 1,
        bookTitle: 'בראשית',
        tocText: 'פרק א',
        score: 0.95,
        proximityScore: 0.9,
        snippet: 'בראשית ברא אלקים את השמים ואת הארץ'
    },
    {
        lineId: 2,
        bookId: 1,
        bookTitle: 'בראשית',
        tocText: 'פרק א',
        score: 0.92,
        proximityScore: 0.88,
        snippet: 'והארץ היתה תהו ובהו וחשך על פני תהום'
    },
    {
        lineId: 3,
        bookId: 2,
        bookTitle: 'שמות',
        tocText: 'פרק א',
        score: 0.88,
        proximityScore: 0.85,
        snippet: 'ואלה שמות בני ישראל הבאים מצרימה'
    },
    {
        lineId: 4,
        bookId: 3,
        bookTitle: 'ויקרא',
        tocText: 'פרק א',
        score: 0.85,
        proximityScore: 0.82,
        snippet: 'ויקרא אל משה וידבר אליו מאהל מועד'
    },
    {
        lineId: 5,
        bookId: 1,
        bookTitle: 'בראשית',
        tocText: 'פרק ב',
        score: 0.82,
        proximityScore: 0.8,
        snippet: 'ויכלו השמים והארץ וכל צבאם'
    }
]

export function useBloomSearch() {
    const results = ref<BloomSearchResult[]>([])
    const isSearching = ref(false)
    const hasSearched = ref(false)
    const executedQuery = ref('')

    let currentSearchId: string | null = null
    const isDev = import.meta.env.DEV

    /**
     * Cancel the current search
     */
    const cancelSearch = async () => {
        if (currentSearchId) {
            console.log('[useBloomSearch] User cancelled search:', currentSearchId)
            try {
                await webviewBridge.bloomSearchCancel(currentSearchId)
                webviewBridge.unregisterSearchListener(currentSearchId)
            } catch (error) {
                console.error('[useBloomSearch] Error cancelling search:', error)
            }
            currentSearchId = null
            isSearching.value = false
        }
    }

    /**
     * Execute a search query
     */
    const executeSearch = async (query: string) => {
        if (!query.trim()) {
            return
        }

        // Cancel previous search completely across all layers
        if (currentSearchId) {
            console.log('[useBloomSearch] Cancelling previous search:', currentSearchId)
            try {
                // Unregister listener first to stop receiving messages
                webviewBridge.unregisterSearchListener(currentSearchId)
                // Cancel in C# backend
                await webviewBridge.bloomSearchCancel(currentSearchId)
            } catch (error) {
                console.error('[useBloomSearch] Error cancelling previous search:', error)
            }
            currentSearchId = null
        }

        // Clear UI state immediately
        isSearching.value = true
        hasSearched.value = true
        results.value = []

        // Store the query being executed for highlighting
        executedQuery.value = query

        console.log('[useBloomSearch] Executing streaming search:', query)

        try {
            if (isDev) {
                const isReady = await bloomSearchService.isReady()
                if (!isReady) {
                    console.log('[useBloomSearch] Dev mode: Using sample data')
                    await new Promise(resolve => setTimeout(resolve, 500))
                    results.value = sampleResults
                    console.log('[useBloomSearch] Dev mode: Loaded sample results:', sampleResults.length)
                    isSearching.value = false
                    return
                }
            }

            const normalizedQuery = query.trim().toLowerCase()
            const cachedResults = await bloomSearchCacheService.get(normalizedQuery)
            if (cachedResults !== null) {
                console.log('[useBloomSearch] Using cached results:', cachedResults.length)
                results.value = cachedResults
                isSearching.value = false
                return
            }

            const searchId = await webviewBridge.bloomSearchStart(query)
            currentSearchId = searchId
            console.log('[useBloomSearch] Search started with ID:', searchId)

            webviewBridge.registerSearchListener(
                searchId,
                (batchResults) => {
                    if (currentSearchId === searchId) {
                        console.log('[useBloomSearch] Received batch:', batchResults.length, 'results')
                        results.value = [...results.value, ...batchResults]
                    }
                },
                async () => {
                    if (currentSearchId === searchId) {
                        console.log('[useBloomSearch] Search completed, total results:', results.value.length)
                        isSearching.value = false

                        if (results.value.length > 0) {
                            const cleanResults = results.value.map(r => ({
                                lineId: r.lineId,
                                bookId: r.bookId,
                                bookTitle: r.bookTitle,
                                tocText: r.tocText,
                                score: r.score,
                                proximityScore: r.proximityScore,
                                snippet: r.snippet
                            }))
                            // Store in cache only (not in tab state)
                            await bloomSearchCacheService.set(normalizedQuery, cleanResults)
                        }

                        currentSearchId = null
                    }
                },
                () => {
                    if (currentSearchId === searchId) {
                        console.log('[useBloomSearch] Search cancelled')
                        isSearching.value = false
                        currentSearchId = null
                    }
                },
                (error) => {
                    if (currentSearchId === searchId) {
                        console.error('[useBloomSearch] Search error:', error)
                        isSearching.value = false
                        currentSearchId = null

                        if (isDev) {
                            console.log('[useBloomSearch] Dev mode: Error fallback to sample data')
                            results.value = sampleResults
                        }
                    }
                }
            )
        } catch (error) {
            console.error('[useBloomSearch] Search error:', error)
            isSearching.value = false

            if (isDev) {
                console.log('[useBloomSearch] Dev mode: Error fallback to sample data')
                results.value = sampleResults
            }
        }
    }

    /**
     * Clear search state
     */
    const clearSearch = () => {
        results.value = []
        hasSearched.value = false
        executedQuery.value = ''
    }

    /**
     * Load cached results for a query
     */
    const loadCachedResults = async (query: string) => {
        const normalizedQuery = query.trim().toLowerCase()
        const cachedResults = await bloomSearchCacheService.get(normalizedQuery)

        if (cachedResults !== null) {
            results.value = cachedResults
            executedQuery.value = query
            hasSearched.value = true
            return true
        }

        return false
    }

    return {
        results,
        isSearching,
        hasSearched,
        executedQuery,
        executeSearch,
        cancelSearch,
        clearSearch,
        loadCachedResults
    }
}
