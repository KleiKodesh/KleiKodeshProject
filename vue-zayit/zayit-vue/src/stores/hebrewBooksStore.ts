import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { HebrewBook } from '../types/HebrewBook'
import { hebrewBooksService } from '../services/hebrewBooksService'
import { useTabStore } from './tabStore'
import { webviewHebrewBooks } from '../services/webviewHebrewBooks'

export const useHebrewBooksStore = defineStore('hebrewBooks', () => {
  // State
  const filteredBooks = ref<HebrewBook[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const searchTerm = ref('')

  // Navigation state
  const currentView = ref<'list' | 'viewer'>('list')
  const selectedBookId = ref<string | null>(null)

  // Getters
  const hasBooks = computed(() => filteredBooks.value.length > 0)
  const selectedBook = computed(() => {
    if (!selectedBookId.value) return null
    return filteredBooks.value.find(book => book.id === selectedBookId.value) || null
  })

  // Actions
  const loadBooks = async () => {
    if (filteredBooks.value.length > 0) return

    isLoading.value = true
    error.value = null

    try {
      // Load history (fast)
      filteredBooks.value = await hebrewBooksService.getHistory()

      // Start loading catalog in background
      hebrewBooksService.loadCatalog()
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load books'
      console.error('Error loading books:', err)
    } finally {
      isLoading.value = false
    }
  }

  const performSearch = (term: string) => {
    searchTerm.value = term

    if (!term || term.trim() === '') {
      // No search - show history
      hebrewBooksService.getHistory().then(history => {
        filteredBooks.value = history
      })
    } else {
      // Search
      const results = hebrewBooksService.search(term)
      filteredBooks.value = results
    }
  }

  const trackBookInteraction = async (bookId: string) => {
    const book = filteredBooks.value.find(b => b.id === bookId)
    if (!book) return

    await hebrewBooksService.trackAccess(bookId, book)
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
    if (!webviewHebrewBooks.isAvailable()) {
      console.error('[HebrewBooks] WebView bridge not available')
      return { success: false }
    }

    const tabStore = useTabStore()
    tabStore.setPage('hebrewbooks-view')

    const tab = tabStore.activeTab
    if (tab) {
      tab.title = title
    }

    try {
      const result = await webviewHebrewBooks.prepareForViewing(bookId, title)

      if (result.success) {
        if (result.cached && result.url) {
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
          const downloadUrl = `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
          const link = document.createElement('a')
          link.href = downloadUrl
          link.style.display = 'none'
          document.body.appendChild(link)
          link.click()
          document.body.removeChild(link)
        }
        return { success: true }
      }
      return { success: false }
    } catch (error) {
      console.error('[HebrewBooks] Failed to prepare Hebrew book:', error)
      return { success: false }
    }
  }

  const downloadHebrewBook = async (bookId: string, title: string) => {
    if (!webviewHebrewBooks.isAvailable()) {
      console.error('[HebrewBooks] WebView bridge not available')
      return
    }

    try {
      const result = await webviewHebrewBooks.prepareForDownload(bookId, title)

      if (result.success && !result.cancelled && !result.filePath) {
        const downloadUrl = `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
        const link = document.createElement('a')
        link.href = downloadUrl
        link.style.display = 'none'
        document.body.appendChild(link)
        link.click()
        document.body.removeChild(link)
      }
    } catch (error) {
      console.error('[HebrewBooks] Failed to download Hebrew book:', error)
    }
  }

  return {
    // State
    filteredBooks,
    isLoading,
    error,
    searchTerm,
    currentView,
    selectedBookId,

    // Getters
    hasBooks,
    selectedBook,

    // Actions
    loadBooks,
    performSearch,
    trackBookInteraction,
    openBookViewer,
    closeBookViewer,
    openHebrewBookViewer,
    downloadHebrewBook,
  }
})
