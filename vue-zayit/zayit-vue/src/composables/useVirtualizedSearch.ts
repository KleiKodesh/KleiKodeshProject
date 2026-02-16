/**
 * Virtualized Search Composable
 * 
 * Complete search solution for virtualized content:
 * - Core search logic (zero-copy, Hebrew text support)
 * - UI behavior (navigation, highlighting)
 * - Delegates scrolling to component-specific callback
 */

import { ref, computed, nextTick, type Ref } from 'vue'

interface SearchMatch {
    itemIndex: number
    occurrence: number
}

interface UseVirtualizedSearchOptions {
    scrollerRef: Ref<any>
    itemSelector: string
    itemIndexAttribute: string
    minItemSize: Ref<number>
    totalItems: Ref<number>
    searchBarOffset?: number
    onScrollToItem: (itemIndex: number) => Promise<void>
}

export function useVirtualizedSearch(options: UseVirtualizedSearchOptions) {
    const {
        scrollerRef,
        itemSelector,
        itemIndexAttribute,
        totalItems,
        onScrollToItem
    } = options

    // Core search state
    const searchQuery = ref('')
    const matches = ref<SearchMatch[]>([])
    const currentMatchIndex = ref(-1)
    const isSearchOpen = ref(false)
    const isNavigating = ref(false)

    const totalMatches = computed(() => matches.value.length)
    const currentMatch = computed(() => {
        if (currentMatchIndex.value >= 0 && currentMatchIndex.value < matches.value.length) {
            return matches.value[currentMatchIndex.value]
        }
        return null
    })

    // ============================================
    // CORE SEARCH LOGIC
    // ============================================

    /**
     * Search data source directly (does NOT auto-select a match)
     */
    function performSearch(
        dataSource: Record<number, string> | Array<any>,
        totalItemsCount: number,
        query: string,
        getContent?: (item: any, index: number) => string | null
    ): void {
        searchQuery.value = query
        matches.value = []
        currentMatchIndex.value = -1

        if (!query.trim()) return

        const normalizedQuery = normalizeForMatching(query)
        const isArray = Array.isArray(dataSource)

        // Search all items
        for (let i = 0; i < totalItemsCount; i++) {
            let content: string | null | undefined

            if (isArray) {
                content = getContent ? getContent(dataSource[i], i) : dataSource[i]
            } else {
                content = dataSource[i]
            }

            if (!content || content === '\u00A0') continue

            const normalizedContent = normalizeForMatching(stripHtml(content))

            // Find all occurrences
            let searchStart = 0
            let occurrence = 0

            while (true) {
                const foundAt = normalizedContent.indexOf(normalizedQuery, searchStart)
                if (foundAt === -1) break

                matches.value.push({ itemIndex: i, occurrence })
                occurrence++
                searchStart = foundAt + 1
            }
        }
    }

    /**
     * Select first match in a list of visible item indices
     */
    function selectFirstVisibleMatch(visibleItemIndices: number[]): void {
        if (matches.value.length === 0) return

        const visibleSet = new Set(visibleItemIndices)
        const firstVisibleMatchIndex = matches.value.findIndex(m => visibleSet.has(m.itemIndex))

        if (firstVisibleMatchIndex !== -1) {
            currentMatchIndex.value = firstVisibleMatchIndex
        } else {
            currentMatchIndex.value = 0
        }
    }

    /**
     * Navigate to specific match
     */
    function navigateToMatch(matchIndex: number): void {
        if (matchIndex >= 0 && matchIndex < matches.value.length) {
            currentMatchIndex.value = matchIndex
        }
    }

    /**
     * Next match (wraps)
     */
    function nextMatch(): void {
        if (matches.value.length === 0) return
        currentMatchIndex.value = (currentMatchIndex.value + 1) % matches.value.length
    }

    /**
     * Previous match (wraps)
     */
    function previousMatch(): void {
        if (matches.value.length === 0) return
        currentMatchIndex.value = currentMatchIndex.value <= 0
            ? matches.value.length - 1
            : currentMatchIndex.value - 1
    }

    /**
     * Highlight matches in HTML content (handles tags in middle of words)
     */
    function highlightMatches(htmlContent: string, itemIndex: number): string {
        if (!searchQuery.value || !htmlContent) return htmlContent

        // Step 1: Get plain text and find all match positions
        const plainText = stripHtml(htmlContent)
        const normalizedPlainText = normalizeForMatching(plainText)
        const normalizedQuery = normalizeForMatching(searchQuery.value)

        // Find all match positions in plain text
        const matchPositions: Array<{ start: number; end: number; occurrence: number }> = []
        let searchStart = 0
        let occurrence = 0

        while (true) {
            const foundAt = normalizedPlainText.indexOf(normalizedQuery, searchStart)
            if (foundAt === -1) break

            const originalStart = mapNormalizedToOriginal(plainText, foundAt)
            const originalEnd = mapNormalizedToOriginal(plainText, foundAt + normalizedQuery.length)

            matchPositions.push({ start: originalStart, end: originalEnd, occurrence })
            occurrence++
            searchStart = foundAt + 1
        }

        if (matchPositions.length === 0) return htmlContent

        // Step 2: Process HTML nodes and apply highlights
        const tempDiv = document.createElement('div')
        tempDiv.innerHTML = htmlContent

        const walker = document.createTreeWalker(tempDiv, NodeFilter.SHOW_TEXT, null)
        const textNodes: Text[] = []
        let node: Node | null
        while ((node = walker.nextNode())) {
            textNodes.push(node as Text)
        }

        let plainTextPosition = 0

        textNodes.forEach(textNode => {
            const text = textNode.nodeValue || ''
            const textLength = text.length
            const nodeStart = plainTextPosition
            const nodeEnd = plainTextPosition + textLength

            // Find matches that overlap with this text node
            const overlappingMatches = matchPositions.filter(match =>
                match.start < nodeEnd && match.end > nodeStart
            )

            if (overlappingMatches.length > 0) {
                const parts: Array<{ text: string; isMatch: boolean; isCurrent: boolean }> = []
                let lastIndex = 0

                overlappingMatches.forEach(match => {
                    const matchStartInNode = Math.max(0, match.start - nodeStart)
                    const matchEndInNode = Math.min(textLength, match.end - nodeStart)

                    if (matchStartInNode > lastIndex) {
                        parts.push({ text: text.substring(lastIndex, matchStartInNode), isMatch: false, isCurrent: false })
                    }

                    const isCurrent = currentMatch.value?.itemIndex === itemIndex &&
                        currentMatch.value?.occurrence === match.occurrence

                    parts.push({
                        text: text.substring(matchStartInNode, matchEndInNode),
                        isMatch: true,
                        isCurrent
                    })

                    lastIndex = matchEndInNode
                })

                if (lastIndex < textLength) {
                    parts.push({ text: text.substring(lastIndex), isMatch: false, isCurrent: false })
                }

                const fragment = document.createDocumentFragment()
                parts.forEach(part => {
                    if (part.isMatch) {
                        const mark = document.createElement('mark')
                        if (part.isCurrent) mark.className = 'current'
                        mark.textContent = part.text
                        fragment.appendChild(mark)
                    } else {
                        fragment.appendChild(document.createTextNode(part.text))
                    }
                })
                textNode.parentNode?.replaceChild(fragment, textNode)
            }

            plainTextPosition += textLength
        })

        return tempDiv.innerHTML
    }

    /**
     * Clear search
     */
    function clear(): void {
        searchQuery.value = ''
        matches.value = []
        currentMatchIndex.value = -1
    }

    // ============================================
    // UI BEHAVIOR LAYER
    // ============================================

    /**
     * Scroll to current match using virtual scroller with search bar offset
     */
    async function scrollToMatch(itemIndex: number): Promise<void> {
        isNavigating.value = true

        try {
            // Call component callback to prioritize loading (if needed)
            await onScrollToItem(itemIndex)

            if (!scrollerRef.value?.scrollToItem) {
                isNavigating.value = false
                return
            }

            const scrollerEl = scrollerRef.value.$el
            if (!scrollerEl) {
                isNavigating.value = false
                return
            }

            // Hide overflow during scroll to prevent visual jumping
            const originalOverflow = scrollerEl.style.overflow
            scrollerEl.style.overflow = 'hidden'

            // Step 1: Scroll to item using virtual scroller
            scrollerRef.value.scrollToItem(itemIndex)

            // Step 2: Wait for DOM to update
            await nextTick()

            // Step 3: Apply offset to account for search bar
            const searchBarOffset = 30
            scrollerEl.scrollTop = scrollerEl.scrollTop - searchBarOffset

            // Restore overflow
            scrollerEl.style.overflow = originalOverflow
        } finally {
            // Keep flag true for 100ms to prevent scroll tracking from interfering
            setTimeout(() => {
                isNavigating.value = false
            }, 100)
        }
    }

    /**
     * Get currently visible item indices
     */
    function getVisibleItemIndices(): number[] {
        if (!scrollerRef.value?.$el) return []

        const scrollerEl = scrollerRef.value.$el
        const scrollerRect = scrollerEl.getBoundingClientRect()
        const items = scrollerEl.querySelectorAll(itemSelector)
        const visibleIndices: number[] = []

        items.forEach((item: Element) => {
            const itemRect = item.getBoundingClientRect()
            const isVisible = itemRect.bottom > scrollerRect.top && itemRect.top < scrollerRect.bottom

            if (isVisible) {
                const indexAttr = (item as HTMLElement).getAttribute(itemIndexAttribute)
                if (indexAttr) {
                    const index = parseInt(indexAttr)
                    if (!visibleIndices.includes(index)) {
                        visibleIndices.push(index)
                    }
                }
            }
        })

        return visibleIndices
    }

    /**
     * Handle search query
     */
    function handleSearch(
        dataSource: Record<number, string> | Array<any>,
        query: string,
        getContent?: (item: any, index: number) => string | null
    ): void {
        if (!query.trim()) {
            clear()
            return
        }

        // Perform search
        performSearch(dataSource, totalItems.value, query, getContent)

        // Select first visible match
        const visibleIndices = getVisibleItemIndices()
        selectFirstVisibleMatch(visibleIndices)
    }

    /**
     * Handle next match
     */
    async function handleSearchNext(): Promise<void> {
        if (totalMatches.value === 0) return

        nextMatch()
        await nextTick()

        if (currentMatch.value) {
            await scrollToMatch(currentMatch.value.itemIndex)
        }
    }

    /**
     * Handle previous match
     */
    async function handleSearchPrevious(): Promise<void> {
        if (totalMatches.value === 0) return

        previousMatch()
        await nextTick()

        if (currentMatch.value) {
            await scrollToMatch(currentMatch.value.itemIndex)
        }
    }

    /**
     * Open search
     */
    function openSearch(): void {
        isSearchOpen.value = true
    }

    /**
     * Close search
     */
    function handleSearchClose(): void {
        isSearchOpen.value = false
        clear()
    }

    // ============================================
    // HELPERS
    // ============================================

    function stripHtml(html: string): string {
        const tempDiv = document.createElement('div')
        tempDiv.innerHTML = html
        return tempDiv.textContent || ''
    }

    function normalizeForMatching(text: string): string {
        let normalized = text.toLowerCase()
        normalized = normalized.replace(/[\u002D\u2013\u2014\u05BE]/g, ' ')
        normalized = normalized.replace(/[\u0591-\u05C7]/g, '')
        return normalized
    }

    function mapNormalizedToOriginal(originalText: string, normalizedPos: number): number {
        let normalizedIndex = 0

        for (let i = 0; i < originalText.length; i++) {
            const char = originalText[i]
            if (!char) continue

            const code = char.charCodeAt(0)
            const isDiacritic = (code >= 0x0591 && code <= 0x05BD) || (code >= 0x05BF && code <= 0x05C7)

            if (isDiacritic) continue

            if (normalizedIndex === normalizedPos) {
                return i
            }

            normalizedIndex++
        }

        return originalText.length
    }

    return {
        // Core search state
        searchQuery,
        matches,
        currentMatchIndex,
        totalMatches,
        currentMatch,

        // Core search methods
        performSearch,
        selectFirstVisibleMatch,
        navigateToMatch,
        nextMatch,
        previousMatch,
        highlightMatches,
        clear,

        // UI state
        isSearchOpen,
        isNavigating,

        // UI methods
        handleSearch,
        handleSearchNext,
        handleSearchPrevious,
        openSearch,
        handleSearchClose
    }
}
