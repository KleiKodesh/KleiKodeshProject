import { ref, watch } from 'vue'
import { useDebounceFn } from '@vueuse/core'
import { normalize } from '@/utils/normalizeText'
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import { useTabStore } from '@/stores/tabStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { BloomSearchResult } from './searchTypes'
import type { CategoryNode } from '@/components/books-fs/booksCategoryTree'

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
  const filterBookQuery = ref('')
  const checkedBookIds = ref<Set<number>>(new Set())

  // Plain refs — never computeds. Updated only when filter/checkboxes change,
  // not on every streaming batch.
  const effectiveBookIds = ref<Set<number>>(new Set())
  const filteredResults = ref<BloomSearchResult[]>([])
  const resultCounts = ref<Map<number, number>>(new Map())
  const initialized = ref(false)

  // ── Core filter logic ────────────────────────────────────────────────────────

  function matchBookIds(q: string, checked: Set<number>): Set<number> {
    const trimmed = q.trim()
    if (trimmed.length < 2) return checked
    const words = normalize(trimmed).split(/\s+/).filter((w) => w.length > 0)
    if (!words.length) return checked
    const exactWords = words.slice(0, -1)
    const prefixWord = words[words.length - 1]!
    const matching = new Set(
      booksStore.allBooks
        .filter((b) => {
          const pathWords = b.searchWords ?? (b.searchPath ?? '').split(/\s+/)
          const exactOk = exactWords.every((qw) => pathWords.some((pw) => pw === qw))
          const prefixOk = pathWords.some((pw) => pw.includes(prefixWord))
          return exactOk && prefixOk
        })
        .map((b) => b.id),
    )
    const result = new Set<number>()
    for (const id of checked) if (matching.has(id)) result.add(id)
    return result
  }

  function applyFilter(ids: Set<number>) {
    const raw = results()
    const allChecked = filterBookQuery.value.trim().length < 2 && ids.size === booksStore.allBooks.length
    filteredResults.value = allChecked ? raw : raw.filter((r) => ids.has(r.bookId))
    const m = new Map<number, number>()
    for (const r of raw) m.set(r.bookId, (m.get(r.bookId) ?? 0) + 1)
    resultCounts.value = m
  }

  function updateFilter(q: string, checked: Set<number>) {
    const ids = matchBookIds(q, checked)
    effectiveBookIds.value = ids
    applyFilter(ids)
  }

  const runFilterDebounce = useDebounceFn((q: string) => {
    updateFilter(q, checkedBookIds.value)
  }, 200)

  // Query typing → debounce the scan; clear immediately if < 2 chars
  watch(filterBookQuery, (q) => {
    if (q.trim().length < 2) updateFilter(q, checkedBookIds.value)
    else runFilterDebounce(q)
  })

  // Checkbox change → immediate (no typing, no debounce needed)
  watch(checkedBookIds, (checked) => updateFilter(filterBookQuery.value, checked))

  // Results streaming → re-apply current filter to new results
  // ids are already known — just a Set lookup, no book scan
  watch(() => results(), () => applyFilter(effectiveBookIds.value))

  // ── Public mutations ─────────────────────────────────────────────────────────

  function initCheckedBooks() {
    if (initialized.value || !booksStore.allBooks.length) return
    const all = new Set(booksStore.allBooks.map((b) => b.id))
    checkedBookIds.value = all
    effectiveBookIds.value = all
    initialized.value = true
  }

  function setCheckedBookIds(ids: Set<number>) {
    checkedBookIds.value = ids
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
      const q = executedQuery()
      tabStore.openTab({
        title: result.bookTitle,
        route: '/book-view',
        bookId: result.bookId,
        openTocLineIndex: lineIndex,
        searchHighlightLineIndex: lineIndex,
        searchHighlightQuery: q,
        searchHighlightSnippet: result.snippet,
        searchHighlightTerms: q.trim().split(/\s+/).filter(Boolean),
      })
    } catch (err) {
      console.error('[useSearch] failed to open result:', err)
    }
  }

  return {
    searchQuery,
    isFilterOpen,
    filterBookQuery,
    checkedBookIds,
    filteredResults,
    resultCounts,
    initCheckedBooks,
    setCheckedBookIds,
    toggleBook,
    toggleCategory,
    checkAll,
    uncheckAll,
    handleSearch,
    handleClearSearch,
    handleResultClick,
  }
}
