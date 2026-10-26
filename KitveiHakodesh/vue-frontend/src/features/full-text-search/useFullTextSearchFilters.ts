import { t } from '@/i18n'
import { ref, shallowRef, watch } from 'vue'
import { getLineIndexFromLineId } from '@/webview-host/seforimApi'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useTabStore } from '@/stores/tabStore'
import { searchBookTitleIds } from '../book-catalog/bookTitleSearch'
import type { FullTextSearchResult } from './fullTextSearchTypes'
import { snippetHtmlOf } from './fullTextSearchSnippets'
import { bookTitleOf } from './fullTextSearchReferences'
import type { CategoryNode } from '../book-catalog/bookCatalogTree'

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

export function useFullTextSearchFilters(
  results: () => FullTextSearchResult[],
  executedQuery: () => string,
  /** The hit's concrete matched terms (useFullTextSearch.matchedTermsOf). */
  matchedTermsOf: (result: FullTextSearchResult) => string[],
  executeSearch: (q: string) => Promise<void>,
  clearSearch: () => void,
) {
  const paneNavigation = usePaneNavigation()
  const booksStore = useBooksDataStore()
  const tabStore = useTabStore()

  const searchQuery = ref('')
  const isFilterOpen = ref(false)
  const checkedBookIds = ref<Set<number>>(new Set())
  // @ tokens — shared between the main search bar and the filter panel search input.
  // Each token is matched against the full book catalog; results are unioned,
  // then intersected with checkedBookIds to produce effectiveBookIds.
  const atFilters = ref<string[]>([])

  // Plain refs — never computeds. filteredResults is a shallowRef grown incrementally
  // (see applyFilter): each streaming flush appends only the freshly-arrived tail rather
  // than re-filtering the whole accumulated set.
  const effectiveBookIds = ref<Set<number>>(new Set())
  const filteredResults = shallowRef<FullTextSearchResult[]>([])
  /**
   * Bumped every time filteredResults is REBUILT rather than appended to — i.e.
   * whenever a given row index comes to address a different result.
   *
   * Streaming appends leave it alone, because appending never re-addresses the rows
   * already in the list. Consumers that hold a row INDEX across time need this to
   * know when to drop it: the results list pins the rows holding a text selection by
   * index, and a filter change that re-indexes the list underneath those pins would
   * otherwise leave them silently pointing at unrelated search hits.
   */
  const filteredGeneration = ref(0)
  const resultCounts = ref<Map<number, number>>(new Map())
  const initialized = ref(false)

  // How many entries of the raw `results` array have already been folded into
  // `filteredResults`. Streaming only ever APPENDS to `results`, so on each flush we
  // filter just the new tail (results.slice(filteredThrough)) and append — O(batch),
  // not O(total). A criteria change (checkboxes / @ tokens) or a non-append mutation
  // (sort finalize reorders the whole array, cache restore replaces it) resets this to
  // 0 and forces a single full rebuild. This is what keeps a high-frequency word from
  // re-filtering hundreds of thousands of rows on every flush → O(n²) → freeze.
  let filteredThrough = 0

  // ── Core filter logic ────────────────────────────────────────────────────────

  // The union of books matching any @ token, searched against the full catalog by the
  // catalog index's title search (async). null = no tokens, no restriction. Resolved in the
  // atFilters watch below; until a new set resolves, the previous one keeps filtering.
  let atFilterBookIds: Set<number> | null = null
  // Bumped on every atFilters change, BEFORE any early return, so a slower earlier
  // resolution can never overwrite a newer one.
  let atFilterGeneration = 0

  /** Book ids matching one @ token, or null when stale / the index cannot answer. */
  async function matchBookIds(q: string, isStale: () => boolean): Promise<number[] | null> {
    const trimmed = q.trim()
    if (trimmed.length < 2) return booksStore.allBooks.map((b) => b.id)
    return searchBookTitleIds(trimmed, isStale)
  }

  /** Union of the books matching any of the @ tokens; null when stale. */
  async function resolveAtFilterIds(tokens: string[], isStale: () => boolean): Promise<Set<number> | null> {
    const perToken = await Promise.all(tokens.map((token) => matchBookIds(token, isStale)))
    if (isStale()) return null
    const union = new Set<number>()
    for (const ids of perToken) for (const id of ids ?? []) union.add(id)
    return union
  }

  /**
   * Computes the final effective book ID set:
   * 1. Start with checkedBookIds (user's manual checkbox selection)
   * 2. Intersect with the union of all @ token matches (if any tokens present)
   */
  function computeEffectiveIds(checked: Set<number>): Set<number> {
    const atIds = atFilterBookIds
    if (!atIds) return checked
    const result = new Set<number>()
    for (const id of checked) if (atIds.has(id)) result.add(id)
    return result
  }

  function rebuildResultCounts(raw: FullTextSearchResult[]) {
    const m = new Map<number, number>()
    for (const r of raw) m.set(r.bookId, (m.get(r.bookId) ?? 0) + 1)
    resultCounts.value = m
  }

  // Fold raw results into filteredResults. Two modes:
  //   • rebuild (criteria changed, or the raw array was reordered/replaced): filter the
  //     whole set from scratch and assign a fresh array.
  //   • append (streaming growth — the common hot path): filter only the newly-arrived
  //     tail and extend, so a flush costs O(batch) regardless of how large the set is.
  // `filteredThrough` records how far into `results` we've already folded.
  function applyFilter(ids: Set<number>, mode: 'rebuild' | 'append') {
    const raw = results()
    const isEffectivelyAll = atFilters.value.length === 0 && ids.size === booksStore.allBooks.length
    const keep = (r: FullTextSearchResult) => isEffectivelyAll || ids.has(r.bookId)

    if (mode === 'append' && raw.length >= filteredThrough) {
      const tail = raw.slice(filteredThrough)
      const kept = isEffectivelyAll ? tail : tail.filter(keep)
      // New array reference is required to trigger the shallowRef (and the downstream
      // virtualizer measurement rebuild); reuse the existing entries, only the tail is new.
      if (kept.length) filteredResults.value = [...filteredResults.value, ...kept]
    } else {
      filteredResults.value = isEffectivelyAll ? raw.slice() : raw.filter(keep)
      // A rebuild, not an append: every row index may now address a different result.
      filteredGeneration.value++
    }
    filteredThrough = raw.length

    // resultCounts (per-book/category tallies) drives ONLY the filter panel, which is
    // rendered solely while it's open. Rebuilding the map is a full O(n) scan over every
    // result; running it on each streaming flush of a high-frequency word (e.g. "כי",
    // hundreds of thousands of hits) is O(n²) over the stream and freezes the page.
    // Skip it while the panel is closed — the watch below recomputes once when it opens.
    if (isFilterOpen.value) rebuildResultCounts(raw)
  }

  function updateFilter(checked: Set<number>) {
    const ids = computeEffectiveIds(checked)
    effectiveBookIds.value = ids
    filteredThrough = 0 // criteria changed → full rebuild
    applyFilter(ids, 'rebuild')
  }

  // Checkbox change → immediate
  watch(checkedBookIds, (checked) => updateFilter(checked))

  // @ tokens change → resolve their books in the catalog index, then refilter.
  watch(atFilters, async (tokens) => {
    const generation = ++atFilterGeneration
    const isStale = () => generation !== atFilterGeneration
    if (!tokens.length) {
      atFilterBookIds = null
      updateFilter(checkedBookIds.value)
      return
    }
    const ids = await resolveAtFilterIds(tokens, isStale)
    if (!ids) return
    atFilterBookIds = ids
    updateFilter(checkedBookIds.value)
  })

  // Results changed. During streaming this is a pure append (results only grows by a
  // batch), so we fold just the new tail. A shrink or a same-length replacement means the
  // array was reordered (sort finalize) or swapped (new search / cache restore) — that
  // can't be reconciled incrementally, so rebuild from scratch.
  watch(() => results(), (raw) => {
    const mode = raw.length > filteredThrough ? 'append' : 'rebuild'
    if (mode === 'rebuild') filteredThrough = 0
    applyFilter(effectiveBookIds.value, mode)
  })

  // Panel opened → its per-book counts were skipped while it was closed (see applyFilter).
  // Compute them once now from the current result set; while it stays open, applyFilter
  // keeps them live on each flush.
  watch(isFilterOpen, (open) => {
    if (open) rebuildResultCounts(results())
  })

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
    effectiveBookIds.value = computeEffectiveIds(ids)
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
  function checkVisible(ids: number[]) {
    const next = new Set(checkedBookIds.value)
    for (const id of ids) next.add(id)
    checkedBookIds.value = next
  }

  async function handleSearch(q: string) {
    paneNavigation.updateActiveTab({ title: t('fullTextSearch.tabTitleQuery', { q }) })
    await executeSearch(q)
  }

  function handleClearSearch() {
    clearSearch()
    searchQuery.value = ''
    atFilters.value = []
    paneNavigation.updateActiveTab({ title: t('fullTextSearch.tabTitle') })
  }

  async function handleResultClick(result: FullTextSearchResult, openInNewTab = false) {
    try {
      // Capture the target tab up front and patch it by id: the lookup below awaits,
      // and the active tab may change during that await (same reasoning as AddressBar).
      const title = bookTitleOf(result)
      const targetTabId = openInNewTab
        ? paneNavigation.openTab({ title, route: '/book-view' }).id
        : paneNavigation.activeTabId

      const rows = await getLineIndexFromLineId(result.lineId)
      const lineIndex = rows[0]?.lineIndex
      const bookId = rows[0]?.bookId
      if (lineIndex == null || bookId == null) return
      const matchedTerms = matchedTermsOf(result)
      tabStore.updateTab(targetTabId, {
        title,
        route: '/book-view',
        bookId,
        openTocLineIndex: lineIndex,
        searchHighlightLineIndex: lineIndex,
        searchHighlightQuery: executedQuery(),
        searchHighlightSnippet: snippetHtmlOf(result) ?? '',  // the clicked row is rendered, so built
        // Use the concrete matched terms from FtsLib (includes fuzzy/wildcard expansions)
        // rather than splitting the raw query, so the book view highlights the actual
        // words that appeared in the line.
        searchHighlightTerms: matchedTerms.length
          ? matchedTerms
          : executedQuery().trim().split(/\s+/).filter(Boolean),
      })
    } catch (err) {
      console.error('[useFullTextSearchFilters] failed to open result:', err)
    }
  }

  return {
    searchQuery,
    isFilterOpen,
    checkedBookIds,
    atFilters,
    filteredResults,
    filteredGeneration,
    resultCounts,
    initCheckedBooks,
    setCheckedBookIds,
    setAtFilters,
    toggleBook,
    toggleCategory,
    checkAll,
    uncheckAll,
    checkVisible,
    handleSearch,
    handleClearSearch,
    handleResultClick,
  }
}
