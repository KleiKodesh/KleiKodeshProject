/**
 * Commentary Search Composable
 * Manages search state and provides search functionality for CommentaryView
 */

import { ref, computed, nextTick, type Ref } from 'vue'
import type { CommentaryLinkGroup } from '@/data/services/bookCommentaryService'
import { scrollToElementCenter } from '@/components/shared/useScrollToElement'

export interface CommentarySearchMatch {
    bookId: number // Use bookId instead of groupIndex for reliable matching
    linkIndex: number
    matchIndex: number // Index of this match within the link (0-based)
    matchCount: number // Total matches in this link
}

export function useCommentarySearch(
    commentaryGroups: Ref<CommentaryLinkGroup[]>,
    scrollContainer: Ref<HTMLElement | null>
) {
    const searchQuery = ref('')
    const matches = ref<CommentarySearchMatch[]>([])
    const currentMatchIndex = ref(-1)

    const totalMatches = computed(() => matches.value.length)
    const currentMatch = computed(() =>
        currentMatchIndex.value >= 0 ? matches.value[currentMatchIndex.value] : null
    )

    // Perform search across all commentary links
    function performSearch(query: string) {
        searchQuery.value = query.trim()
        matches.value = []
        currentMatchIndex.value = -1

        // Minimum 2 characters
        if (searchQuery.value.length < 2) {
            return
        }

        const normalizedQuery = removeDiacritics(searchQuery.value.toLowerCase())

        // Find all matching links and count matches per link
        commentaryGroups.value.forEach((group) => {
            if (!group.links || group.links.length === 0 || !group.targetBookId) {
                return
            }
            group.links.forEach((link: any, linkIndex: number) => {
                const content = link.html
                if (!content) return

                // Strip HTML tags first, then remove diacritics
                const strippedContent = content.replace(/<[^>]*>/g, '')
                const normalizedContent = removeDiacritics(strippedContent.toLowerCase())

                // Count how many times the query appears in this link with word boundaries
                let matchCount = 0
                let searchStart = 0
                while (true) {
                    const matchIdx = normalizedContent.indexOf(normalizedQuery, searchStart)
                    if (matchIdx === -1) break

                    // Check word boundaries
                    const charBefore = matchIdx > 0 ? normalizedContent[matchIdx - 1] : null
                    const charAfter = matchIdx + normalizedQuery.length < normalizedContent.length
                        ? normalizedContent[matchIdx + normalizedQuery.length]
                        : null

                    const isWordBoundaryBefore = !charBefore || /[\s־׳״׃.,;:!?()[\]{}\-–—]/.test(charBefore)
                    const isWordBoundaryAfter = !charAfter || /[\s־׳״׃.,;:!?()[\]{}\-–—]/.test(charAfter)

                    if (isWordBoundaryBefore && isWordBoundaryAfter) {
                        matchCount++
                    }

                    searchStart = matchIdx + 1
                }

                // Add a match entry for each occurrence in the link
                for (let j = 0; j < matchCount; j++) {
                    matches.value.push({
                        bookId: group.targetBookId!,
                        linkIndex,
                        matchIndex: j,
                        matchCount: matchCount
                    })
                }
            })
        })

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
            const linkElement = container.querySelector(
                `[data-book-id="${match.bookId}"] [data-link-index="${match.linkIndex}"]`
            ) as HTMLElement

            if (linkElement) {
                const linkRect = linkElement.getBoundingClientRect()
                const linkCenter = linkRect.top + (linkRect.height / 2)
                const distance = Math.abs(linkCenter - containerCenter)

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
        if (!match || !scrollContainer.value) {
            return
        }

        // Wait for Vue to update the highlighting based on currentMatchIndex
        await nextTick()

        // Find the group element by bookId, then find the link within it
        const groupElement = scrollContainer.value.querySelector(
            `[data-book-id="${match.bookId}"]`
        ) as HTMLElement

        if (!groupElement) return

        // Find the link within the group
        const linkElements = groupElement.querySelectorAll('[data-link-index]')
        const linkElement = Array.from(linkElements).find(
            el => el.getAttribute('data-link-index') === String(match.linkIndex)
        ) as HTMLElement

        if (!linkElement) return

        // Force visibility for reliable scrolling with content-visibility
        const originalVisibility = groupElement.style.contentVisibility
        groupElement.style.contentVisibility = 'visible'

        // Wait for render
        await new Promise(resolve => requestAnimationFrame(resolve))

        // Try to scroll to the specific mark element if there are multiple matches in the link
        const currentMark = linkElement.querySelector('mark.current') as HTMLElement
        const targetElement = currentMark || linkElement

        await scrollToElementCenter(targetElement, { behavior: 'instant' })

        // Verify scroll position and retry if needed (up to 3 times)
        for (let attempt = 0; attempt < 3; attempt++) {
            await new Promise(resolve => requestAnimationFrame(resolve))
            const containerRect = scrollContainer.value.getBoundingClientRect()
            const elementRect = targetElement.getBoundingClientRect()
            const containerCenter = containerRect.top + containerRect.height / 2
            const elementCenter = elementRect.top + elementRect.height / 2
            const distance = Math.abs(elementCenter - containerCenter)

            // If centered (tolerance of 5px), we're done
            if (distance < 5) {
                break
            }

            // Retry scroll
            await scrollToElementCenter(targetElement, { behavior: 'instant' })
        }

        // Restore original visibility
        if (originalVisibility !== undefined) {
            groupElement.style.contentVisibility = originalVisibility
        }
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
