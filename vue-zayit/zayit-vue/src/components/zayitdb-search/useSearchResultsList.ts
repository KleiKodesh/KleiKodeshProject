import { computed, type Ref } from 'vue'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'

// Simple scroll position storage for search results
let savedScrollPosition = 0

export function useSearchResultsList(
    searchQuery: () => string,
    scrollContainer: Ref<HTMLElement | null>
) {
    const settingsStore = useSettingsStore()

    const containerStyles = computed(() => ({
        backgroundColor: 'var(--reading-bg-primary)',
        color: 'var(--reading-text-primary)'
    }))

    const intrinsicSize = computed(() => {
        const baseFontSize = 16
        const lineHeight = 1.5
        const estimatedLines = 3
        const padding = 16
        const headerHeight = 30
        const estimatedHeight = baseFontSize * lineHeight * estimatedLines + padding + headerHeight
        return `auto ${Math.round(estimatedHeight)}px`
    })

    /**
     * Highlight search terms in snippet
     */
    const highlightedSnippet = (snippet: string): string => {
        const query = searchQuery()
        if (!query || !snippet) {
            return snippet
        }

        // Apply censoring if enabled
        let processedSnippet = snippet
        if (settingsStore.censorDivineNames) {
            processedSnippet = censorDivineNames(processedSnippet)
        }

        const terms = query.trim().split(/\s+/)
        let highlighted = processedSnippet

        terms.forEach((term) => {
            if (term.length > 0) {
                const escapedTerm = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
                const regex = new RegExp(`(${escapedTerm})`, 'gi')
                highlighted = highlighted.replace(regex, '<span class="search-match">$1</span>')
            }
        })

        return highlighted
    }

    function saveScrollPosition() {
        if (scrollContainer.value) {
            savedScrollPosition = scrollContainer.value.scrollTop
        }
    }

    function restoreScrollPosition() {
        if (scrollContainer.value && savedScrollPosition > 0) {
            scrollContainer.value.scrollTop = savedScrollPosition
        }
    }

    function handleScroll() {
        saveScrollPosition()
    }

    return {
        containerStyles,
        intrinsicSize,
        highlightedSnippet,
        saveScrollPosition,
        restoreScrollPosition,
        handleScroll
    }
}
