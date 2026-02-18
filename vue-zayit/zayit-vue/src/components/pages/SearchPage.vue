<template>
  <div class="flex-column height-fill search-page">
    <!-- Results area with virtualization -->
    <div class="flex-110 results-container"
         :style="resultsContainerStyles">
      <!-- Empty state - no search -->
      <div v-if="!hasSearched"
           class="empty-state flex-column flex-center">
        <Icon icon="fluent:search-sparkle-24-filled"
              width="64"
              height="64"
              class="empty-icon" />
      </div>

      <!-- No results after search completed -->
      <div v-else-if="results.length === 0 && !isSearching"
           class="empty-state flex-column flex-center">
        <Icon icon="fluent:search-sparkle-24-filled"
              width="64"
              height="64"
              class="empty-icon" />
        <div class="empty-message">לא נמצאו תוצאות</div>
      </div>

      <!-- Results list with virtualization -->
      <DynamicScroller v-else
                       ref="scrollerRef"
                       :items="filteredResults"
                       :min-item-size="80"
                       key-field="lineId"
                       class="scroller">
        <template #default="{ item, index, active }">
          <DynamicScrollerItem :item="item"
                               :active="active"
                               :data-index="index"
                               :size-dependencies="[item.snippet]">
            <div class="result-item">
              <div class="result-header"
                   @click="handleResultClick(item)">
                <span class="book-title">{{ item.bookTitle }}</span>
                <span v-if="item.tocText"
                      class="toc-separator">›</span>
                <span v-if="item.tocText"
                      class="toc-text">{{ item.tocText }}</span>
              </div>
              <div class="result-snippet"
                   v-html="highlightSearchTerms(item.snippet, executedQuery)"></div>
            </div>
          </DynamicScrollerItem>
        </template>
      </DynamicScroller>
    </div>

    <!-- Search bar at bottom -->
    <div class="bar search-bar">
      <div class="search-input-wrapper">
        <input ref="searchInputRef"
               v-model="searchQuery"
               type="text"
               class="search-input"
               placeholder="חיפוש בכל הספרים..."
               @keydown.enter="executeSearch"
               @keydown.esc="clearSearch" />
        <button @click="isSearching ? cancelSearch() : executeSearch()"
                class="search-button-inside search-button-left"
                :class="{ 'is-searching': isSearching }"
                :disabled="!isSearching && !searchQuery.trim()"
                :title="isSearching ? 'ביטול חיפוש' : 'חיפוש'">
          <div v-if="isSearching"
               class="search-progress-container">
            <svg class="progress-ring"
                 viewBox="0 0 24 24">
              <circle class="progress-ring-bg"
                      cx="12"
                      cy="12"
                      r="10"
                      fill="none"
                      stroke-width="2" />
              <circle class="progress-ring-spinner"
                      cx="12"
                      cy="12"
                      r="10"
                      fill="none"
                      stroke-width="2"
                      stroke-dasharray="31.4 31.4"
                      stroke-linecap="round" />
            </svg>
            <Icon icon="fluent:dismiss-24-regular"
                  class="cancel-icon" />
          </div>
          <Icon v-else
                icon="fluent:search-24-regular" />
        </button>
        <button @click.stop="toggleFilter"
                class="search-button-inside search-button-right"
                :class="{ 'filter-active': checkedBookIds.size > 0 }"
                :title="checkedBookIds.size > 0 ? `סינון: ${checkedBookIds.size} ספרים` : 'סינון לפי ספרים'">
          <Icon icon="fluent:filter-24-regular" />
        </button>
      </div>
    </div>

    <!-- Filter panel -->
    <CheckedBookTree v-if="isFilterOpen"
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
import { ref, onMounted, onBeforeUnmount, nextTick, watch, computed } from 'vue'
import { Icon } from '@iconify/vue'
import { DynamicScroller, DynamicScrollerItem } from 'vue-virtual-scroller'
import { bloomSearchService } from '../../services/bloomSearchService'
import { bloomSearchCacheService } from '../../services/bloomSearchCacheService'
import { webviewBridge } from '../../services/webviewBridge'
import { dbService } from '../../services/dbService'
import { useTabStore } from '../../stores/tabStore'
import { useCategoryTreeStore } from '../../stores/categoryTreeStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { censorDivineNames } from '../../utils/censorDivineNames'
import { onClickOutside } from '@vueuse/core'
import { useVirtualScrollerPosition } from '../../composables/useVirtualScrollerPosition'
import { useVirtualScrollerKeyboard } from '../../composables/useVirtualScrollerKeyboard'
import type { BloomSearchResult } from '../../types/BloomSearch'
import type { Category } from '../../types/BookCategoryTree'
import CheckedBookTree from '../CheckedBookTree.vue'

