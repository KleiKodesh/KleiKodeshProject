import { ref, computed } from 'vue'
import { useDebounce } from '@vueuse/core'
import { query, queryDict } from '@/host/db'
import { SQL } from '@/host/queries.sql'

const SOURCE_BOOK_TITLES: Record<number, string> = {
  10: 'ספר הערוך',
  11: 'הפלאה שבערכין',
  12: 'ספר השרשים לרד"ק',
  13: 'אוצר לעזי רש"י',
  14: 'מחברת מנחם',
  15: 'דבש לפי',
  16: 'מדבר קדמות',
  17: 'עין זוכר',
  20: 'מצודת ציון',
  23: 'מלבים באור המילות',
  25: 'ויקימילון',
}

const TWO_LINE_BOOKS = new Set([6105])

export interface DictEntry {
  id: number
  bookId: number | null
  lineIndex: number | null
  headword: string
  nikud: string | null
  definition: string
  type: string
  source: number
  bookTitle: string
  /** 0 = exact, 1 = prefix, 2 = root, 3 = contains */
  matchTier: number
}

export interface DictEntryContent {
  bookId: number | null
  lineIndex: number | null
  bookTitle: string
  headword: string
  nikud: string | null
  type: string
  html: string
}

function resolveBookTitle(source: number, type: string): string {
  if (SOURCE_BOOK_TITLES[source]) return SOURCE_BOOK_TITLES[source]
  if (type === 'abbrev') return 'ראשי תיבות'
  return 'מילון ארמי'
}

function levenshtein(a: string, b: string): number {
  const m = a.length,
    n = b.length
  const dp: number[] = Array.from({ length: n + 1 }, (_, i) => i)
  for (let i = 1; i <= m; i++) {
    let prev = dp[0]
    dp[0] = i
    for (let j = 1; j <= n; j++) {
      const temp = dp[j]
      dp[j] = a[i - 1] === b[j - 1] ? prev : 1 + Math.min(prev, dp[j], dp[j - 1])
      prev = temp
    }
  }
  return dp[n]
}

export function useDictionarySearch() {
  const searchQuery = ref('')
  const debouncedQuery = useDebounce(searchQuery, 250)
  const results = ref<DictEntry[]>([])
  const selectedEntry = ref<DictEntryContent | null>(null)
  // Map of entryId → content (null = loading)
  const expandedEntries = ref<Map<number, DictEntryContent | null>>(new Map())
  const searching = ref(false)
  const hasSearched = ref(false)
  const activeSource = ref<Set<string>>(new Set())

  const filteredResults = computed(() =>
    activeSource.value.size === 0
      ? results.value
      : results.value.filter((r) => activeSource.value.has(r.bookTitle)),
  )

  const bookCounts = computed(() => {
    const map = new Map<string, { count: number }>()
    for (const r of results.value) {
      if (!map.has(r.bookTitle)) map.set(r.bookTitle, { count: 0 })
      map.get(r.bookTitle)!.count++
    }
    return new Map([...map.entries()].sort(([a], [b]) => a.localeCompare(b, 'he')))
  })

  async function fetchAndExpand(entry: DictEntry) {
    // Mark as loading
    expandedEntries.value = new Map(expandedEntries.value).set(entry.id, null)

    let content: DictEntryContent
    if (entry.source < 10 || entry.bookId === null || entry.lineIndex === null) {
      content = {
        bookId: null,
        lineIndex: null,
        bookTitle: entry.bookTitle,
        headword: entry.headword,
        nikud: entry.nikud,
        type: entry.type,
        html: entry.definition,
      }
    } else {
      const isTwoLine = TWO_LINE_BOOKS.has(entry.bookId)
      const endIndex = entry.lineIndex + (isTwoLine ? 2 : 1)
      const lines = await query<{ lineIndex: number; content: string }>(
        SQL.GET_DICTIONARY_ENTRY_LINES,
        [entry.bookId, entry.lineIndex, endIndex],
      )
      content = {
        bookId: entry.bookId,
        lineIndex: entry.lineIndex,
        bookTitle: entry.bookTitle,
        headword: entry.headword,
        nikud: entry.nikud,
        type: entry.type,
        html: lines
          .map((l) => l.content)
          .join('\n')
          .replace(/^<h3>[^<]*<\/h3>\n?/, ''),
      }
    }
    // Only apply if still expanded (user may have collapsed while loading)
    if (expandedEntries.value.has(entry.id)) {
      expandedEntries.value = new Map(expandedEntries.value).set(entry.id, content)
    }
  }

  async function search(term: string) {
    const trimmed = term.trim()
    if (!trimmed) {
      results.value = []
      hasSearched.value = false
      expandedEntries.value = new Map()
      return
    }
    searching.value = true
    hasSearched.value = true
    expandedEntries.value = new Map()
    try {
      type RawRow = Omit<DictEntry, 'bookTitle'>
      const rows = await queryDict<RawRow>(SQL.SEARCH_DICTIONARY_ENTRIES, [
        trimmed, // matchTier exact
        `${trimmed}%`, // matchTier prefix
        trimmed, // matchTier root
        `%${trimmed}%`, // WHERE contains
        trimmed, // WHERE root
        trimmed, // ORDER BY root length
      ])
      const mapped = rows.map((r) => ({
        ...r,
        bookTitle: resolveBookTitle(r.source, r.type),
      }))
      // SQL already orders by matchTier; stable-sort within same headword by source
      mapped.sort((a, b) => {
        if (a.matchTier !== b.matchTier) return a.matchTier - b.matchTier
        if (a.headword !== b.headword) return a.headword.localeCompare(b.headword, 'he')
        return a.source - b.source
      })
      results.value = mapped
      if (activeSource.value.size > 0) {
        const validTitles = new Set(mapped.map((r) => r.bookTitle))
        activeSource.value = new Set([...activeSource.value].filter((s) => validTitles.has(s)))
      }
      // Auto-expand only exact matches (tier 0) — inflections are shown as clickable links
      if (mapped.length > 0)
        mapped.filter((e) => e.matchTier === 0).forEach((e) => fetchAndExpand(e))
    } finally {
      searching.value = false
    }
  }

  function toggleSource(src: string) {
    const next = new Set(activeSource.value)
    if (next.has(src)) next.delete(src)
    else next.add(src)
    activeSource.value = next
  }

  function clearSources() {
    activeSource.value = new Set()
  }

  function toggleEntry(entry: DictEntry) {
    if (expandedEntries.value.has(entry.id)) {
      const next = new Map(expandedEntries.value)
      next.delete(entry.id)
      expandedEntries.value = next
    } else {
      fetchAndExpand(entry)
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
    expandedEntries,
    searching,
    hasSearched,
    activeSource,
    bookCounts,
    lastTerm: computed(() => searchQuery.value.trim()),
    search,
    toggleEntry,
    toggleSource,
    clearSources,
    clearEntry,
  }
}
