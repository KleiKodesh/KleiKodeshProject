<template>
  <div class="flex-column height-fill search-page">
    <!-- Results area -->
    <SearchResultsList ref="resultsListRef"
                       :results="filteredResults"
                       :search-query="executedQuery"
                       :is-searching="isSearching"
                       :has-searched="hasSearched"
                       @result-click="handleResultClick" />

    <!-- Search bar at bottom -->
    <SearchBar ref="searchBarRef"
               v-model:search-query="searchQuery"
               :is-searching="isSearching"
               :filter-count="checkedBookIds.size"
               @search="handleSearch"
               @cancel="cancelSearch"
               @toggle-filter="toggleFilter"
               @clear="handleClearSearch" />

    <!-- Filter panel -->
    <FsCheckedTree v-if="isFilterOpen"
                   ref="filterPanelRef"
                   :checked-book-ids="checkedBookIds"
                   :result-counts="resultCounts"
                   @toggle-book="toggleBook"
                   @toggle-category="toggleCategory"
                   @check-all="checkAllBooks"
                   @uncheck-all="uncheckAllBooks"
                   @close="isFilterOpen = false" />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick, watch, computed } from 'vue'
import { bloomSearchService } from '@/data/services/bloomSearchService'
import { dbService } from '@/data/services/dbService'
import { useTabStore } from '@/data/stores/tabStore'
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore'
import { onClickOutside } from '@vueuse/core'
import { useVirtualScrollerPosition } from '@/components/shared/useVirtualScrollerPosition'
import { useVirtualScrollerKeyboard } from '@/components/shared/useVirtualScrollerKeyboard'
import { useBloomSearch } from '@/components/zayitdb-search/useBloomSearch'
import type { BloomSearchResult } from '@/data/types/BloomSearch'
import type { Category } from '@/data/types/BookCategoryTree'
import FsCheckedTree from '@/components/zayitdb-search/FsCheckedTree.vue'
import SearchBar from '@/components/zayitdb-search/SearchBar.vue'
import SearchResultsList from '@/components/zayitdb-search/SearchResultsList.vue'

const tabStore = useTabStore()
const categoryTreeStore = useCategoryTreeStore()

// Use bloom search composable
const {
  results,
  isSearching,
  hasSearched,
  executedQuery,
  executeSearch: executeSearchComposable,
  cancelSearch,
  clearSearch: clearSearchComposable,
  loadCachedResults
} = useBloomSearch()

const searchQuery = ref('')
const searchBarRef = ref<InstanceType<typeof SearchBar> | null>(null)
const resultsListRef = ref<InstanceType<typeof SearchResultsList> | null>(null)
const filterPanelRef = ref<InstanceType<typeof FsCheckedTree>>()
const isFilterOpen = ref(false)
const checkedBookIds = ref<Set<number>>(new Set())
let isInitialized = ref(false)

// Computed ref to get the actual DOM element from the component
const filterPanelElement = computed(() => {
  return filterPanelRef.value?.$el as HTMLElement | undefined
})

// Close filter panel when clicking outside
onClickOutside(filterPanelElement, () => {
  if (isFilterOpen.value) {
    isFilterOpen.value = false
  }
})

// Get current tab's search state
const currentSearchState = computed(() => {
  const tab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'kezayit-search')
  return tab?.searchState
})

// Get scroller ref from results list
const scrollerRef = computed(() => resultsListRef.value?.scrollerRef || null)

// Position manager for virtual scroller with auto-persistence
const positionId = ref('search-results')
useVirtualScrollerPosition(scrollerRef, positionId)

// Keyboard navigation for virtual scroller
useVirtualScrollerKeyboard(
  scrollerRef,
  computed(() => filteredResults.value.length)
)

// Filter results based on checked books
const filteredResults = computed(() => {
  if (checkedBookIds.value.size === 0) {
    return results.value
  }
  return results.value.filter(result => checkedBookIds.value.has(result.bookId))
})

// Calculate result counts per book
const resultCounts = computed(() => {
  const counts = new Map<number, number>()
  results.value.forEach(result => {
    counts.set(result.bookId, (counts.get(result.bookId) || 0) + 1)
  })
  return counts
})

// Restore state on mount
onMounted(async () => {
  // Initialize with all books checked by default
  if (!isInitialized.value && categoryTreeStore.allBooks.length > 0) {
    const allBookIds = categoryTreeStore.allBooks.map(b => b.id)
    checkedBookIds.value = new Set(allBookIds)
    isInitialized.value = true
  }

  if (currentSearchState.value) {
    searchQuery.value = currentSearchState.value.searchQuery
    hasSearched.value = currentSearchState.value.hasSearched

    // Restore tab title if there's a search query
    if (currentSearchState.value.searchQuery.trim()) {
      const currentTab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'kezayit-search')
      if (currentTab) {
        currentTab.title = `חיפוש: ${currentSearchState.value.searchQuery}`
      }
    }

    // Load results from cache
    if (hasSearched.value && searchQuery.value.trim()) {
      const loaded = await loadCachedResults(searchQuery.value)

      if (!loaded) {
        // Cache miss - re-execute search
        await handleSearch(searchQuery.value)
      }
    }
  }

  nextTick(() => {
    searchBarRef.value?.focus()
  })
})

// Watch for changes and save to state
watch([searchQuery, hasSearched], () => {
  if (currentSearchState.value) {
    currentSearchState.value.searchQuery = searchQuery.value
    currentSearchState.value.hasSearched = hasSearched.value
  }
})

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

  await executeSearchComposable(query)
}

// Clear search
const handleClearSearch = () => {
  clearSearchComposable()
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

  searchBarRef.value?.focus()
}

// Handle result click
const handleResultClick = async (result: BloomSearchResult) => {
  console.log('[ZayitSearchPage] Result clicked:', result)

  try {
    const lineInfo = await bloomSearchService.getLineIndexFromLineId(result.lineId)

    if (!lineInfo) {
      console.error('[ZayitSearchPage] Failed to get line index for lineId:', result.lineId)
      return
    }

    console.log('[ZayitSearchPage] Opening book:', result.bookTitle, 'at line index:', lineInfo.lineIndex, 'with highlight terms:', executedQuery.value, 'snippet:', result.snippet)

    const hasConnections = await checkBookHasConnections(result.bookId)

    tabStore.openBookInNewTab(
      result.bookTitle,
      result.bookId,
      hasConnections,
      lineInfo.lineIndex,
      true,
      executedQuery.value,
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
</script>

<style scoped>
.search-page {
  position: relative;
}
</style>