const tabStore = useTabStore()
const categoryTreeStore = useCategoryTreeStore()
const settingsStore = useSettingsStore()

// Reactive dark mode detection
const isDarkMode = ref(false)

const updateDarkMode = () => {
  isDarkMode.value = document.documentElement.classList.contains('dark')
}

const searchQuery = ref('')
const executedQuery = ref('') // The query that was actually searched
const searchInputRef = ref<HTMLInputElement | null>(null)
const scrollerRef = ref<InstanceType<typeof DynamicScroller> | null>(null)
const filterPanelRef = ref<InstanceType<typeof CheckedBookTree>>()
const results = ref<BloomSearchResult[]>([])
const isSearching = ref(false)
const hasSearched = ref(false)
const isFilterOpen = ref(false)
const checkedBookIds = ref<Set<number>>(new Set())
const isDev = import.meta.env.DEV
let currentSearchId: string | null = null
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

// Computed styles for reading background
const resultsContainerStyles = computed(() => ({
  backgroundColor: !isDarkMode.value && settingsStore.readingBackgroundColor
    ? settingsStore.readingBackgroundColor
    : 'var(--bg-primary)',
  color: !isDarkMode.value && settingsStore.readingBackgroundColor
    ? 'var(--reading-text-color)'
    : 'var(--text-primary)'
}))

// Get current tab's search state
const currentSearchState = computed(() => {
  const tab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'kezayit-search')
  return tab?.searchState
})

// Position manager for virtual scroller with auto-persistence
const positionId = ref('search-results')
const positionManager = useVirtualScrollerPosition(scrollerRef, positionId)

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

// Sample data for dev mode
const sampleResults: BloomSearchResult[] = [
  {
    lineId: 1,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק א',
    score: 0.95,
    proximityScore: 0.9,
    snippet: 'בראשית ברא אלקים את השמים ואת הארץ'
  },
  {
    lineId: 2,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק א',
    score: 0.92,
    proximityScore: 0.88,
    snippet: 'והארץ היתה תהו ובהו וחשך על פני תהום'
  },
  {
    lineId: 3,
    bookId: 2,
    bookTitle: 'שמות',
    tocText: 'פרק א',
    score: 0.88,
    proximityScore: 0.85,
    snippet: 'ואלה שמות בני ישראל הבאים מצרימה'
  },
  {
    lineId: 4,
    bookId: 3,
    bookTitle: 'ויקרא',
    tocText: 'פרק א',
    score: 0.85,
    proximityScore: 0.82,
    snippet: 'ויקרא אל משה וידבר אליו מאהל מועד'
  },
  {
    lineId: 5,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק ב',
    score: 0.82,
    proximityScore: 0.8,
    snippet: 'ויכלו השמים והארץ וכל צבאם'
  }
]

