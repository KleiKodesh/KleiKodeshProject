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

    function searchInItems(items: SearchItem[], query: string) {
        searchQuery.value = query
        matches.value = []
        currentMatchIndex.value = -1

        if (!query) return

        const normalizedQuery = removeDiacritics(query.toLowerCase())

        items.forEach(item => {
            const normalizedContent = removeDiacritics(stripHtml(item.content).toLowerCase())
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
    }

    function navigateToMatch(matchIndex: number) {
        if (matchIndex >= 0 && matchIndex < matches.value.length) {
            currentMatchIndex.value = matchIndex
        }
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

        const normalizedQuery = removeDiacritics(query.toLowerCase())
        let globalOccurrence = 0

        textNodes.forEach(textNode => {
            const text = textNode.nodeValue || ''
            const lowerText = text.toLowerCase()

            // Build a map from normalized position to original position
            const positionMap: number[] = []
            let normalizedIndex = 0
            for (let i = 0; i < text.length; i++) {
                const char = text[i]
                if (char && !isDiacritic(char)) {
                    positionMap[normalizedIndex] = i
                    normalizedIndex++
                }
            }
            positionMap[normalizedIndex] = text.length // End position

            const normalizedText = removeDiacritics(lowerText)

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
        // Remove Hebrew diacritics (nikkud and cantillation marks)
        return text.replace(/[\u0591-\u05C7]/g, '')
    }

    function isDiacritic(char: string): boolean {
        const code = char.charCodeAt(0)
        return code >= 0x0591 && code <= 0x05C7
    }

    return {
        searchQuery,
        totalMatches,
        currentMatch,
        searchInItems,
        navigateToMatch,
        highlightMatches
    }
}
