import { ref, watch } from 'vue'
import { query } from '@/host/seforimDb'
import { SQL } from '@/host/queries.sql'
import { normalize } from '@/utils/normalizeText'
import { useTabStore } from '@/stores/tabStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { ensureBookSearchMetadata } from '@/utils/booksCategoryTree'
import type { BloomSearchResult } from './searchTypes'
import type { CategoryNode } from '@/utils/booksCategoryTree'

// ── Query parsing ─────────────────────────────────────────────────────────────

/**
 * Splits a raw search input on `@` separators.
 * "some words @ בראשית @ בבלי" → { term: "some words", atFilters: ["בראשית", "בבלי"] }
 * Whitespace around `@` and around each token is trimmed.
 */
export function parseSearchQuery(raw: string): { term: string; atFilters: string[] } {
  const parts = raw.split('@').map((p) => p.trim())
  const term = parts[0] ?? ''
  const atFilters = parts.slice(1).filter((p) => p.length > 0)
  return { term, atFilters }
}

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
  // @ tokens — shared between the main search bar and the filter panel search input.
  // Each token is matched against the full book catalog; results are unioned,
  // then intersected with checkedBookIds to produce effectiveBookIds.
  const atFilters = ref<string[]>([])

  // Plain refs — never computeds. Updated only when filter/checkboxes change,
  // not on every streaming batch.
  const effectiveBookIds = ref<Set<number>>(new Set())
  const filteredResults = ref<BloomSearchResult[]>([])
  const resultCounts = ref<Map<number, number>>(new Map())
  const initialized = ref(false)

  // ── Core filter logic ────────────────────────────────────────────────────────

  /**
   * Returns the subset of `checked` whose books match the query `q`.
   * If q is too short to be meaningful, returns `checked` unchanged.
   */
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
          ensureBookSearchMetadata(b)
          const pathWords = b.searchWords ?? []
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

  /**
   * Computes the union of books matching any of the @ tokens, searched against
   * the full book catalog. Returns null when there are no tokens (no restriction).
   */
  function atFilterIds(tokens: string[]): Set<number> | null {
    if (!tokens.length) return null
    const allIds = new Set(booksStore.allBooks.map((b) => b.id))
    const union = new Set<number>()
    for (const token of tokens) {
      for (const id of matchBookIds(token, allIds)) union.add(id)
    }
    return union
  }

  /**
   * Computes the final effective book ID set:
   * 1. Start with checkedBookIds (user's manual checkbox selection)
   * 2. Intersect with the union of all @ token matches (if any tokens present)
   */
  function computeEffectiveIds(checked: Set<number>, tokens: string[]): Set<number> {
    const atIds = atFilterIds(tokens)
    if (!atIds) return checked
    const result = new Set<number>()
    for (const id of checked) if (atIds.has(id)) result.add(id)
    return result
  }

  function applyFilter(ids: Set<number>) {
    const raw = results()
    const isEffectivelyAll = atFilters.value.length === 0 && ids.size === booksStore.allBooks.length
    filteredResults.value = isEffectivelyAll ? raw : raw.filter((r) => ids.has(r.bookId))
    const m = new Map<number, number>()
    for (const r of raw) m.set(r.bookId, (m.get(r.bookId) ?? 0) + 1)
    resultCounts.value = m
  }

  function updateFilter(checked: Set<number>, tokens: string[]) {
    const ids = computeEffectiveIds(checked, tokens)
    effectiveBookIds.value = ids
    applyFilter(ids)
  }

  // Checkbox change → immediate
  watch(checkedBookIds, (checked) => updateFilter(checked, atFilters.value))

  // @ tokens change → immediate
  watch(atFilters, (tokens) => updateFilter(checkedBookIds.value, tokens))

  // Results streaming → re-apply current filter (ids already known, just a Set lookup)
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
    effectiveBookIds.value = computeEffectiveIds(ids, atFilters.value)
    initialized.value = true
  }

  function setAtFilters(tokens: string[]) {
    atFilters.value = tokens
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
    atFilters.value = []
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
    checkedBookIds,
    atFilters,
    filteredResults,
    resultCounts,
    initCheckedBooks,
    setCheckedBookIds,
    setAtFilters,
    toggleBook,
    toggleCategory,
    checkAll,
    uncheckAll,
    handleSearch,
    handleClearSearch,
    handleResultClick,
  }
}