// Restore state on mount
onMounted(async () => {
  // Initialize dark mode
  updateDarkMode()
  const observer = new MutationObserver(updateDarkMode)
  observer.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['class']
  })

  // Initialize with all books checked by default
  if (!isInitialized.value && categoryTreeStore.allBooks.length > 0) {
    const allBookIds = categoryTreeStore.allBooks.map(b => b.id)
    checkedBookIds.value = new Set(allBookIds)
    isInitialized.value = true
  }

  if (currentSearchState.value) {
    searchQuery.value = currentSearchState.value.searchQuery
    executedQuery.value = currentSearchState.value.searchQuery // Set executed query to match
    hasSearched.value = currentSearchState.value.hasSearched

    // Restore tab title if there's a search query
    if (currentSearchState.value.searchQuery.trim()) {
      const currentTab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'kezayit-search')
      if (currentTab) {
        currentTab.title = `חיפוש: ${currentSearchState.value.searchQuery}`
      }
    }

    // Load results from cache (not from tab state)
    if (hasSearched.value && searchQuery.value.trim()) {
      const normalizedQuery = searchQuery.value.trim().toLowerCase()
      const cachedResults = await bloomSearchCacheService.get(normalizedQuery)

      if (cachedResults !== null) {
        results.value = cachedResults
        // Position is automatically restored by composable
      } else {
        // Cache miss - re-execute search
        await executeSearch()
      }
    }
  }

  nextTick(() => {
    searchInputRef.value?.focus()
  })
})

// Watch for changes and save to state (but not results)
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
  // Trigger reactivity
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
  // Trigger reactivity
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

// Root checkbox state
const isAllChecked = computed(() => {
  const totalBooks = categoryTreeStore.allBooks.length
  if (totalBooks === 0) return false
  return checkedBookIds.value.size === totalBooks
})

const isIndeterminate = computed(() => {
  const totalBooks = categoryTreeStore.allBooks.length
  if (totalBooks === 0) return false
  return checkedBookIds.value.size > 0 && checkedBookIds.value.size < totalBooks
})

const handleRootCheckboxToggle = () => {
  if (isAllChecked.value) {
    uncheckAllBooks()
  } else {
    checkAllBooks()
  }
}

// Cancel search
const cancelSearch = async () => {
  if (currentSearchId) {
    console.log('[SearchPage] User cancelled search:', currentSearchId)
    try {
      await webviewBridge.bloomSearchCancel(currentSearchId)
      webviewBridge.unregisterSearchListener(currentSearchId)
    } catch (error) {
      console.error('[SearchPage] Error cancelling search:', error)
    }
    currentSearchId = null
    isSearching.value = false
  }
}

