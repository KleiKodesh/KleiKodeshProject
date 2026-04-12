import { ref, computed, onMounted } from 'vue'
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import { useTabStore } from '@/stores/tabStore'

export interface DictionaryBook {
  id: number
  title: string
  totalLines: number
  categoryId: number
  categoryTitle: string
  authors: string | null
}

export interface DictionarySection {
  title: string
  books: DictionaryBook[]
}

export function useDictionary() {
  const tabStore = useTabStore()
  const books = ref<DictionaryBook[]>([])
  const loading = ref(true)

  // Group books by sub-category title, preserving DB order
  const sections = computed<DictionarySection[]>(() => {
    const map = new Map<string, DictionaryBook[]>()
    for (const book of books.value) {
      const key = book.categoryTitle
      if (!map.has(key)) map.set(key, [])
      map.get(key)!.push(book)
    }
    return Array.from(map.entries()).map(([title, bks]) => ({ title, books: bks }))
  })

  async function load() {
    loading.value = true
    try {
      books.value = await query<DictionaryBook>(SQL.GET_DICTIONARY_BOOKS)
    } finally {
      loading.value = false
    }
  }

  function openBook(book: DictionaryBook) {
    tabStore.updateActiveTab({
      route: '/book-view',
      title: book.title,
      bookId: book.id,
    })
  }

  onMounted(load)

  return { sections, loading, openBook }
}
