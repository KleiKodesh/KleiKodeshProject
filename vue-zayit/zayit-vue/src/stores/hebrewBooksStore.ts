import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { HebrewBook } from '../types/HebrewBook'
import { CsvLoader } from '../services/hebrewBooksCsvLoader'
import { PopularityManager } from '../services/hebrewBooksPopularityManager'
import { HebrewBooksSearchService } from '../services/hebrewBooksSearchService'

// Create debounced search function outside store to persist across instances
let globalDebouncedSearch: ((value: string) => void) | null = null

export const useHebrewBooksStore = defineStore('hebrewBooks', () => {
  // State
  const books = ref<HebrewBook[]>([])
  const filteredBooks = ref<HebrewBook[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const searchTerm = ref('')
  const debouncedSearchTerm = ref('')

  // Navigation state
  const currentView = ref<'list' | 'viewer'>('list')
  const selectedBookId = ref<string | null>(null)

  // Getters
  const hasBooks = computed(() => books.value.length > 0)
  const selectedBook = computed(() => {
    if (!selectedBookId.value) return null
    return books.value.find(book => book.ID_Book === selectedBookId.value) || null
  })

  // Actions
  const loadBooks = async () => {
    if (books.value.length > 0) {
      // Books already loaded, don't load again
      return
    }

    isLoading.value = true
    error.value = null

    try {
      const loadedBooks = await CsvLoader.loadBooks()
      books.value = await PopularityManager.loadUserInteractions(loadedBooks)
      // Load initial filtered books
      await updateFilteredBooks()
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load books'
      console.error('Error loading books:', err)
    } finally {
      isLoading.value = false
    }
  }

  const trackBookInteraction = async (bookId: string) => {
    books.value = await PopularityManager.trackBookInteraction(books.value, bookId)
    // Update filtered books after interaction
    await updateFilteredBooks()
  }

  const updateSearchTerm = (term: string) => {
    searchTerm.value = term
  }

  const updateDebouncedSearchTerm = async (term: string) => {
    debouncedSearchTerm.value = term
    await updateFilteredBooks()
  }

  // Update filtered books based on current search term
  const updateFilteredBooks = async () => {
    filteredBooks.value = await HebrewBooksSearchService.getFilteredBooks(books.value, debouncedSearchTerm.value)
  }

  // Create debounced search function once globally
  if (!globalDebouncedSearch) {
    globalDebouncedSearch = HebrewBooksSearchService.createDebouncedSearch((value: string) => {
      debouncedSearchTerm.value = value
      updateFilteredBooks() // Remove await since callback should return void
    }, 300)
  }

  const performDebouncedSearch = (term: string) => {
    updateSearchTerm(term)
    if (globalDebouncedSearch) {
      globalDebouncedSearch(term)
    }
  }

  // Navigation actions
  const openBookViewer = (bookId: string) => {
    selectedBookId.value = bookId
    currentView.value = 'viewer'
  }

  const closeBookViewer = () => {
    selectedBookId.value = null
    currentView.value = 'list'
  }

  // Hebrew Books PDF actions
  const openHebrewBookViewer = async (bookId: string, title: string) => {
    console.log(`[HebrewBooks] Starting openHebrewBookViewer - bookId: ${bookId}, title: ${title}`)

    // Import services dynamically to avoid circular dependencies
    const { useTabStore } = await import('../stores/tabStore')
    const { webviewHebrewBooks } = await import('../services/webviewHebrewBooks')
    const tabStore = useTabStore()

    // Navigate to Hebrew books view page and set title immediately
    console.log('[HebrewBooks] Navigating to Hebrew books view page')
    tabStore.setPage('hebrewbooks-view')

    // Set tab title to book title immediately
    const tab = tabStore.activeTab
    if (tab) {
      tab.title = title
      console.log('[HebrewBooks] Set tab title to:', title)
    }

    // Use unified Hebrew Books service
    if (webviewHebrewBooks.isAvailable()) {
      console.log('[HebrewBooks] WebView bridge available, starting Hebrew book viewing flow')
      try {
        const result = await webviewHebrewBooks.prepareForViewing(bookId, title)

        if (result.success) {
          if (result.cached && result.url) {
            // File was cached, set PDF state immediately
            console.log('[HebrewBooks] File was cached, setting PDF state:', result.url)
            if (tab) {
              tab.pdfState = {
                fileName: `${title}.pdf`,
                fileUrl: result.url,
                source: 'hebrewbook',
                bookId: bookId,
                bookTitle: title
              }
            }
          } else {
            // File not cached, trigger download and wait for completion
            console.log('[HebrewBooks] File not cached, triggering download')
            webviewHebrewBooks.triggerBrowserDownload(bookId, title)

            // Note: C# will handle download completion and notify via postMessage
            // The HebrewBooksService will automatically set up the virtual URL
          }

          console.log('[HebrewBooks] Hebrew book preparation completed successfully')
          return { success: true }
        } else {
          console.error('[HebrewBooks] Hebrew book preparation failed')
          return { success: false }
        }
      } catch (error) {
        console.error('[HebrewBooks] Failed to prepare Hebrew book for viewing:', error)
        return { success: false }
      }
    } else {
      console.log('[HebrewBooks] WebView bridge not available, using development mode fallback')
      // Development mode fallback - open in new tab
      const url = `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
      window.open(url, '_blank')
      return { success: true }
    }
  }

  const downloadHebrewBook = async (bookId: string, title: string) => {
    console.log(`[HebrewBooks] Starting downloadHebrewBook - bookId: ${bookId}, title: ${title}`)

    // Import service dynamically to avoid circular dependencies
    const { webviewHebrewBooks } = await import('../services/webviewHebrewBooks')

    // Use unified Hebrew Books service
    if (webviewHebrewBooks.isAvailable()) {
      console.log('[HebrewBooks] WebView bridge available, starting Hebrew book download flow')
      try {
        const result = await webviewHebrewBooks.prepareForDownload(bookId, title)

        if (result.success && !result.cancelled) {
          console.log('[HebrewBooks] Download preparation successful, triggering browser download')
          webviewHebrewBooks.triggerBrowserDownload(bookId, title)

          // Note: C# will handle download capture and save to user's chosen location
          console.log('[HebrewBooks] Hebrew book download initiated successfully')
        } else if (result.cancelled) {
          console.log('[HebrewBooks] Hebrew book download was cancelled by user')
        } else {
          console.error('[HebrewBooks] Hebrew book download preparation failed')
        }
      } catch (error) {
        console.error('[HebrewBooks] Failed to prepare Hebrew book for download:', error)
      }
    } else {
      console.log('[HebrewBooks] WebView bridge not available, using development mode fallback')
      // Development mode fallback - trigger browser download directly
      const url = `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
      const link = document.createElement('a')
      link.href = url
      link.download = `${title}.pdf`
      link.style.display = 'none'
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
    }
  }

  return {
    // State
    books,
    filteredBooks,
    isLoading,
    error,
    searchTerm,
    debouncedSearchTerm,
    currentView,
    selectedBookId,

    // Getters
    hasBooks,
    selectedBook,

    // Actions
    loadBooks,
    trackBookInteraction,
    updateSearchTerm,
    updateDebouncedSearchTerm,
    performDebouncedSearch,
    updateFilteredBooks,
    openBookViewer,
    closeBookViewer,
    openHebrewBookViewer,
    downloadHebrewBook,
  }
})