import { ref, computed } from 'vue'
import { useDebounce } from '@vueuse/core'
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'

// Books where entries span 2 lines (h3 header + content)
const TWO_LINE_BOOKS = new Set([6105])

export interface DictEntry {
  id: number
  bookId: number
  lineId: number
  lineIndex: number
  headword: string
  bookTitle: string
}

export interface DictEntryContent {
  bookId: number
  lineIndex: number
  bookTitle: string
  headword: string
  html: string
}

export function useDictionarySearch() {
  const searchQuery = ref('')
  const debouncedQuery = useDebounce(searchQuery, 250)
  const results = ref<DictEntry[]>([])
  const selectedEntry = ref<DictEntryContent | null>(null)
  const searching = ref(false)
  const hasSearched = ref(false)
  const activeBookId = ref<number | null>(null)

  const filteredResults = computed(() =>
    activeBookId.value === null
      ? results.value
      : results.value.filter((r) => r.bookId === activeBookId.value),
  )

  const bookCounts = computed(() => {
    const map = new Map<number, { title: string; count: number }>()
    for (const r of results.value) {
      if (!map.has(r.bookId)) map.set(r.bookId, { title: r.bookTitle, count: 0 })
      map.get(r.bookId)!.count++
    }
    return map
  })

  async function search(term: string) {
    const trimmed = term.trim()
    if (!trimmed) {
      results.value = []
      selectedEntry.value = null
      hasSearched.value = false
      return
    }
    searching.value = true
    hasSearched.value = true
    try {
      // Params: containsPattern, exactTerm, prefixPattern
      results.value = await query<DictEntry>(SQL.SEARCH_DICTIONARY_ENTRIES, [
        `%${trimmed}%`,
        trimmed,
        `${trimmed}%`,
      ])
      selectedEntry.value = null
      if (
        activeBookId.value !== null &&
        !results.value.some((r) => r.bookId === activeBookId.value)
      ) {
        activeBookId.value = null
      }
    } finally {
      searching.value = false
    }
  }

  async function selectEntry(entry: DictEntry) {
    const isTwoLine = TWO_LINE_BOOKS.has(entry.bookId)
    const endIndex = entry.lineIndex + (isTwoLine ? 2 : 1)
    const lines = await query<{ lineIndex: number; content: string }>(
      SQL.GET_DICTIONARY_ENTRY_LINES,
      [entry.bookId, entry.lineIndex, endIndex],
    )
    selectedEntry.value = {
      bookId: entry.bookId,
      lineIndex: entry.lineIndex,
      bookTitle: entry.bookTitle,
      headword: entry.headword,
      html: lines.map((l) => l.content).join('\n'),
    }
  }

  function clearEntry() {
    selectedEntry.value = null
  }

  return {
    searchQuery,
    debouncedQuery,
    results,
    filteredResults,
    selectedEntry,
    searching,
    hasSearched,
    activeBookId,
    bookCounts,
    search,
    selectEntry,
    clearEntry,
  }
}
