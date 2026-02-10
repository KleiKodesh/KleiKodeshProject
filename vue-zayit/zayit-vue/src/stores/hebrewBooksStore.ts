import { defineStore } from 'pinia'
import { ref, computed, nextTick } from 'vue'
import type { HebrewBook } from '../types/HebrewBook'
import { CsvLoader } from '../services/hebrewBooksCsvLoader'
import { PopularityManager } from '../services/hebrewBooksPopularityManager'
import { hebrewBooksSearchService } from '../services/hebrewBooksSearchService'
import { useTabStore } from './tabStore'
import { webviewHebrewBooks } from '../services/webviewHebrewBooks'

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
    return books.value.find(book => book.id === selectedBookId.value) || null
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
    filteredBooks.value = await hebrewBooksSearchService.getFilteredBooks(books.value, debouncedSearchTerm.value)
  }

  // Create debounced search function once globally
  if (!globalDebouncedSearch) {
    globalDebouncedSearch = hebrewBooksSearchService.createDebouncedSearch(async (value: string) => {
      debouncedSearchTerm.value = value
      await updateFilteredBooks()
    }, 150)
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

  // Flow 1: Open Hebrew Book for viewing (may cache if needed, no SaveAs dialog)
  const openHebrewBookViewer = async (bookId: string, title: string) => {
    console.log(`[HebrewBooks] Starting openHebrewBookViewer - bookId: ${bookId}, title: ${title}`)

    // Check WebView availability first - don't create tab if it won't work
    if (!webviewHebrewBooks.isAvailable()) {
      console.error('[HebrewBooks] WebView bridge not available - Hebrew Books viewing requires C# host')
      return { success: false }
    }

    // WebView is available, proceed with tab creation and viewing flow
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

    // Use C# to prepare book for viewing (will cache if needed)
    console.log('[HebrewBooks] Preparing Hebrew book for viewing through C#')
    try {
      const result = await webviewHebrewBooks.prepareForViewing(bookId, title)

      if (result.success) {
        if (result.cached && result.url) {
          // File was already cached, set PDF state immediately
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
          // File not cached, trigger browser download that C# will capture
          console.log('[HebrewBooks] File not cached, triggering browser download for viewing')
          const downloadUrl = `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
          console.log('[HebrewBooks] Creating browser download trigger for viewing:', downloadUrl)

          // Create a hidden link and trigger download - C# will capture this
          const link = document.createElement('a')
          link.href = downloadUrl
          link.style.display = 'none'
          document.body.appendChild(link)
          link.click()
          document.body.removeChild(link)

          console.log('[HebrewBooks] Browser download triggered for viewing - C# will capture and cache')
          // Note: C# will capture the download, cache it, and notify Vue when ready
          // The HebrewBooksService will automatically set up the virtual URL and call handleHebrewBookViewingReady
        }

        console.log('[HebrewBooks] Hebrew book viewing preparation completed successfully')
        return { success: true }
      } else {
        console.error('[HebrewBooks] Hebrew book viewing preparation failed')
        return { success: false }
      }
    } catch (error) {
      console.error('[HebrewBooks] Failed to prepare Hebrew book for viewing:', error)
      return { success: false }
    }
  }

  // Flow 2: Download Hebrew Book with SaveAs dialog (user chooses location)
  const downloadHebrewBook = async (bookId: string, title: string) => {
    console.log(`[HebrewBooks] Starting downloadHebrewBook - bookId: ${bookId}, title: ${title}`)

    // Hebrew Books download only works through C# WebView
    if (!webviewHebrewBooks.isAvailable()) {
      console.error('[HebrewBooks] WebView bridge not available - Hebrew Books download requires C# host')
      return
    }

    console.log('[HebrewBooks] Starting Hebrew book download with SaveAs dialog through C#')
    try {
      // Use C# SaveAs dialog and handle the entire download process in C#
      const result = await webviewHebrewBooks.prepareForDownload(bookId, title)

      if (result.success && !result.cancelled) {
        // Check if file was already cached
        if (result.filePath) {
          // File was already cached and copied to user location
          console.log('[HebrewBooks] Hebrew book download completed successfully (from cache)')
          console.log('[HebrewBooks] File saved to:', result.filePath)
        } else {
          // File not cached, trigger browser download that C# will capture
          console.log('[HebrewBooks] File not cached, triggering browser download for SaveAs')
          const downloadUrl = `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
          console.log('[HebrewBooks] Creating browser download trigger for SaveAs:', downloadUrl)

          // Create a hidden link and trigger download - C# will capture this
          const link = document.createElement('a')
          link.href = downloadUrl
          link.style.display = 'none'
          document.body.appendChild(link)
          link.click()
          document.body.removeChild(link)

          console.log('[HebrewBooks] Browser download triggered for SaveAs - C# will capture and copy to user location')
          // Note: C# will capture the download, copy to user's chosen location, and notify Vue when complete
          // The HebrewBooksService will call handleHebrewBookDownloadComplete when done
        }
      } else if (result.cancelled) {
        console.log('[HebrewBooks] Hebrew book download was cancelled by user')
      } else {
        console.error('[HebrewBooks] Hebrew book download failed')
      }
    } catch (error) {
      console.error('[HebrewBooks] Failed to download Hebrew book:', error)
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