// Execute search
const executeSearch = async () => {
  if (!searchQuery.value.trim()) {
    return
  }

  // Update tab title with search query
  const currentTab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'kezayit-search')
  if (currentTab) {
    currentTab.title = `חיפוש: ${searchQuery.value}`
  }

  // Cancel previous search completely across all layers
  if (currentSearchId) {
    console.log('[SearchPage] Cancelling previous search:', currentSearchId)
    try {
      // Unregister listener first to stop receiving messages
      webviewBridge.unregisterSearchListener(currentSearchId)
      // Cancel in C# backend
      await webviewBridge.bloomSearchCancel(currentSearchId)
    } catch (error) {
      console.error('[SearchPage] Error cancelling previous search:', error)
    }
    currentSearchId = null
  }

  // Clear UI state immediately
  isSearching.value = true
  hasSearched.value = true
  results.value = []

  // Store the query being executed for highlighting
  executedQuery.value = searchQuery.value

  console.log('[SearchPage] Executing streaming search:', searchQuery.value)

  try {
    if (isDev) {
      const isReady = await bloomSearchService.isReady()
      if (!isReady) {
        console.log('[SearchPage] Dev mode: Using sample data')
        await new Promise(resolve => setTimeout(resolve, 500))
        results.value = sampleResults
        console.log('[SearchPage] Dev mode: Loaded sample results:', sampleResults.length)
        isSearching.value = false
        return
      }
    }

    const normalizedQuery = searchQuery.value.trim().toLowerCase()
    const cachedResults = await bloomSearchCacheService.get(normalizedQuery)
    if (cachedResults !== null) {
      console.log('[SearchPage] Using cached results:', cachedResults.length)
      results.value = cachedResults
      isSearching.value = false
      return
    }

    const searchId = await webviewBridge.bloomSearchStart(searchQuery.value)
    currentSearchId = searchId
    console.log('[SearchPage] Search started with ID:', searchId)

    webviewBridge.registerSearchListener(
      searchId,
      (batchResults) => {
        if (currentSearchId === searchId) {
          console.log('[SearchPage] Received batch:', batchResults.length, 'results')
          results.value = [...results.value, ...batchResults]
        }
      },
      async () => {
        if (currentSearchId === searchId) {
          console.log('[SearchPage] Search completed, total results:', results.value.length)
          isSearching.value = false

          if (results.value.length > 0) {
            const cleanResults = results.value.map(r => ({
              lineId: r.lineId,
              bookId: r.bookId,
              bookTitle: r.bookTitle,
              tocText: r.tocText,
              score: r.score,
              proximityScore: r.proximityScore,
              snippet: r.snippet
            }))
            // Store in cache only (not in tab state)
            await bloomSearchCacheService.set(normalizedQuery, cleanResults)
          }

          currentSearchId = null
        }
      },
      () => {
        if (currentSearchId === searchId) {
          console.log('[SearchPage] Search cancelled')
          isSearching.value = false
          currentSearchId = null
        }
      },
      (error) => {
        if (currentSearchId === searchId) {
          console.error('[SearchPage] Search error:', error)
          isSearching.value = false
          currentSearchId = null

          if (isDev) {
            console.log('[SearchPage] Dev mode: Error fallback to sample data')
            results.value = sampleResults
          }
        }
      }
    )
  } catch (error) {
    console.error('[SearchPage] Search error:', error)
    isSearching.value = false

    if (isDev) {
      console.log('[SearchPage] Dev mode: Error fallback to sample data')
      results.value = sampleResults
    }
  }
}

// Clear search
const clearSearch = () => {
  searchQuery.value = ''
  executedQuery.value = ''
  results.value = []
  hasSearched.value = false
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

  searchInputRef.value?.focus()
}

// Handle result click
const handleResultClick = async (result: BloomSearchResult) => {
  console.log('[SearchPage] Result clicked:', result)

  try {
    const lineInfo = await bloomSearchService.getLineIndexFromLineId(result.lineId)

    if (!lineInfo) {
      console.error('[SearchPage] Failed to get line index for lineId:', result.lineId)
      return
    }

    console.log('[SearchPage] Opening book:', result.bookTitle, 'at line index:', lineInfo.lineIndex, 'with highlight terms:', executedQuery.value, 'snippet:', result.snippet)

    const hasConnections = await checkBookHasConnections(result.bookId)

    tabStore.openBookInNewTab(
      result.bookTitle,
      result.bookId,
      hasConnections,
      lineInfo.lineIndex,
      true, // shouldHighlight = true for search results
      executedQuery.value, // Pass search terms for highlighting
      result.snippet // Pass snippet for background highlighting
    )
  } catch (error) {
    console.error('[SearchPage] Error opening book:', error)
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
    console.error('[SearchPage] Error checking book connections:', error)
    return false
  }
}

// Highlight search terms
const highlightSearchTerms = (snippet: string, query: string): string => {
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

  terms.forEach(term => {
    if (term.length > 0) {
      const escapedTerm = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
      const regex = new RegExp(`(${escapedTerm})`, 'gi')
      highlighted = highlighted.replace(regex, '<mark>$1</mark>')
    }
  })

  return highlighted
}
</script>

<style scoped>
.search-page {
  position: relative;
}

.results-container {
  overflow: hidden;
  position: relative;
}

.scroller {
  height: 100%;
}

