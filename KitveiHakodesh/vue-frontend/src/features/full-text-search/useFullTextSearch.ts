/**
 * Full-text search composable.
 *
 * ONE path, both runtimes: the KitveiHakodesh service PUSHES result frames continuously over
 * one connection (`ftsSearchStream`) until the search finishes. In dev that connection is the
 * service's loopback HTTP host; in the hosted app it is the C# relay in front of the service's
 * named pipe. Aborting the stream is the cancel signal, and the service itself supersedes the
 * previous search when a new one starts. Hits are NUMBERS ONLY (see FullTextSearchResult):
 * book id + TOC entry id, a snippet descriptor, and ids into the search's matched-term
 * table, which streams once per search (`newTerms` per frame) and is resolved here
 * (matchedTermsOf, from the term id of each snippet mark). Titles, TOC paths and snippet
 * HTML are built per rendered row.
 */
import { t } from '@/i18n'
import { computed, onScopeDispose, ref, shallowRef, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { serviceStream } from '@/webview-host/serviceClient'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { chronologicalKey } from './chronology'
import { seedLineTexts, setSnippetSource } from './fullTextSearchSnippets'
import { matchedTermIdsOf } from './fullTextSearchTypes'
import { WORD_OPTIONS, applyWordOptions } from './queryWordSyntax'
import type { AllWords, PerWord, WordOption } from './queryWordSyntax'
import type { FullTextSearchResult, SearchFailReason, FullTextSearchSortOrder } from './fullTextSearchTypes'

// Flush cadence. A flush publishes the hits appended since the last one: the result
// array is ONE array grown in place for the whole stream, and `resultsVersion` is what
// consumers react to — so a flush costs O(batch), not O(n), and the cadence can stay
// constant however large the set grows. Results are never capped.
const FLUSH_INTERVAL_MS = 150

/** One frame of the `ftsSearchStream` op. */
interface SearchStreamChunk {
  ready: boolean
  results: FullTextSearchResult[]
  /** Matched-term strings first seen in this frame, in id order — appended to the
   *  search's term table, which the hits' mark term ids index into. */
  newTerms?: string[]
  /** Snippet line texts (line id -> text) for the search's first hits, riding on the frame
   *  (not on the hits): seeded into the snippet cache so the first screen paints without a
   *  fetch. Later frames carry none. Int map keys arrive as strings from MessagePack. */
  lineTexts?: Record<string, string>
  done: boolean
  error?: string
  /** What the engine is doing right now (stable ASCII key). A frame of its own before the
   *  first results frame; folded onto results frames after it. */
  stage?: string
}

/** Short labels for the service's search stage keys. Unknown keys show nothing. */
export const SEARCH_STAGE_LABELS: Record<string, string> = {
  rewrite: t('fullTextSearch.stage.rewrite'),
  parse: t('fullTextSearch.stage.parse'),
  expand: t('fullTextSearch.stage.expand'),
  lookup: t('fullTextSearch.stage.lookup'),
  fetch: t('fullTextSearch.stage.fetch'),
  snippets: t('fullTextSearch.stage.snippets'),
  enrich: t('fullTextSearch.stage.enrich'),
}

export function useFullTextSearch(isIndexing?: () => boolean) {
  const cache = useSearchCacheStore()
  const settings = useSettingsStore()
  const booksStore = useBooksDataStore()
  const { searchMaxWordDistance, searchRequireOrdered } = storeToRefs(settings)

  // Each option's persisted "all words" setting, keyed by option.
  const ALL_WORDS_SETTINGS = {
    wildcardPrefix: 'searchWildcardPrefix', wildcardSuffix: 'searchWildcardSuffix',
    grammarPrefix: 'searchGrammarPrefix', grammarSuffix: 'searchGrammarSuffix',
    fuzzy: 'searchFuzzy', ketiv: 'searchExpandKetiv',
    inflections: 'searchExpandInflections', synonyms: 'searchExpandSynonyms',
  } as const satisfies Record<WordOption, keyof typeof settings>
  const allWords = computed<AllWords>(() => {
    const out = {} as AllWords
    for (const option of WORD_OPTIONS) out[option] = settings[ALL_WORDS_SETTINGS[option]]
    return out
  })
  function setAllWords(option: WordOption, on: boolean) {
    settings[ALL_WORDS_SETTINGS[option]] = on
  }
  // Per-word choices: per tab, saved with the tab's view state (the page restores them).
  const perWord = ref<PerWord>({})

  // Sort order is per-search and ephemeral — NOT persisted. Every new search resets it to
  // 'catalogOrder' (the streamed order), so results always finish in catalog order and the
  // user must actively pick 'relevance' from the toggle (which is only shown once streaming
  // completes) to reorder. See executeSearch (reset) and the watch below (user-driven re-sort).
  const sortOrder = ref<FullTextSearchSortOrder>('catalogOrder')

  // Every option reaches the service as query syntax: the toggles are written onto the typed
  // words (see applyWordOptions). Pipes are padded first, so "a|b" gets them on both words.
  function _buildQueryToSend(normalizedQuery: string): string {
    return applyWordOptions(normalizedQuery.replace(/\|/g, ' | '), allWords.value, perWord.value)
  }

  function _buildCacheKey(normalizedQuery: string): string {
    const queryToSend = _buildQueryToSend(normalizedQuery)
    return [
      queryToSend,
      `d${settings.searchMaxWordDistance}`,
      settings.searchRequireOrdered ? 'ord' : '',
      `ctx${settings.searchContextMarginWords}`,
      // Results stream in catalog order since the FTS5 index; entries cached before that are
      // in line-id order (and from the old index), so they must not match.
      'catalog',
      // Hits are numbers only (snippet descriptor, tocEntryId) since the on-demand
      // snippets/references. Entries cached in the earlier shapes (snippet HTML, or the
      // descriptor with per-hit strings) must not match — they are discarded by LRU
      // eviction, never migrated.
      'num',
      // Marks are (start, length, termId) triples and the hit's matched term ids are read
      // off them (no per-hit termIds field any more; stride-2 entries would render wrong).
      'mk3t',
    ]
      .filter(Boolean)
      .join('|')
  }

  /**
   * The result set. While a search streams, this is ONE array appended to in place —
   * its identity does not change per flush, only `resultsVersion` does. A rebuild (new
   * search, sort, cache restore) assigns a NEW array and bumps the version too, so the
   * identity tells an append from a rebuild and the version tells that anything changed.
   */
  const results = shallowRef<FullTextSearchResult[]>([])
  /** Bumped on every change to `results` — in-place appends included. Consumers that
   *  read `results.value.length` or iterate it must depend on this, not on the ref. */
  const resultsVersion = ref(0)
  const resultCount = computed(() => { resultsVersion.value; return results.value.length })
  /** The current result set's matched-term table (the hits' mark term ids index into it). */
  let searchTerms: string[] = []
  /** The search's id for a matched term — appended when a snippet fetched later brings a new one. */
  function termIdOf(term: string): number {
    const known = searchTerms.indexOf(term)
    if (known >= 0) return known
    searchTerms.push(term)
    return searchTerms.length - 1
  }

  /** Tells the snippet loader which search (query as sent, options) later rows' snippets belong to. */
  function publishSnippetSource(queryToSend: string) {
    setSnippetSource({
      query: queryToSend,
      requireOrdered: settings.searchRequireOrdered,
      contextWords: settings.searchContextMarginWords,
      termIdOf,
    })
  }

  /** The concrete index terms that matched a hit — what the book view highlights. */
  function matchedTermsOf(hit: FullTextSearchResult): string[] {
    const out: string[] = []
    for (const id of matchedTermIdsOf(hit)) {
      const term = searchTerms[id]
      if (term !== undefined) out.push(term)
    }
    return out
  }
  const isSearching = ref(false)
  /** Live stage key of the running search; null when idle, done, failed or cancelled. */
  const currentStage = ref<string | null>(null)
  /** Stage label shown only once a stage has lasted 1s; '' until then. */
  const settledStageLabel = ref('')
  let stageTimer: ReturnType<typeof setTimeout> | null = null
  function clearStageLabel() {
    if (stageTimer) { clearTimeout(stageTimer); stageTimer = null }
    settledStageLabel.value = ''
    shownStage = null
    resultsArrived = false
  }
  /** Stage key whose label is on screen; when it ends, the next stage shows at once. */
  let shownStage: string | null = null
  /** Once hits stream in, the label locks on the results label for the rest of the search. */
  let resultsArrived = false
  function enterStage(stage: string) {
    if (resultsArrived) return
    const shownJustEnded = shownStage !== null && shownStage === currentStage.value && stage !== shownStage
    currentStage.value = stage
    if (stageTimer) clearTimeout(stageTimer)
    shownStage = null
    // Only the stage right after a slow (1s-settled) one shows at once; it does not chain.
    if (shownJustEnded && SEARCH_STAGE_LABELS[stage]) settledStageLabel.value = SEARCH_STAGE_LABELS[stage]
    stageTimer = setTimeout(() => {
      stageTimer = null
      if (currentStage.value === stage && SEARCH_STAGE_LABELS[stage]) { shownStage = stage; settledStageLabel.value = SEARCH_STAGE_LABELS[stage] }
    }, 1000)
  }
  function enterResultsStage() {
    if (resultsArrived) return
    resultsArrived = true
    if (stageTimer) { clearTimeout(stageTimer); stageTimer = null }
    settledStageLabel.value = SEARCH_STAGE_LABELS.fetch ?? ''
  }
  onScopeDispose(clearStageLabel)
  /** Text for the stage indicator: generic "searching" until a stage settles. */
  const stageLabel = computed(() => isSearching.value ? (settledStageLabel.value || t('fullTextSearch.stage.searching')) : '')
  const hasSearched = ref(false)
  const executedQuery = ref('')
  const searchError = ref<SearchFailReason | null>(null)
  // The in-flight stream — aborting it closes the connection, which is the service's
  // cancel signal (no cancel op, no polling).
  let _streamAbort: AbortController | null = null
  // The result set in the order the service streamed it — the library's catalog tree order
  // (the index stores lines in that order). 'catalogOrder' restores it, and every other sort
  // breaks ties by it. Kept apart from `results`, which a re-sort reorders.
  let streamedOrder: FullTextSearchResult[] = []

  /**
   * A rank per DISTINCT book of the result set under `compare` (a book-level order:
   * title collation, author, chronology). The distinct books — a few thousand at most —
   * are sorted once with the string comparator; the hits then compare integer ranks,
   * so the O(n log n) hit sort never calls localeCompare.
   */
  function bookRanks(compare: (a: number, b: number) => number): Map<number, number> {
    const distinct = new Set<number>()
    for (const r of streamedOrder) distinct.add(r.bookId)
    const ids = [...distinct].sort(compare)
    const ranks = new Map<number, number>()
    ids.forEach((id, rank) => ranks.set(id, rank))
    return ranks
  }

  // Apply the current relevancy sort to the fully-collected result set. Called only
  // once the search has completed — sorting mid-stream would fight the incremental
  // append (results paint in catalog order as they arrive). Every order sorts a COPY of
  // the streamed order with a stable sort (Array.prototype.sort is stable since ES2019),
  // so ties keep the streamed (catalog) order with no tiebreak lookup per comparison.
  // 'catalogOrder' is the streamed order itself. The result is always a NEW array (a
  // rebuild — consumers re-index), with the version bumped.
  async function finalizeSort() {
    if (streamedOrder.length < 2) return

    // chronological/authorName need per-book metadata (period / authors) that
    // booksDataStore derives from the catalog lazily. Load it before sorting; guard
    // against a newer sort pick landing while we awaited.
    if (sortOrder.value === 'chronological' || sortOrder.value === 'authorName') {
      const picked = sortOrder.value
      await booksStore.ensureCommentaryMetadataLoaded()
      if (sortOrder.value !== picked) return
    }

    const sorted = streamedOrder.slice()
    const bookMap = booksStore.allBooksMap
    const titleOf = (id: number) => bookMap.get(id)?.title ?? ''
    const byTitle = (a: number, b: number) => titleOf(a).localeCompare(titleOf(b), 'he')
    const byRank = (ranks: Map<number, number>) =>
      (a: FullTextSearchResult, b: FullTextSearchResult) => ranks.get(a.bookId)! - ranks.get(b.bookId)!
    if (sortOrder.value === 'relevance') {
      // Sole relevancy key: minimum word distance (0 = query words adjacent → most relevant).
      // Ties keep their original streamed (catalog) order. We deliberately do NOT tiebreak on
      // `score` (character span) — that reorders equally-adjacent hits by irrelevant length
      // differences (nikud, word length), which reads as nonsense to the user.
      // `?? 0` guards results restored from a cache written before wordDistance existed on
      // the wire — treat a missing distance as "closest" rather than sorting to NaN.
      const wd = (r: FullTextSearchResult) => r.wordDistance ?? 0
      sorted.sort((a, b) => wd(a) - wd(b))
    } else if (sortOrder.value === 'bookName') {
      // Alphabetical by book title (Hebrew collation), then streamed order so lines within the
      // same book stay in document order.
      sorted.sort(byRank(bookRanks(byTitle)))
    } else if (sortOrder.value === 'authorName') {
      // Alphabetical by author name (Hebrew collation), then book name, then streamed order.
      // Authorless books (empty author string) sort last. book.authors is the comma-joined
      // author list; we collate on it as-is (its natural first author leads the string).
      const authorOf = (id: number) => bookMap.get(id)?.authors || ''
      sorted.sort(byRank(bookRanks((a, b) => {
        const aa = authorOf(a), ab = authorOf(b)
        if (!aa !== !ab) return aa ? -1 : 1 // non-empty authors before authorless
        return aa.localeCompare(ab, 'he') || byTitle(a, b)
      })))
    } else if (sortOrder.value === 'chronological') {
      // Author death-year (primary) → precision tier (an exact author year leads an era
      // estimate landing on the same year) → catalog tree order → book name → streamed order.
      // Books with neither an author year nor one of the three trusted era categories carry
      // year = Infinity and sort LAST — an honest "unknown" at the end rather than a
      // fabricated position mid-list. Tree order keeps same-key books in their curated
      // catalog sequence (Tanach in canonical book order, Mishna in seder order) instead of
      // alphabetical. Keys are cached per book because the author-year lookup normalizes
      // name strings.
      const keyCache = new Map<number, { year: number; precision: number; treeOrder: number }>()
      const keyOf = (id: number) => {
        let key = keyCache.get(id)
        if (!key) {
          const book = bookMap.get(id)
          const { year, precision } = chronologicalKey(book ?? { title: '' })
          // Finite fallback, not Infinity — the comparator subtracts these and
          // Infinity-Infinity is NaN (see YEAR_UNKNOWN in chronology/index.ts).
          key = { year, precision, treeOrder: book?.treeOrder ?? Number.MAX_SAFE_INTEGER }
          keyCache.set(id, key)
        }
        return key
      }
      sorted.sort(byRank(bookRanks((a, b) => {
        const keyA = keyOf(a)
        const keyB = keyOf(b)
        return (
          keyA.year - keyB.year ||
          keyA.precision - keyB.precision ||
          keyA.treeOrder - keyB.treeOrder ||
          byTitle(a, b)
        )
      })))
    }
    // else 'catalogOrder': the streamed order itself. A no-op right after streaming, but
    // matters when the user switches back from another order on an already-sorted set.
    results.value = sorted
    resultsVersion.value++
  }

  // Re-sort an already-completed result set when the user changes the sort order.
  // No-op while a search is in flight — but the toggle is hidden during streaming anyway,
  // so in practice this only fires on an explicit user pick after results finish.
  watch(sortOrder, () => {
    if (isSearching.value) return
    void finalizeSort().catch((err) => console.error('[useFullTextSearch] sort failed:', err))
  })

  async function cancelSearch() {
    // Closing the stream IS the cancel — the service sees the broken connection.
    if (_streamAbort) {
      _streamAbort.abort()
      _streamAbort = null
    }
    isSearching.value = false
    currentStage.value = null
    clearStageLabel()
  }

  async function executeSearch(q: string) {
    if (!q.trim()) return
    await cancelSearch()

    isSearching.value = true
    hasSearched.value = true
    // The stream's accumulator IS the published array: hits are pushed into it in place
    // and each flush only bumps the version. Published now, empty, so the first flush is
    // already an append (same identity) and not a rebuild.
    const acc: FullTextSearchResult[] = []
    results.value = acc
    resultsVersion.value++
    streamedOrder = acc
    searchTerms = []
    sortOrder.value = 'catalogOrder'   // each new search starts in streamed order; user re-sorts after
    searchError.value = null
    executedQuery.value = q

    const normalizedQuery = q.trim().toLowerCase()
    const cacheKey = _buildCacheKey(normalizedQuery)
    publishSnippetSource(_buildQueryToSend(normalizedQuery))
    // The IndexedDB cache serves session restore and tab switching (see loadCachedResults),
    // never a user-initiated search. Only a search run against a fully built index is
    // cached — partial results from a mid-build search would be served as complete and stale.
    const cacheable = !isIndexing?.()
    if (cacheable) await cache.init(cacheKey, q, false).catch(() => {/* best-effort */})

    const abort = new AbortController()
    _streamAbort = abort
    try {
      const terms = searchTerms
      const stream = serviceStream<SearchStreamChunk>('ftsSearchStream', {
        query: _buildQueryToSend(normalizedQuery),
        maxWordDistance: settings.searchMaxWordDistance,
        requireOrdered: settings.searchRequireOrdered,
        contextWords: settings.searchContextMarginWords,
      }, abort.signal)

      // Publish the pushed hits every FLUSH_INTERVAL_MS: the array is already the one
      // consumers hold, so a flush is a version bump and they fold in only the new tail.
      // `dirty` tracks pending hits so the last ones are not lost.
      let lastFlushAt = 0
      let dirty = false
      const flush = () => {
        resultsVersion.value++
        dirty = false
        lastFlushAt = performance.now()
      }

      for await (const chunk of stream) {
        if (executedQuery.value !== q) { abort.abort(); return } // superseded locally
        if (chunk.stage) enterStage(chunk.stage)
        if (chunk.newTerms?.length) for (const t of chunk.newTerms) terms.push(t)
        seedLineTexts(chunk.lineTexts) // before the hits reach the list: its first render finds them
        if (chunk.ready === false) {
          searchError.value = 'indexNotReady'
          return
        }
        const hits = chunk.results ?? []
        if (hits.length) {
          enterResultsStage()
          for (const c of hits) acc.push(c) // spread-push would overflow on very large chunks
          dirty = true
          if (performance.now() - lastFlushAt >= FLUSH_INTERVAL_MS) flush()
          if (cacheable) cache.appendBatch(cacheKey, hits) // buffered, never awaited: the cache keeps its own ordered write chain
        }
        if (executedQuery.value !== q) { abort.abort(); return } // superseded while caching
        if (chunk.error) {
          if (dirty) flush() // surface whatever streamed before the error
          if (!acc.length) searchError.value = 'searchFailed'
          // The stream did not complete: whatever was appended is an incomplete snapshot.
          if (cacheable) cache.remove(cacheKey).catch(() => {/* non-fatal */})
          break
        }
        if (chunk.done) {
          if (dirty) flush() // final trailing flush before sorting
          // Search complete — apply the sort now (no-op for 'catalogOrder').
          // Done here rather than in `finally` so it runs only on genuine completion,
          // not when the loop exits early via supersession/error.
          await finalizeSort()
          if (cacheable) await cache.markComplete(cacheKey, false, terms).catch(() => {/* non-fatal */})
          break
        }
      }
      // Safety: if the iterator completed without a done/error frame, surface any
      // hits that were still pending under the flush throttle.
      if (dirty && executedQuery.value === q) flush()
    } catch (err) {
      // An aborted stream is a cancel (new search / navigation), not a failure.
      if (!abort.signal.aborted) {
        console.error('[useFullTextSearch] FTS stream failed:', err)
        searchError.value = 'searchFailed'
        if (cacheable) cache.remove(cacheKey).catch(() => {/* non-fatal */})
      }
    } finally {
      if (_streamAbort === abort) _streamAbort = null
      if (executedQuery.value === q) {
        isSearching.value = false
        currentStage.value = null
        clearStageLabel()
      }
    }
  }

  function clearSearch() {
    results.value = []
    resultsVersion.value++
    streamedOrder = []
    searchTerms = []
    hasSearched.value = false
    executedQuery.value = ''
    searchError.value = null
  }

  function clearCachedResults(q: string): void {
    const normalizedQuery = q.trim().toLowerCase()
    cache.remove(_buildCacheKey(normalizedQuery)).catch(() => {/* non-fatal */})
  }

  /**
   * Session restore / tab switch: show the cached result set when one exists. A cache
   * entry that never completed cannot be resumed mid-stream (the service streams a whole
   * search or nothing), so it is re-run from the start instead.
   */
  async function loadCachedResults(q: string): Promise<boolean> {
    if (isIndexing?.()) return false
    const normalizedQuery = q.trim().toLowerCase()
    const cacheKey = _buildCacheKey(normalizedQuery)
    const cached = await cache.get(cacheKey, q)
    if (!cached || cached.results.length === 0) return false
    if (!cached.complete) {
      executeSearch(q).catch((err) => console.error('[useFullTextSearch] re-run after tab restore failed:', err))
      return true
    }
    results.value = cached.results
    resultsVersion.value++
    streamedOrder = cached.results   // cached in the order it streamed
    searchTerms = cached.terms
    publishSnippetSource(_buildQueryToSend(normalizedQuery))
    executedQuery.value = q
    hasSearched.value = true
    return true
  }

  return {
    results,
    resultsVersion,
    resultCount,
    matchedTermsOf,
    isSearching,
    currentStage,
    stageLabel,
    hasSearched,
    executedQuery,
    searchError,
    maxWordDistance: searchMaxWordDistance,
    requireOrdered: searchRequireOrdered,
    allWords,
    setAllWords,
    perWord,
    sortOrder,
    executeSearch,
    cancelSearch,
    clearSearch,
    clearCachedResults,
    loadCachedResults,
  }
}
