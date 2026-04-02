import { ref, computed } from 'vue'
import { hebrewBooksHistory } from './hebrewBooksHistory'
import { loadHbCatalog, searchHbCatalog, getHbPdfUrl, type HebrewBook } from './hebrewBooksCatalog'
import { usePdfStore } from '@/stores/pdfStore'
import { useTabStore } from '@/stores/tabStore'
import { isHosted } from '@/host/db'

export function useHebrewBooks() {
  const pdfStore = usePdfStore()

  // Catalog lives here — freed automatically when the component unmounts
  const catalog = ref<HebrewBook[]>([])
  const books = ref<HebrewBook[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const searchTerm = ref('')
  const isOnline = ref(navigator.onLine)

  async function load() {
    isLoading.value = true
    error.value = null
    try {
      const [history, loadedCatalog] = await Promise.all([
        hebrewBooksHistory.getHistory(),
        loadHbCatalog(),
      ])
      catalog.value = loadedCatalog
      books.value = history
    } catch {
      error.value = 'שגיאה בטעינת הספרים'
    } finally {
      isLoading.value = false
    }
  }

  function search(term: string) {
    searchTerm.value = term
    if (!term.trim()) {
      hebrewBooksHistory.getHistory().then((h) => {
        books.value = h
      })
    } else {
      books.value = searchHbCatalog(catalog.value, term)
    }
  }

  async function trackAccess(book: HebrewBook) {
    await hebrewBooksHistory.trackAccess(book)
  }

  function openBook(book: HebrewBook) {
    if (!isHosted) {
      const input = Object.assign(document.createElement('input'), { type: 'file', accept: '.pdf' })
      input.onchange = () => {
        const file = input.files?.[0]
        if (!file) return
        trackAccess(book)
        const tabId = useTabStore().activeTabId
        pdfStore.finishLocalFileConversion(tabId, {
          url: URL.createObjectURL(file),
          fileName: file.name,
          filePath: '',
        })
      }
      input.click()
      return
    }
    trackAccess(book)
    const tabId = useTabStore().activeTabId
    pdfStore.startHbDownload(book.title, tabId)
    window.__webviewAction?.('triggerHbDownload', {
      bookId: book.id,
      bookTitle: book.title,
      url: getHbPdfUrl(book.id),
      tabId,
    })
  }

  function downloadBook(book: HebrewBook) {
    if (!isHosted) return
    window.__webviewAction?.('triggerHbSaveAs', {
      bookId: book.id,
      bookTitle: book.title,
      url: getHbPdfUrl(book.id),
    })
  }

  const displayedBooks = computed(() => {
    if (isOnline.value || searchTerm.value) return books.value
    return books.value.slice(0, 10)
  })

  return {
    displayedBooks,
    isLoading,
    error,
    searchTerm,
    isOnline,
    load,
    search,
    trackAccess,
    openBook,
    downloadBook,
  }
}