.empty-state {
  height: 100%;
  padding: 2rem;
  gap: 1rem;
  justify-content: center;
  align-items: center;
}

.empty-icon {
  color: var(--color-text-tertiary);
  opacity: 0.3;
}

.empty-message {
  font-size: 1.1rem;
  color: var(--color-text-secondary);
}

.result-item {
  padding: 8px 16px;
  border-bottom: 1px solid var(--color-border);
}

.result-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
  font-family: var(--header-font);
  font-weight: 500;
  cursor: pointer;
  padding: 4px 0;
  width: fit-content;
  position: relative;
  text-decoration: none;
  transition: opacity 0.15s ease;
}

.result-header:hover {
  opacity: 0.7;
}

.result-header::after {
  content: '';
  position: absolute;
  bottom: 0;
  right: -1%;
  left: -1%;
  height: 1px;
  background-color: currentColor;
  opacity: 0.2;
}

.result-header:hover .book-title {
  opacity: 1;
}

.book-title {
  color: var(--accent-color);
}

.toc-separator {
  color: var(--color-text-tertiary);
  font-size: 0.9rem;
}

.toc-text {
  color: var(--text-secondary);
  font-size: 0.9rem;
}

.result-snippet {
  font-family: var(--text-font);
  font-size: var(--font-size, 100%);
  line-height: var(--line-height, 1.5);
  color: var(--color-text-secondary);
  direction: rtl;
  text-align: justify;
  cursor: text;
  user-select: text;
}

.result-snippet :deep(mark) {
  background-color: transparent;
  color: var(--accent-color);
  font-weight: 600;
  padding: 0;
}

.search-bar {
  padding: 12px;
  border-top: 1px solid var(--color-border);
}

.search-input-wrapper {
  position: relative;
  width: 100%;
}

.search-input {
  width: 100%;
  padding: 10px 48px 10px 48px;
  border: 1px solid var(--border-color);
  border-radius: 20px;
  background-color: var(--bg-primary);
  color: var(--text-primary);
  font-size: 15px;
  direction: rtl;
  text-align: right;
  transition: border-color 0.15s ease;
  height: 40px;
}

.search-input:focus {
  border-color: var(--accent-color);
  box-shadow: 0 0 0 0.5px var(--accent-color);
  outline: none;
}

.search-input::placeholder {
  color: var(--text-secondary);
  opacity: 1;
}

.search-button-inside {
  position: absolute;
  top: 50%;
  transform: translateY(-50%);
  padding: 6px;
  border-radius: 6px;
  background: transparent;
  border: none;
  color: var(--color-text-primary);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background-color 0.15s ease, color 0.15s ease;
}

.search-button-left {
  left: 8px;
}

.search-button-right {
  right: 8px;
}

.search-button-inside:hover:not(:disabled) {
  background-color: var(--hover-bg);
  color: var(--accent-color);
}

.search-button-inside:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.search-button-inside.is-searching {
  cursor: pointer;
}

.filter-active {
  color: var(--accent-color);
}

.filter-badge {
  position: absolute;
  top: -4px;
  right: -4px;
  background-color: var(--accent-color);
  color: white;
  font-size: 10px;
  font-weight: 600;
  padding: 2px 4px;
  border-radius: 8px;
  min-width: 16px;
  text-align: center;
}

.search-progress-container {
  position: relative;
  width: 20px;
  height: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.cancel-icon {
  position: absolute;
  width: 14px;
  height: 14px;
  color: var(--text-secondary);
}

.progress-ring {
  width: 20px;
  height: 20px;
  animation: rotate 1s linear infinite;
}

.progress-ring-bg {
  stroke: var(--border-color);
}

.progress-ring-spinner {
  stroke: var(--accent-color);
  transform-origin: center;
}

@keyframes rotate {
  from {
    transform: rotate(0deg);
  }

  to {
    transform: rotate(360deg);
  }
}
</style>
