import { ref, computed } from 'vue'
import { hebrewBooksService, type HebrewBook } from './hebrewBooksService'
import { usePdfStore } from '@/stores/pdfStore'
import { useTabStore } from '@/stores/tabStore'
import { isHosted } from '@/host/db'

const books = ref<HebrewBook[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const searchTerm = ref('')
const isOnline = ref(navigator.onLine)

export function useHebrewBooks() {
  const pdfStore = usePdfStore()

  async function load() {
    isLoading.value = true
    error.value = null
    try {
      books.value = await hebrewBooksService.getHistory()
      hebrewBooksService.loadCatalog() // background
    } catch {
      error.value = 'שגיאה בטעינת הספרים'
    } finally {
      isLoading.value = false
    }
  }

  function search(term: string) {
    searchTerm.value = term
    if (!term.trim()) {
      hebrewBooksService.getHistory().then(h => { books.value = h })
    } else {
      books.value = hebrewBooksService.search(term)
    }
  }

  async function trackAccess(book: HebrewBook) {
    await hebrewBooksService.trackAccess(book)
  }

  /**
   * Open a book in the PDF viewer.
   * Triggers the hebrewbooks.org download URL in the WebView2 engine.
   * C# intercepts the download, saves to cache, then sends a push event with the URL.
   */
  function openBook(book: HebrewBook) {
    if (!isHosted) return
    trackAccess(book)
    // Navigate to pdf-view placeholder immediately
    const tabId = useTabStore().activeTabId
    pdfStore.startHbDownload(book.title, tabId)
    window.__webviewAction?.('triggerHbDownload', {
      bookId: book.id,
      bookTitle: book.title,
      url: hebrewBooksService.getPdfUrl(book.id),
      tabId,
    })
  }

  /**
   * Download a book to a user-chosen location (Save As).
   * Triggers the download URL — C# intercepts and shows a Save As dialog.
   */
  function downloadBook(book: HebrewBook) {
    if (!isHosted) return
    window.__webviewAction?.('triggerHbSaveAs', {
      bookId: book.id,
      bookTitle: book.title,
      url: hebrewBooksService.getPdfUrl(book.id),
    })
  }

  const displayedBooks = computed(() => {
    if (isOnline.value || searchTerm.value) return books.value
    return books.value.slice(0, 10)
  })

  return {
    displayedBooks, isLoading, error, searchTerm, isOnline,
    load, search, trackAccess, openBook, downloadBook,
  }
}
