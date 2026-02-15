/**
 * Virtualized Search Composable
 * 
 * Clean search implementation for virtualized content.
 * Searches the underlying data source (bond data), not rendered DOM.
 */

import { ref, computed } from 'vue'

interface SearchMatch {
    lineIndex: number
    matchIndex: number // Which match within the line (0-based)
}

export function useVirtualizedSearch() {
    const searchQuery = ref('')
    const allMatches = ref<SearchMatch[]>([])
    const currentMatchIndex = ref(-1)
    const isSearching = ref(false)

    const totalMatches = computed(() => allMatches.value.length)

    const currentMatch = computed(() => {
        if (currentMatchIndex.value >= 0 && currentMatchIndex.value < allMatches.value.length) {
            return allMatches.value[currentMatchIndex.value]
        }
        return null
    })

    /**
     * Search through all lines in the data source
     * @param currentLineIndex - Optional current scroll position to find closest match
     */
    async function search(
        query: string,
        dataSource: Record<number, string>,
        totalLines: number,
        currentLineIndex?: number
    ): Promise<void> {
        searchQuery.value = query
        allMatches.value = []
        currentMatchIndex.value = -1

        if (!query.trim()) {
            isSearching.value = false
            return
        }

        isSearching.value = true

        // Check if query contains spaces or dashes
        const queryHasSpacesOrDashes = /[\s\u002D\u2013\u2014\u05BE]/.test(query)

        // Normalize query - dashes BEFORE diacritics removal
        let normalizedQuery = query.toLowerCase()
        if (queryHasSpacesOrDashes) {
            normalizedQuery = normalizeDashes(normalizedQuery)
        }
        normalizedQuery = removeDiacritics(normalizedQuery)

        const matches: SearchMatch[] = []

        // Search through all lines
        for (let lineIndex = 0; lineIndex < totalLines; lineIndex++) {
            const content = dataSource[lineIndex]

            // Skip placeholders
            if (!content || content === '\u00A0') continue

            // Normalize content the same way as query - dashes BEFORE diacritics removal
            let normalizedContent = stripHtml(content).toLowerCase()
            if (queryHasSpacesOrDashes) {
                normalizedContent = normalizeDashes(normalizedContent)
            }
            normalizedContent = removeDiacritics(normalizedContent)

            // Find all occurrences in this line
            let searchStart = 0
            let matchIndex = 0

            while (true) {
                const foundAt = normalizedContent.indexOf(normalizedQuery, searchStart)
                if (foundAt === -1) break

                matches.push({
                    lineIndex,
                    matchIndex
                })

                matchIndex++
                searchStart = foundAt + 1
            }
        }

        allMatches.value = matches
        isSearching.value = false

        // Select closest match to current position, or first match if no position given
        if (matches.length > 0) {
            if (currentLineIndex !== undefined) {
                // Find the closest match at or after the current line
                let closestIndex = matches.findIndex(m => m.lineIndex >= currentLineIndex)

                // If no match found after current line, wrap to first match
                if (closestIndex === -1) {
                    closestIndex = 0
                }

                currentMatchIndex.value = closestIndex
            } else {
                currentMatchIndex.value = 0
            }
        }
    }

    /**
     * Navigate to next match
     */
    function nextMatch(): void {
        if (allMatches.value.length === 0) return

        currentMatchIndex.value = (currentMatchIndex.value + 1) % allMatches.value.length
    }

    /**
     * Navigate to previous match
     */
    function previousMatch(): void {
        if (allMatches.value.length === 0) return

        currentMatchIndex.value = currentMatchIndex.value <= 0
            ? allMatches.value.length - 1
            : currentMatchIndex.value - 1
    }

    /**
     * Highlight matches in HTML content
     * Returns HTML with <mark> tags around matches
     */
    function highlightInContent(
        htmlContent: string,
        isCurrentLine: boolean
    ): string {
        if (!searchQuery.value || !htmlContent || htmlContent === '\u00A0') {
            return htmlContent
        }

        const tempDiv = document.createElement('div')
        tempDiv.innerHTML = htmlContent

        const walker = document.createTreeWalker(
            tempDiv,
            NodeFilter.SHOW_TEXT,
            null
        )

        const textNodes: Text[] = []
        let node: Node | null
        while ((node = walker.nextNode())) {
            textNodes.push(node as Text)
        }

        // Check if query contains spaces or dashes
        const queryHasSpacesOrDashes = /[\s\u002D\u2013\u2014\u05BE]/.test(searchQuery.value)

        // Normalize query - dashes BEFORE diacritics removal
        let normalizedQuery = searchQuery.value.toLowerCase()
        if (queryHasSpacesOrDashes) {
            normalizedQuery = normalizeDashes(normalizedQuery)
        }
        normalizedQuery = removeDiacritics(normalizedQuery)

        let lineMatchIndex = 0

        textNodes.forEach(textNode => {
            const text = textNode.nodeValue || ''

            // Normalize text the same way as query - dashes BEFORE diacritics removal
            let normalizedText = text.toLowerCase()
            if (queryHasSpacesOrDashes) {
                normalizedText = normalizeDashes(normalizedText)
            }
            normalizedText = removeDiacritics(normalizedText)

            // Build position map for diacritics
            const positionMap = buildPositionMap(text)

            const parts: Array<{ text: string; highlight: boolean; isCurrent: boolean }> = []
            let lastIndex = 0

            let searchStart = 0
            while (true) {
                const foundAt = normalizedText.indexOf(normalizedQuery, searchStart)
                if (foundAt === -1) break

                // Map back to original positions
                const originalStart = positionMap[foundAt] ?? 0
                const originalEnd = positionMap[foundAt + normalizedQuery.length] ?? text.length

                // Add text before match
                if (originalStart > lastIndex) {
                    parts.push({
                        text: text.substring(lastIndex, originalStart),
                        highlight: false,
                        isCurrent: false
                    })
                }

                // Add match
                const isCurrent = isCurrentLine &&
                    currentMatch.value?.matchIndex === lineMatchIndex

                parts.push({
                    text: text.substring(originalStart, originalEnd),
                    highlight: true,
                    isCurrent
                })

                lineMatchIndex++
                lastIndex = originalEnd
                searchStart = foundAt + 1
            }

            // Add remaining text
            if (lastIndex < text.length) {
                parts.push({
                    text: text.substring(lastIndex),
                    highlight: false,
                    isCurrent: false
                })
            }

            // Replace text node with highlighted content
            if (parts.length > 0) {
                const fragment = document.createDocumentFragment()
                parts.forEach(part => {
                    if (part.highlight) {
                        const mark = document.createElement('mark')
                        if (part.isCurrent) {
                            mark.className = 'current'
                        }
                        mark.textContent = part.text
                        fragment.appendChild(mark)
                    } else {
                        fragment.appendChild(document.createTextNode(part.text))
                    }
                })
                textNode.parentNode?.replaceChild(fragment, textNode)
            }
        })

        return tempDiv.innerHTML
    }

    /**
     * Clear search
     */
    function clear(): void {
        searchQuery.value = ''
        allMatches.value = []
        currentMatchIndex.value = -1
        isSearching.value = false
    }

    // Helper functions
    function stripHtml(html: string): string {
        const tempDiv = document.createElement('div')
        tempDiv.innerHTML = html
        return tempDiv.textContent || ''
    }

    function removeDiacritics(text: string): string {
        return text.replace(/[\u0591-\u05C7]/g, '')
    }

    function normalizeDashes(text: string): string {
        // Replace maqaf (־ U+05BE) and all dash types with space
        // Includes: hyphen-minus (-), en dash (–), em dash (—), maqaf (־)
        return text.replace(/[\u002D\u2013\u2014\u05BE]/g, ' ')
    }

    function buildPositionMap(text: string): number[] {
        const map: number[] = []
        let normalizedIndex = 0

        for (let i = 0; i < text.length; i++) {
            const char = text[i]
            // Skip diacritics but treat dashes as spaces (they count in position)
            if (char && !isDiacritic(char)) {
                map[normalizedIndex] = i
                normalizedIndex++
            }
        }

        map[normalizedIndex] = text.length
        return map
    }

    function isDiacritic(char: string): boolean {
        const code = char.charCodeAt(0)
        return code >= 0x0591 && code <= 0x05C7
    }

    return {
        searchQuery,
        allMatches,
        currentMatchIndex,
        totalMatches,
        currentMatch,
        isSearching,
        search,
        nextMatch,
        previousMatch,
        highlightInContent,
        clear
    }
}
