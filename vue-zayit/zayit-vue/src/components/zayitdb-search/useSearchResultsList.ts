import { computed, nextTick, type Ref } from 'vue'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'

// Scroll position persistence for virtualized search results
// SAVE: 1) Get scrollOffset 2) Find result index at that offset 3) Calculate offset within result 4) Store in memory
// RESTORE: 1) Read saved index and offset 2) Scroll to saved result 3) Apply saved offset within that result
// MAGIC: Works despite virtualization by saving relative position (result + offset), not absolute pixels

let savedResultIndex: number | undefined
let savedResultOffset = 10

export function useSearchResultsList(
    searchQuery: () => string,
    vListRef: Ref<any>
) {
    const settingsStore = useSettingsStore()

    const containerStyles = computed(() => ({
        backgroundColor: 'var(--reading-bg-primary)',
        color: 'var(--reading-text-primary)'
    }))

    // Find which result index contains a given scroll offset (only searches ~20-50 rendered results)
    function findResultIndex(scrollOffset: number): number | undefined {
        if (!vListRef.value) return undefined
        const container = vListRef.value.$el as HTMLElement
        if (!container) return undefined
        const items = Array.from(container.querySelectorAll('[data-result-index]'))
        if (items.length === 0) return undefined

        for (let i = 0; i < items.length; i++) {
            const item = items[i] as HTMLElement
            const resultIndex = parseInt(item.getAttribute('data-result-index') || '0', 10)
            const itemOffset = vListRef.value.getItemOffset(resultIndex)
            const itemHeight = item.offsetHeight
            if (scrollOffset >= itemOffset && scrollOffset < itemOffset + itemHeight) return resultIndex
        }
        return undefined
    }

    function saveScrollPosition() {
        if (!vListRef.value) return
        const scrollOffset = vListRef.value.scrollOffset
        const topResultIndex = findResultIndex(scrollOffset)
        if (topResultIndex === undefined || topResultIndex === null) return
        const itemOffset = vListRef.value.getItemOffset(topResultIndex)
        const offset = scrollOffset - itemOffset
        savedResultIndex = topResultIndex
        savedResultOffset = offset
    }

    async function restoreScrollPosition() {
        if (!vListRef.value || savedResultIndex === undefined) return
        await nextTick()
        vListRef.value.scrollToIndex(savedResultIndex, { align: 'start' })
        await nextTick()
        await new Promise(resolve => setTimeout(resolve, 100))
        const itemOffset = vListRef.value.getItemOffset(savedResultIndex)
        const targetPosition = itemOffset + savedResultOffset
        vListRef.value.scrollTo(targetPosition)
    }

    function handleScroll() {
        saveScrollPosition()
    }

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

    return {
        containerStyles,
        highlightedSnippet,
        saveScrollPosition,
        restoreScrollPosition,
        handleScroll
    }
}
