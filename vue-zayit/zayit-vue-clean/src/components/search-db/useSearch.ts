import { ref, computed } from 'vue'
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import { useTabStore } from '@/stores/tabStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { BloomSearchResult } from './searchTypes'
import type { CategoryNode } from '@/components/books-fs/booksFsTree'

export function useSearch(
  results: () => BloomSearchResult[],
  executedQuery: () => string,
  executeSearch: (q: string) => Promise<void>,
  clearSearch: () => void,
) {
  const tabStore = useTabStore()
  const booksStore = useBooksDataStore()

  const searchQuery = ref('')
  const isFilterOpen = ref(false)
  const checkedBookIds = ref<Set<number>>(new Set())
  const initialized = ref(false)

  const filteredResults = computed(() =>
    checkedBookIds.value.size === 0
      ? results()
      : results().filter((r) => checkedBookIds.value.has(r.bookId)),
  )

  const resultCounts = computed(() => {
    const m = new Map<number, number>()
    for (const r of results()) m.set(r.bookId, (m.get(r.bookId) ?? 0) + 1)
    return m
  })

  function initCheckedBooks() {
    if (initialized.value || !booksStore.allBooks.length) return
    checkedBookIds.value = new Set(booksStore.allBooks.map((b) => b.id))
    initialized.value = true
  }

  function toggleBook(bookId: number) {
    const s = new Set(checkedBookIds.value)
    s.has(bookId) ? s.delete(bookId) : s.add(bookId)
    checkedBookIds.value = s
  }

  function allBookIds(cat: CategoryNode): number[] {
    return [...cat.books.map((b) => b.id), ...cat.children.flatMap(allBookIds)]
  }

  function toggleCategory(cat: CategoryNode, checked: boolean) {
    const s = new Set(checkedBookIds.value)
    for (const id of allBookIds(cat)) checked ? s.add(id) : s.delete(id)
    checkedBookIds.value = s
  }

  function checkAll() {
    checkedBookIds.value = new Set(booksStore.allBooks.map((b) => b.id))
  }
  function uncheckAll() {
    checkedBookIds.value = new Set()
  }

  async function handleSearch(q: string) {
    tabStore.updateActiveTab({ title: `חיפוש: ${q}` })
    await executeSearch(q)
  }

  function handleClearSearch() {
    clearSearch()
    searchQuery.value = ''
    tabStore.updateActiveTab({ title: 'חיפוש' })
  }

  async function handleResultClick(result: BloomSearchResult) {
    try {
      const rows = await query<{ lineIndex: number }>(SQL.GET_LINE_INDEX_FROM_LINE_ID, [
        result.lineId,
      ])
      const lineIndex = rows[0]?.lineIndex
      if (lineIndex == null) return
      tabStore.openTab({
        title: result.bookTitle,
        route: '/book-view',
        bookId: result.bookId,
        openTocLineIndex: lineIndex,
        searchHighlightLineIndex: lineIndex,
        searchHighlightQuery: executedQuery(),
      })
    } catch (err) {
      console.error('[useSearch] failed to open result:', err)
    }
  }

  return {
    searchQuery,
    isFilterOpen,
    checkedBookIds,
    filteredResults,
    resultCounts,
    initCheckedBooks,
    toggleBook,
    toggleCategory,
    checkAll,
    uncheckAll,
    handleSearch,
    handleClearSearch,
    handleResultClick,
  }
}
