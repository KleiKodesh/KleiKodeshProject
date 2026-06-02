import { ref, computed } from 'vue'
import { useDebounceFn } from '@vueuse/core'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { searchHbCatalog, getHbPdfUrl, type HebrewBook } from './hebrewBooksCatalog'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useTabStore } from '@/stores/tabStore'
import { triggerHbDownload, triggerHbSaveAs } from '@/webview-host/bridge'

export function useHebrewBooks() {
  const localFileStore = useLocalFileStore()
  const history = useHebrewBooksHistoryStore()

  const books = ref<HebrewBook[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const searchTerm = ref('')
  const isOnline = ref(navigator.onLine)

  async function load() {
    isLoading.value = true
    error.value = null
    try {
      books.value = await history.getHistory()
    } catch {
      error.value = 'שגיאה בטעינת הספרים'
    } finally {
      isLoading.value = false
    }
  }

  const runSearch = useDebounceFn(async (term: string) => {
    if (!term.trim()) {
      books.value = await history.getHistory()
    } else {
      books.value = await searchHbCatalog(term)
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
    trackAccess(book)
    const tabId = useTabStore().activeTabId
    localFileStore.startHbDownload(book.title, tabId)
    triggerHbDownload(String(book.id), book.title, getHbPdfUrl(book.id), tabId).catch(() => {})
  }

  function downloadBook(book: HebrewBook) {
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
