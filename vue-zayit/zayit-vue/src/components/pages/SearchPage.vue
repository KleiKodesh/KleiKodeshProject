<template>
  <div class="flex-column height-fill search-page">
    <!-- Results area with virtualization -->
    <div class="flex-110 results-container">
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

      <!-- Results list with virtualization (show even while searching) -->
      <DynamicScroller v-else
                       :items="results"
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
                   v-html="highlightSearchTerms(item.snippet, searchQuery)"></div>
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
        <button @click="executeSearch"
                class="search-button-inside"
                :disabled="!searchQuery.trim() || isSearching"
                title="חיפוש">
          <svg v-if="isSearching"
               class="progress-ring"
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
          <Icon v-else
                icon="fluent:search-24-regular" />
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick } from 'vue'
import { Icon } from '@iconify/vue'
import { DynamicScroller, DynamicScrollerItem } from 'vue3-virtual-scroller'
import { bloomSearchService } from '../../services/bloomSearchService'
import { bloomSearchCacheService } from '../../services/bloomSearchCacheService'
import { webviewBridge } from '../../services/webviewBridge'
import { dbService } from '../../services/dbService'
import { useTabStore } from '../../stores/tabStore'
import type { BloomSearchResult } from '../../types/BloomSearch'

const tabStore = useTabStore()

const searchQuery = ref('')
const searchInputRef = ref<HTMLInputElement | null>(null)
const results = ref<BloomSearchResult[]>([])
const isSearching = ref(false)
const hasSearched = ref(false)
const isDev = import.meta.env.DEV
let currentSearchId: string | null = null

// Sample data for dev mode
const sampleResults: BloomSearchResult[] = [
  {
    lineId: 1,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק א',
    score: 0.95,
    proximityScore: 0.9,
    snippet: 'בְּרֵאשִׁית בָּרָא אֱלֹהִים אֵת הַשָּׁמַיִם וְאֵת הָאָרֶץ'
  },
  {
    lineId: 2,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק א',
    score: 0.92,
    proximityScore: 0.88,
    snippet: 'וְהָאָרֶץ הָיְתָה תֹהוּ וָבֹהוּ וְחֹשֶׁךְ עַל פְּנֵי תְהוֹם'
  },
  {
    lineId: 3,
    bookId: 2,
    bookTitle: 'שמות',
    tocText: 'פרק א',
    score: 0.88,
    proximityScore: 0.85,
    snippet: 'וְאֵלֶּה שְׁמוֹת בְּנֵי יִשְׂרָאֵל הַבָּאִים מִצְרָיְמָה'
  },
  {
    lineId: 4,
    bookId: 3,
    bookTitle: 'ויקרא',
    tocText: 'פרק א',
    score: 0.85,
    proximityScore: 0.82,
    snippet: 'וַיִּקְרָא אֶל מֹשֶׁה וַיְדַבֵּר ה\' אֵלָיו מֵאֹהֶל מוֹעֵד'
  },
  {
    lineId: 5,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק ב',
    score: 0.82,
    proximityScore: 0.8,
    snippet: 'וַיְכֻלּוּ הַשָּׁמַיִם וְהָאָרֶץ וְכָל צְבָאָם'
  }
]

// Focus search input on mount
onMounted(() => {
  nextTick(() => {
    searchInputRef.value?.focus()
  })
})

