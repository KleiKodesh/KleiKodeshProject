import { ref, computed } from 'vue'
import { useDebounceFn } from '@vueuse/core'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { loadHbCatalog, searchHbCatalog, getHbPdfUrl, type HebrewBook } from './hebrewBooksCatalog'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useTabStore } from '@/stores/tabStore'
import { isHosted } from '@/webview-host/seforimDb'
import { triggerHbDownload, triggerHbSaveAs } from '@/webview-host/bridge'

export function useHebrewBooks() {
  const localFileStore = useLocalFileStore()
  const history = useHebrewBooksHistoryStore()

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
      const [historyBooks, loadedCatalog] = await Promise.all([
        history.getHistory(),
        loadHbCatalog(),
      ])
      catalog.value = loadedCatalog
      books.value = historyBooks
    } catch {
      error.value = 'שגיאה בטעינת הספרים'
    } finally {
      isLoading.value = false
    }
  }

  const runSearch = useDebounceFn(async (term: string) => {
    if (!term.trim()) {
      history.getHistory().then((h) => {
        books.value = h
      })
    } else {
      const results = await searchHbCatalog(catalog.value, term)
      books.value = results
    }
  }, 200)

  function search(term: string) {
    searchTerm.value = term
    runSearch(term)
  }

  async function trackAccess(book: HebrewBook) {
    await history.trackAccess(book)
  }

  function openBook(book: HebrewBook) {
    if (!isHosted) {
      const input = Object.assign(document.createElement('input'), { type: 'file', accept: '.pdf' })
      input.onchange = () => {
        const file = input.files?.[0]
        if (!file) return
        trackAccess(book)
        const tabId = useTabStore().activeTabId
        localFileStore.finishLocalFileConversion(tabId, {
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
    localFileStore.startHbDownload(book.title, tabId)
    triggerHbDownload(String(book.id), book.title, getHbPdfUrl(book.id), tabId).catch(() => {})
  }

  function downloadBook(book: HebrewBook) {
    if (!isHosted) return
    triggerHbSaveAs(String(book.id), book.title, getHbPdfUrl(book.id)).catch(() => {})
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
