/**
 * Content Search Composable
 * 
 * Simple search for non-virtualized or already-loaded content.
 * Used by BookCommentaryView and other components that don't need
 * to search through large virtualized datasets.
 */

import { ref, computed } from 'vue'

interface SearchItem {
    index: number
    content: string
}

interface SearchMatch {
    itemIndex: number
    occurrence: number
    totalInItem: number
}

export function useContentSearch() {
    const searchQuery = ref('')
    const matches = ref<SearchMatch[]>([])
    const currentMatchIndex = ref(-1)

    const totalMatches = computed(() => matches.value.length)

    const currentMatch = computed(() => {
        if (currentMatchIndex.value >= 0 && currentMatchIndex.value < matches.value.length) {
            return matches.value[currentMatchIndex.value]
        }
        return null
    })

    function searchInItems(items: SearchItem[], query: string, currentItemIndex?: number) {
        searchQuery.value = query
        matches.value = []
        currentMatchIndex.value = -1

        if (!query) return

        // Check if query contains spaces or dashes
        const queryHasSpacesOrDashes = /[\s\u002D\u2013\u2014\u05BE]/.test(query)

        // Normalize query - dashes BEFORE diacritics removal
        let normalizedQuery = query.toLowerCase()
        if (queryHasSpacesOrDashes) {
            normalizedQuery = normalizeDashes(normalizedQuery)
        }
        normalizedQuery = removeDiacritics(normalizedQuery)

        items.forEach(item => {
            // Normalize content the same way as query - dashes BEFORE diacritics removal
            let normalizedContent = stripHtml(item.content).toLowerCase()
            if (queryHasSpacesOrDashes) {
                normalizedContent = normalizeDashes(normalizedContent)
            }
            normalizedContent = removeDiacritics(normalizedContent)
            let startIndex = 0
            let occurrence = 0

            while (true) {
                const index = normalizedContent.indexOf(normalizedQuery, startIndex)
                if (index === -1) break

                matches.value.push({
                    itemIndex: item.index,
                    occurrence,
                    totalInItem: 0 // Will be updated after
                })

                occurrence++
                startIndex = index + 1
            }

            // Update totalInItem for all matches in this item
            const itemMatches = matches.value.filter(m => m.itemIndex === item.index)
            itemMatches.forEach(m => m.totalInItem = itemMatches.length)
        })

        // Select closest match to current position, or first match if no position given
        if (matches.value.length > 0) {
            if (currentItemIndex !== undefined) {
                // Find the closest match at or after the current item
                let closestIndex = matches.value.findIndex(m => m.itemIndex >= currentItemIndex)

                // If no match found after current item, wrap to first match
                if (closestIndex === -1) {
                    closestIndex = 0
                }

                currentMatchIndex.value = closestIndex
            } else {
                currentMatchIndex.value = 0
            }
        }
    }

    function navigateToMatch(matchIndex: number) {
        if (matchIndex >= 0 && matchIndex < matches.value.length) {
            currentMatchIndex.value = matchIndex
        }
    }

    function nextMatch() {
        if (matches.value.length === 0) return
        currentMatchIndex.value = (currentMatchIndex.value + 1) % matches.value.length
    }

    function previousMatch() {
        if (matches.value.length === 0) return
        currentMatchIndex.value = currentMatchIndex.value <= 0
            ? matches.value.length - 1
            : currentMatchIndex.value - 1
    }

    function highlightMatches(htmlContent: string, query: string, currentOccurrence: number = -1): string {
        if (!query) return htmlContent

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
        const queryHasSpacesOrDashes = /[\s\u002D\u2013\u2014\u05BE]/.test(query)

        // Normalize query - dashes BEFORE diacritics removal
        let normalizedQuery = query.toLowerCase()
        if (queryHasSpacesOrDashes) {
            normalizedQuery = normalizeDashes(normalizedQuery)
        }
        normalizedQuery = removeDiacritics(normalizedQuery)

        let globalOccurrence = 0

        textNodes.forEach(textNode => {
            const text = textNode.nodeValue || ''
            const lowerText = text.toLowerCase()

            // Build position map for diacritics
            const positionMap = buildPositionMap(text)

            // Normalize text the same way as query - dashes BEFORE diacritics removal
            let normalizedText = lowerText
            if (queryHasSpacesOrDashes) {
                normalizedText = normalizeDashes(normalizedText)
            }
            normalizedText = removeDiacritics(normalizedText)

            const parts: Array<{ text: string; isMatch: boolean; isCurrent: boolean }> = []
            let lastIndex = 0

            let searchStart = 0
            while (true) {
                const normalizedMatchIndex = normalizedText.indexOf(normalizedQuery, searchStart)
                if (normalizedMatchIndex === -1) break

                // Map normalized position back to original position
                const originalStartIndex = positionMap[normalizedMatchIndex] || 0
                const originalEndIndex = positionMap[normalizedMatchIndex + normalizedQuery.length] || text.length

                // Add text before match
                if (originalStartIndex > lastIndex) {
                    parts.push({ text: text.substring(lastIndex, originalStartIndex), isMatch: false, isCurrent: false })
                }

                // Add match
                const isCurrent = globalOccurrence === currentOccurrence
                parts.push({
                    text: text.substring(originalStartIndex, originalEndIndex),
                    isMatch: true,
                    isCurrent
                })

                globalOccurrence++
                lastIndex = originalEndIndex
                searchStart = normalizedMatchIndex + 1
            }

            // Add remaining text
            if (lastIndex < text.length) {
                parts.push({ text: text.substring(lastIndex), isMatch: false, isCurrent: false })
            }

            if (parts.length > 0) {
                const fragment = document.createDocumentFragment()
                parts.forEach(part => {
                    if (part.isMatch) {
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
        currentMatchIndex,
        totalMatches,
        currentMatch,
        searchInItems,
        navigateToMatch,
        nextMatch,
        previousMatch,
        highlightMatches
    }
}