// Execute search with event-driven streaming
const executeSearch = async () => {
  if (!searchQuery.value.trim()) {
    return
  }

  console.log('[SearchPage] Executing streaming search:', searchQuery.value)

  // Cancel previous search if still running
  if (currentSearchId) {
    console.log('[SearchPage] Cancelling previous search:', currentSearchId)
    try {
      await webviewBridge.bloomSearchCancel(currentSearchId)
      webviewBridge.unregisterSearchListener(currentSearchId)
    } catch (error) {
      console.error('[SearchPage] Error cancelling previous search:', error)
    }
    currentSearchId = null
  }

  isSearching.value = true
  hasSearched.value = true
  results.value = []

  try {
    // In dev mode, use sample data if search service is not available
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

    // Check cache first
    const normalizedQuery = searchQuery.value.trim().toLowerCase()
    const cachedResults = await bloomSearchCacheService.get(normalizedQuery)
    if (cachedResults !== null) {
      console.log('[SearchPage] Using cached results:', cachedResults.length)
      results.value = cachedResults
      isSearching.value = false
      return
    }

    // Start streaming search
    const searchId = await webviewBridge.bloomSearchStart(searchQuery.value)
    currentSearchId = searchId
    console.log('[SearchPage] Search started with ID:', searchId)

    // Register listener for streaming results
    webviewBridge.registerSearchListener(
      searchId,
      // onBatch
      (batchResults) => {
        if (currentSearchId === searchId) {
          console.log('[SearchPage] Received batch:', batchResults.length, 'results')
          // Append immediately - Vue's reactivity will handle the update
          results.value = [...results.value, ...batchResults]
        }
      },
      // onComplete
      async () => {
        if (currentSearchId === searchId) {
          console.log('[SearchPage] Search completed, total results:', results.value.length)
          isSearching.value = false

          // Cache the complete results (create clean copies to avoid cloning issues)
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
            await bloomSearchCacheService.set(normalizedQuery, cleanResults)
          }

          currentSearchId = null
        }
      },
      // onCancelled
      () => {
        if (currentSearchId === searchId) {
          console.log('[SearchPage] Search cancelled')
          isSearching.value = false
          currentSearchId = null
        }
      },
      // onError
      (error) => {
        if (currentSearchId === searchId) {
          console.error('[SearchPage] Search error:', error)
          isSearching.value = false
          currentSearchId = null

          // Fallback to sample data in dev mode
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

    // Fallback to sample data in dev mode on error
    if (isDev) {
      console.log('[SearchPage] Dev mode: Error fallback to sample data')
      results.value = sampleResults
    }
  }
}

// Clear search
const clearSearch = () => {
  searchQuery.value = ''
  results.value = []
  hasSearched.value = false
  searchInputRef.value?.focus()
}

// Handle result click - open book at specific line
const handleResultClick = async (result: BloomSearchResult) => {
  console.log('[SearchPage] Result clicked:', result)

  try {
    // Get line index from line ID
    const lineInfo = await bloomSearchService.getLineIndexFromLineId(result.lineId)

    if (!lineInfo) {
      console.error('[SearchPage] Failed to get line index for lineId:', result.lineId)
      return
    }

    console.log('[SearchPage] Opening book:', result.bookTitle, 'at line index:', lineInfo.lineIndex)

    // Check if book has connections by fetching book data
    const hasConnections = await checkBookHasConnections(result.bookId)

    // Open book in new tab at specific line
    tabStore.openBookInNewTab(
      result.bookTitle,
      result.bookId,
      hasConnections,
      lineInfo.lineIndex
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

// Highlight search terms in snippet
const highlightSearchTerms = (snippet: string, query: string): string => {
  if (!query || !snippet) {
    return snippet
  }

  // Split query into terms
  const terms = query.trim().split(/\s+/)

  let highlighted = snippet

  // Highlight each term
  terms.forEach(term => {
    if (term.length > 0) {
      // Escape special regex characters
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

/* Search progress */
.search-progress {
  height: 100%;
  padding: 2rem;
  gap: 1rem;
  justify-content: center;
  align-items: center;
}

.spinner-icon {
  color: var(--color-primary);
}

.search-message {
  font-size: 1.1rem;
  color: var(--color-text-secondary);
}

/* Empty state */
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

/* Result item */
.result-item {
  padding: 12px 16px;
  border-bottom: 1px solid var(--color-border);
}

.result-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
  font-weight: 500;
  cursor: pointer;
  padding: 4px 0;
  width: fit-content;
  text-decoration: none;
  transition: text-decoration 0.15s ease;
}

.result-header:hover {
  text-decoration: underline;
}

.book-title {
  color: #2563eb;
}

:root.dark .book-title {
  color: #60a5fa;
}

.toc-separator {
  color: var(--color-text-tertiary);
  font-size: 0.9rem;
}

.toc-text {
  color: #7c3aed;
  font-size: 0.9rem;
}

:root.dark .toc-text {
  color: #a78bfa;
}

.result-snippet {
  font-size: 0.95rem;
  line-height: 1.5;
  color: var(--color-text-secondary);
  direction: rtl;
  cursor: text;
  user-select: text;
}

.result-snippet :deep(mark) {
  background-color: transparent;
  color: #f59e0b;
  font-weight: 600;
  padding: 0;
}

:root.dark .result-snippet :deep(mark) {
  background-color: transparent;
  color: #fbbf24;
}

/* Search bar */
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
  padding: 10px 48px 10px 16px;
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
  left: 8px;
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

.search-button-inside:hover:not(:disabled) {
  background-color: var(--color-hover);
  color: var(--accent-color);
}

.search-button-inside:disabled {
  opacity: 0.4;
  cursor: not-allowed;
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
