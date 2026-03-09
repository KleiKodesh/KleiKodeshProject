import { ref, computed, watch, onMounted, nextTick } from 'vue'
import { useTabStore } from '@/data/stores/tabStore'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { bloomSearchService } from '@/data/services/bloomSearchService'
import { dbService } from '@/data/services/dbService'
import type { BloomSearchResult } from '@/data/types/BloomSearch'
import type { Category } from '@/data/types/BookCategoryTree'

export function useZayitSearchPage(
    results: () => BloomSearchResult[],
    executedQuery: () => string,
    executeSearch: (query: string) => Promise<void>,
    clearSearch: () => void
) {
    const tabStore = useTabStore()
    const categoryTreeStore = useCategoryTreeStore()

    const searchQuery = ref('')
    const isFilterOpen = ref(false)
    const checkedBookIds = ref<Set<number>>(new Set())
    const isInitialized = ref(false)

    // Get current tab's search state
    const currentSearchState = computed(() => {
        const tab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'kezayit-search')
        return tab?.searchState
    })

    // Filter results based on checked books
    const filteredResults = computed(() => {
        if (checkedBookIds.value.size === 0) {
            return results()
        }
        return results().filter(result => checkedBookIds.value.has(result.bookId))
    })

    // Calculate result counts per book
    const resultCounts = computed(() => {
        const counts = new Map<number, number>()
        results().forEach(result => {
            counts.set(result.bookId, (counts.get(result.bookId) || 0) + 1)
        })
        return counts
    })

    // Initialize with all books checked
    const initializeCheckedBooks = () => {
        if (!isInitialized.value && categoryTreeStore.allBooks.length > 0) {
            const allBookIds = categoryTreeStore.allBooks.map(b => b.id)
            checkedBookIds.value = new Set(allBookIds)
            isInitialized.value = true
        }
    }

    // Toggle filter panel
    const toggleFilter = () => {
        isFilterOpen.value = !isFilterOpen.value
    }

    // Toggle individual book
    const toggleBook = (bookId: number) => {
        if (checkedBookIds.value.has(bookId)) {
            checkedBookIds.value.delete(bookId)
        } else {
            checkedBookIds.value.add(bookId)
        }
        checkedBookIds.value = new Set(checkedBookIds.value)
    }

    // Toggle category (all books in category and subcategories)
    const toggleCategory = (category: Category, checked: boolean) => {
        const getAllBookIds = (cat: Category): number[] => {
            const bookIds = cat.books.map(b => b.id)
            cat.children.forEach(child => {
                bookIds.push(...getAllBookIds(child))
            })
            return bookIds
        }

        const bookIds = getAllBookIds(category)
        bookIds.forEach(id => {
            if (checked) {
                checkedBookIds.value.add(id)
            } else {
                checkedBookIds.value.delete(id)
            }
        })
        checkedBookIds.value = new Set(checkedBookIds.value)
    }

    // Check all books
    const checkAllBooks = () => {
        const allBookIds = categoryTreeStore.allBooks.map(b => b.id)
        checkedBookIds.value = new Set(allBookIds)
    }

    // Uncheck all books
    const uncheckAllBooks = () => {
        checkedBookIds.value = new Set()
    }

    // Execute search
    const handleSearch = async (query: string) => {
        // Update tab title with search query
        const currentTab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'kezayit-search')
        if (currentTab) {
            currentTab.title = `חיפוש: ${query}`
        }

        await executeSearch(query)
    }

    // Clear search
    const handleClearSearch = () => {
        clearSearch()
        searchQuery.value = ''

        if (currentSearchState.value) {
            currentSearchState.value.scrollPosition = 0
            currentSearchState.value.firstVisibleItemIndex = undefined
            currentSearchState.value.itemOffset = undefined
        }

        // Reset tab title to default
        const currentTab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'kezayit-search')
        if (currentTab) {
            currentTab.title = 'חיפוש'
        }
    }

    // Handle result click
    const handleResultClick = async (result: BloomSearchResult) => {
        try {
            const lineInfo = await bloomSearchService.getLineIndexFromLineId(result.lineId)

            if (!lineInfo) {
                console.error('[ZayitSearchPage] Failed to get line index for lineId:', result.lineId)
                return
            }

            const hasConnections = await checkBookHasConnections(result.bookId)

            tabStore.openBookInNewTab(
                result.bookTitle,
                result.bookId,
                hasConnections,
                lineInfo.lineIndex,
                true,
                executedQuery(),
                result.snippet
            )
        } catch (error) {
            console.error('[ZayitSearchPage] Error opening book:', error)
        }
    }

    // Check if book has connections
    const checkBookHasConnections = async (bookId: number): Promise<boolean> => {
        try {
            const { booksFlat } = await dbService.getTree()
            const book = booksFlat.find(b => b.id === bookId)
            if (book) {
                return book.hasTargumConnection > 0 ||
                    book.hasReferenceConnection > 0 ||
                    book.hasCommentaryConnection > 0 ||
                    book.hasOtherConnection > 0 ||
                    book.hasSourceConnection > 0
            }
            return false
        } catch (error) {
            console.error('[ZayitSearchPage] Error checking book connections:', error)
            return false
        }
    }

    // Watch for changes and save to state
    const setupStateWatchers = (hasSearched: () => boolean) => {
        watch([searchQuery, hasSearched], () => {
            if (currentSearchState.value) {
                currentSearchState.value.searchQuery = searchQuery.value
                currentSearchState.value.hasSearched = hasSearched()
            }
        })
    }

    return {
        searchQuery,
        isFilterOpen,
        checkedBookIds,
        currentSearchState,
        filteredResults,
        resultCounts,
        initializeCheckedBooks,
        toggleFilter,
        toggleBook,
        toggleCategory,
        checkAllBooks,
        uncheckAllBooks,
        handleSearch,
        handleClearSearch,
        handleResultClick,
        setupStateWatchers
    }
}
