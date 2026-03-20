import { ref, computed } from 'vue'
import { hebrewBooksService, type HebrewBook } from './hebrewBooksService'

const books = ref<HebrewBook[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const searchTerm = ref('')
const isOnline = ref(navigator.onLine)

export function useHebrewBooks() {
  async function load() {
    isLoading.value = true
    error.value = null
    try {
      books.value = await hebrewBooksService.getHistory()
      hebrewBooksService.loadCatalog() // background
    } catch (e) {
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

  const displayedBooks = computed(() => {
    if (isOnline.value || searchTerm.value) return books.value
    return books.value.slice(0, 10)
  })

  return { displayedBooks, isLoading, error, searchTerm, isOnline, load, search, trackAccess }
}
