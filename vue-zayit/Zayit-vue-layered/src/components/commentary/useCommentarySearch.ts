/**
 * Commentary Search Composable
 * Handles search functionality within commentary content
 */

import { ref, computed, type Ref } from 'vue'

export function useCommentarySearch(contentRef: Ref<HTMLElement | null>) {
    const isSearchOpen = ref(false)
    const searchQuery = ref('')
    const matches = ref<number[]>([])
    const currentMatchIndex = ref(-1)

    const totalMatches = computed(() => matches.value.length)

    /**
     * Highlight search term in element
     */
    function highlightInElement(element: HTMLElement, searchTerm: string, matchIndex: number) {
        const text = element.textContent || ''
        const lowerText = text.toLowerCase()
        const index = lowerText.indexOf(searchTerm)

        if (index === -1) return

        const isCurrent = matchIndex === currentMatchIndex.value
        const className = isCurrent ? 'search-highlight-current' : 'search-highlight'

        const before = text.substring(0, index)
        const match = text.substring(index, index + searchTerm.length)
        const after = text.substring(index + searchTerm.length)

        element.innerHTML = `${before}<span class="${className}">${match}</span>${after}`

        if (isCurrent) {
            element.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
        }
    }

    /**
     * Highlight all search matches
     */
    function highlightMatches() {
        if (!searchQuery.value) return

        const container = contentRef.value
        if (!container) return

        const searchLower = searchQuery.value.toLowerCase()
        let matchIndex = 0

        // Highlight in headers
        const headers = container.querySelectorAll('.group-header')
        headers.forEach((headerEl) => {
            highlightInElement(headerEl as HTMLElement, searchLower, matchIndex)
            const text = headerEl.textContent || ''
            if (text.toLowerCase().includes(searchLower)) {
                matchIndex++
            }
        })

        // Highlight in links
        const linkEls = container.querySelectorAll('.link-item')
        linkEls.forEach((linkEl) => {
            highlightInElement(linkEl as HTMLElement, searchLower, matchIndex)
            const text = linkEl.textContent || ''
            if (text.toLowerCase().includes(searchLower)) {
                matchIndex++
            }
        })
    }

    /**
     * Remove all highlights
     */
    function removeHighlights() {
        if (!contentRef.value) return

        const highlighted = contentRef.value.querySelectorAll('.search-highlight, .search-highlight-current')
        highlighted.forEach(el => {
            const parent = el.parentNode
            if (parent) {
                parent.replaceChild(document.createTextNode(el.textContent || ''), el)
                parent.normalize()
            }
        })
    }

    /**
     * Handle search
     */
    function handleSearch(query: string) {
        searchQuery.value = query
        matches.value = []
        currentMatchIndex.value = -1

        if (!query) return

        const container = contentRef.value
        if (!container) return

        // Remove existing highlights
        removeHighlights()

        // Search in headers and links
        const searchLower = query.toLowerCase()
        let matchIndex = 0

        const headers = container.querySelectorAll('.group-header')
        headers.forEach((headerEl) => {
            const text = headerEl.textContent || ''
            if (text.toLowerCase().includes(searchLower)) {
                matches.value.push(matchIndex++)
            }
        })

        const linkEls = container.querySelectorAll('.link-item')
        linkEls.forEach((linkEl) => {
            const text = linkEl.textContent || ''
            if (text.toLowerCase().includes(searchLower)) {
                matches.value.push(matchIndex++)
            }
        })

        if (matches.value.length > 0) {
            currentMatchIndex.value = 0
            highlightMatches()
        }
    }

    /**
     * Navigate to next match
     */
    function handleSearchNext() {
        if (matches.value.length === 0) return

        currentMatchIndex.value = (currentMatchIndex.value + 1) % matches.value.length
        highlightMatches()
    }

    /**
     * Navigate to previous match
     */
    function handleSearchPrevious() {
        if (matches.value.length === 0) return

        currentMatchIndex.value = currentMatchIndex.value <= 0
            ? matches.value.length - 1
            : currentMatchIndex.value - 1
        highlightMatches()
    }

    /**
     * Open search
     */
    function openSearch() {
        isSearchOpen.value = true
    }

    /**
     * Close search
     */
    function handleSearchClose() {
        isSearchOpen.value = false
        searchQuery.value = ''
        matches.value = []
        currentMatchIndex.value = -1
        removeHighlights()
    }

    return {
        isSearchOpen,
        searchQuery,
        currentMatchIndex,
        totalMatches,
        handleSearch,
        handleSearchNext,
        handleSearchPrevious,
        openSearch,
        handleSearchClose
    }
}
