/**
 * Line View Search Composable
 * Manages search state and provides search functionality for LineView
 */

import { ref, computed, type Ref } from 'vue'
import type { BookLineViewerService } from '@/data/services/bookLineViewerService'
import { scrollToElementCenter } from '@/components/shared/useScrollToElement'

export interface SearchMatch {
    lineIndex: number
    matchIndex: number // Index of this match within the line (0-based)
    matchCount: number // Total matches in this line
}

export function useLineViewSearch(
    viewerState: BookLineViewerService,
    scrollContainer: Ref<HTMLElement | null>
) {
    const searchQuery = ref('')
    const matches = ref<SearchMatch[]>([])
    const currentMatchIndex = ref(-1)

    const totalMatches = computed(() => matches.value.length)
    const currentMatch = computed(() =>
        currentMatchIndex.value >= 0 ? matches.value[currentMatchIndex.value] : null
    )

    // Perform search across all lines
    function performSearch(query: string) {
        searchQuery.value = query.trim()
        matches.value = []
        currentMatchIndex.value = -1

        // Minimum 2 characters
        if (searchQuery.value.length < 2) {
            return
        }

        const lines = viewerState.lines.value
        const totalLines = viewerState.totalLines.value
        const normalizedQuery = removeDiacritics(searchQuery.value.toLowerCase())

        // Find all matching lines and count matches per line
        for (let i = 0; i < totalLines; i++) {
            const line = lines[i]
            if (!line || line === '\u00A0') continue

            // Strip HTML tags first, then remove diacritics
            const strippedLine = line.replace(/<[^>]*>/g, '')
            const normalizedLine = removeDiacritics(strippedLine.toLowerCase())

            // Count how many times the query appears in this line with word boundaries
            let matchCount = 0
            let searchStart = 0
            while (true) {
                const matchIndex = normalizedLine.indexOf(normalizedQuery, searchStart)
                if (matchIndex === -1) break

                // Check word boundaries - character before and after match
                const charBefore = matchIndex > 0 ? normalizedLine[matchIndex - 1] : null
                const charAfter = matchIndex + normalizedQuery.length < normalizedLine.length
                    ? normalizedLine[matchIndex + normalizedQuery.length]
                    : null

                // Check if boundaries are word separators (maqaf, space, punctuation, or start/end)
                // null/undefined means start/end of string, which is a valid word boundary
                const isWordBoundaryBefore = !charBefore || /[\s־׳״׃.,;:!?()[\]{}\-–—]/.test(charBefore)
                const isWordBoundaryAfter = !charAfter || /[\s־׳״׃.,;:!?()[\]{}\-–—]/.test(charAfter)

                if (isWordBoundaryBefore && isWordBoundaryAfter) {
                    matchCount++
                }

                searchStart = matchIndex + 1
            }

            // Add a match entry for each occurrence in the line
            for (let j = 0; j < matchCount; j++) {
                matches.value.push({
                    lineIndex: i,
                    matchIndex: j,
                    matchCount: matchCount
                })
            }
        }

        // Select nearest match to current viewport
        if (matches.value.length > 0) {
            currentMatchIndex.value = findNearestMatch()
            scrollToCurrentMatch()
        }
    }

    // Find match nearest to current scroll position
    function findNearestMatch(): number {
        if (!scrollContainer.value || matches.value.length === 0) return 0

        const container = scrollContainer.value
        const containerRect = container.getBoundingClientRect()
        const containerCenter = containerRect.top + (containerRect.height / 2)

        let nearestIndex = 0
        let minDistance = Infinity

        matches.value.forEach((match, idx) => {
            const lineElement = container.querySelector(
                `[data-line-index="${match.lineIndex}"]`
            ) as HTMLElement

            if (lineElement) {
                const lineRect = lineElement.getBoundingClientRect()
                const lineCenter = lineRect.top + (lineRect.height / 2)
                const distance = Math.abs(lineCenter - containerCenter)

                if (distance < minDistance) {
                    minDistance = distance
                    nearestIndex = idx
                }
            }
        })

        return nearestIndex
    }

    // Navigate to next match
    function nextMatch() {
        if (matches.value.length === 0) return
        currentMatchIndex.value = (currentMatchIndex.value + 1) % matches.value.length
        scrollToCurrentMatch()
    }

    // Navigate to previous match
    function previousMatch() {
        if (matches.value.length === 0) return
        currentMatchIndex.value = currentMatchIndex.value <= 0
            ? matches.value.length - 1
            : currentMatchIndex.value - 1
        scrollToCurrentMatch()
    }

    // Scroll to current match
    async function scrollToCurrentMatch() {
        const match = currentMatch.value
        if (!match || !scrollContainer.value) return

        const lineElement = scrollContainer.value.querySelector(
            `[data-line-index="${match.lineIndex}"]`
        ) as HTMLElement

        if (!lineElement) return

        // Force visibility for reliable scrolling with content-visibility
        const originalVisibility = lineElement.style.contentVisibility
        lineElement.style.contentVisibility = 'visible'

        // Wait for render
        await new Promise(resolve => requestAnimationFrame(resolve))
        await scrollToElementCenter(lineElement, { behavior: 'instant' })

        // Verify scroll position and retry if needed
        await new Promise(resolve => requestAnimationFrame(resolve))
        const containerRect = scrollContainer.value.getBoundingClientRect()
        const elementRect = lineElement.getBoundingClientRect()
        const containerCenter = containerRect.top + containerRect.height / 2
        const elementCenter = elementRect.top + elementRect.height / 2

        // If not centered (tolerance of 5px), retry
        if (Math.abs(elementCenter - containerCenter) >= 5) {
            await scrollToElementCenter(lineElement, { behavior: 'instant' })
        }

        // Restore original visibility
        lineElement.style.contentVisibility = originalVisibility
    }

    // Clear search
    function clearSearch() {
        searchQuery.value = ''
        matches.value = []
        currentMatchIndex.value = -1
    }

    return {
        searchQuery,
        matches,
        currentMatchIndex,
        totalMatches,
        currentMatch,
        performSearch,
        nextMatch,
        previousMatch,
        clearSearch
    }
}

/**
 * Remove Hebrew diacritics for matching
 */
function removeDiacritics(text: string): string {
    return text.replace(/[\u0591-\u05C7]/g, '')
}

/**
 * Normalize text for search by removing diacritics and treating punctuation as spaces
 * This ensures that words separated by maqaf or other punctuation are treated as separate words
 */
function normalizeForSearch(text: string): string {
    // Remove diacritics
    let normalized = removeDiacritics(text)

    // Replace maqaf (־) and other punctuation with spaces to create word boundaries
    // This includes: maqaf, geresh, gershayim, sof pasuq, and common punctuation
    normalized = normalized.replace(/[־׳״׃.,;:!?()[\]{}\-–—]/g, ' ')

    // Normalize multiple spaces to single space
    normalized = normalized.replace(/\s+/g, ' ')

    return normalized.toLowerCase()
}
