import { computed } from 'vue'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'

export function useSearchResultsList(searchQuery: () => string) {
    const settingsStore = useSettingsStore()

    const containerStyles = computed(() => ({
        backgroundColor: 'var(--reading-bg-primary)',
        color: 'var(--reading-text-primary)'
    }))

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
                highlighted = highlighted.replace(regex, '<mark>$1</mark>')
            }
        })

        return highlighted
    }

    return {
        containerStyles,
        highlightedSnippet
    }
}
