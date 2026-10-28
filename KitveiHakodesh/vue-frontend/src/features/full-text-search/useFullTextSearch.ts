/**
 * Full-text search composable.
 *
 * ONE path, both runtimes: the KitveiHakodesh service PUSHES result frames continuously over
 * one connection (`ftsSearchStream`) until the search finishes. In dev that connection is the
 * service's loopback HTTP host; in the hosted app it is the C# relay in front of the service's
 * named pipe. Aborting the stream is the cancel signal, and the service itself supersedes the
 * previous search when a new one starts. Hits are NUMBERS ONLY (see FullTextSearchResult):
 * book id, a snippet descriptor, and ids into the search's matched-term
 * table, which streams once per search (`newTerms` per frame) and is resolved here
 * (matchedTermsOf, from the term id of each snippet mark). Titles, TOC paths and snippet
 * HTML are built per rendered row.
 */
import { t } from '@/i18n'
import { computed, onScopeDispose, ref, shallowRef, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { serviceStream } from '@/webview-host/serviceClient'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import type { SearchCacheEntry } from '@/stores/searchCacheStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { chronologicalKey } from './chronology'
import { seedSnippetTexts, setSnippetContextWords } from './fullTextSearchSnippets'
import type { FtsSnippetText } from '@/webview-host/seforimApi'
import { SNIPPET_MARK_STRIDE, matchedTermIdsOf } from './fullTextSearchTypes'
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
  /** First frame of a resumed search whose resume line could not be placed: the service
   *  runs it from the start, so the hits already held are dropped. */
  restarted?: boolean
  /** Snippet texts for the search's first hits, riding on the frame (not on the hits):
   *  seeded into the snippet cache so the first screen paints without a fetch. Later frames
   *  carry none. */
  snippetTexts?: FtsSnippetText[]
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

/** `pinOwner` (workspace:tab) pins the entry this tab shows, so the cache budget never evicts it. */
export function useFullTextSearch(isIndexing?: () => boolean, pinOwner?: string) {
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
      // Hits are numbers only (snippet descriptor) since the on-demand
      // snippets/references. Entries cached in the earlier shapes (snippet HTML, or the
      // descriptor with per-hit strings) must not match — they are discarded by LRU
      // eviction, never migrated.
      'num',
      // Marks are (start, length, termId) triples and the hit's matched term ids are read
      // off them (no per-hit termIds field any more; stride-2 entries would render wrong).
      'mk3t',
      // Hits carry no tocEntryId (the row's TOC path is fetched by line id), and every hit
      // carries its descriptor over the matched line alone (no snippetLineIds, short flag).
      'notoc',
      'desc1',
      // Packed chunks addressed by a numeric id (searchCacheStore); older metas have none.
      'pk1',
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

  /**
   * Runs the search for `q`, saving every streamed batch to the IndexedDB cache as it
   * arrives. With `resumeFrom` (an unfinished cached run of the same search) the cached hits
   * are the start of the result set and the stream continues after the last of them —
   * nothing already received is fetched again.
   */
  async function executeSearch(q: string, resumeFrom?: SearchCacheEntry) {
    if (!q.trim()) return
    await cancelSearch()
    rerunWhenIndexed = null

    isSearching.value = true
    hasSearched.value = true
    // The stream's accumulator IS the published array: hits are pushed into it in place
    // and each flush only bumps the version. Published now (empty, or the resumed hits),
    // so the first flush is already an append (same identity) and not a rebuild.
    const acc: FullTextSearchResult[] = resumeFrom ? resumeFrom.results : []
    results.value = acc
    resultsVersion.value++
    streamedOrder = acc
    searchTerms = resumeFrom ? resumeFrom.terms : []
    sortOrder.value = 'catalogOrder'   // each new search starts in streamed order; user re-sorts after
    searchError.value = null
    executedQuery.value = q

    const normalizedQuery = q.trim().toLowerCase()
    const cacheKey = _buildCacheKey(normalizedQuery)
    if (pinOwner) await cache.pin(pinOwner, cacheKey).catch(() => {/* non-fatal */})
    setSnippetContextWords(settings.searchContextMarginWords)
    // Every run is cached, batch by batch. One run while the index is still building is
    // marked so: a later visit shows it, then runs it again once the index is ready.
    const indexingComplete = !isIndexing?.()
    let terms = searchTerms
    const resumeAfter = resumeFrom ? acc[acc.length - 1]?.lineId ?? 0 : 0
    const resumed = resumeAfter > 0 && await cache.resume(cacheKey, terms).catch(() => false)
    if (executedQuery.value !== q) return // superseded while the cache answered
    if (!resumed && acc.length) {
      // Nothing to continue (the entry went meanwhile): start over, or every hit would show twice.
      acc.length = 0
      terms = searchTerms = []
      resultsVersion.value++
    }
    if (!resumed) await cache.init(cacheKey, q, indexingComplete, terms).catch(() => {/* best-effort */})
    if (executedQuery.value !== q) return // superseded while the cache answered

    const abort = new AbortController()
    _streamAbort = abort
    // A resumed stream numbers its matched terms from 0 again: each frame's new terms are
    // mapped onto this search's table, and the hits' mark term ids rewritten to match.
    const streamTermToLocal: number[] = []
    try {
      const stream = serviceStream<SearchStreamChunk>('ftsSearchStream', {
        query: _buildQueryToSend(normalizedQuery),
        maxWordDistance: settings.searchMaxWordDistance,
        requireOrdered: settings.searchRequireOrdered,
        contextWords: settings.searchContextMarginWords,
        resumeAfterLineId: resumed ? resumeAfter : 0,
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
        if (chunk.restarted && acc.length) {
          // The resume line could not be placed (the library changed): the service runs the
          // search from the start, so the cached hits go.
          acc.length = 0
          terms = searchTerms = []
          flush()
          await cache.init(cacheKey, q, indexingComplete, terms).catch(() => {/* best-effort */})
          if (executedQuery.value !== q) { abort.abort(); return } // superseded meanwhile
        }
        if (chunk.stage) enterStage(chunk.stage)
        if (chunk.newTerms?.length) {
          for (const term of chunk.newTerms) {
            const known = terms.indexOf(term)
            streamTermToLocal.push(known >= 0 ? known : terms.push(term) - 1)
          }
        }
        seedSnippetTexts(chunk.snippetTexts) // before the hits reach the list: its first render finds them
        if (chunk.ready === false) {
          searchError.value = 'indexNotReady'
          return
        }
        const hits = chunk.results ?? []
        if (hits.length) {
          enterResultsStage()
          if (resumed) for (const hit of hits) remapMarkTerms(hit, streamTermToLocal)
          for (const c of hits) acc.push(c) // spread-push would overflow on very large chunks
          dirty = true
          if (performance.now() - lastFlushAt >= FLUSH_INTERVAL_MS) flush()
          cache.appendBatch(cacheKey, hits) // buffered, never awaited: the cache keeps its own ordered write chain
        }
        if (executedQuery.value !== q) { abort.abort(); return } // superseded while caching
        if (chunk.error) {
          if (dirty) flush() // surface whatever streamed before the error
          if (!acc.length) searchError.value = 'searchFailed'
          // Keep what arrived: a later visit resumes after it.
          cache.flush(cacheKey)
          break
        }
        if (chunk.done) {
          if (dirty) flush() // final trailing flush before sorting
          // Search complete — apply the sort now (no-op for 'catalogOrder').
          // Done here rather than in `finally` so it runs only on genuine completion,
          // not when the loop exits early via supersession/error.
          await finalizeSort()
          await cache.markComplete(cacheKey).catch(() => {/* non-fatal */})
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
        if (!acc.length) searchError.value = 'searchFailed'
      }
    } finally {
      // Whatever arrived and is still buffered is written, so the next visit resumes after it.
      cache.flush(cacheKey)
      if (_streamAbort === abort) _streamAbort = null
      if (executedQuery.value === q) {
        isSearching.value = false
        currentStage.value = null
        clearStageLabel()
      }
    }
  }

  /** Rewrites a resumed hit's mark term ids (stream-local) onto the search's table. */
  function remapMarkTerms(hit: FullTextSearchResult, streamTermToLocal: number[]) {
    const marks = hit.snippetMarks
    if (!marks) return
    for (let i = SNIPPET_MARK_STRIDE - 1; i < marks.length; i += SNIPPET_MARK_STRIDE) {
      marks[i] = streamTermToLocal[marks[i]!] ?? marks[i]!
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
    rerunWhenIndexed = null
  }

  function clearCachedResults(q: string): void {
    const normalizedQuery = q.trim().toLowerCase()
    cache.remove(_buildCacheKey(normalizedQuery)).catch(() => {/* non-fatal */})
  }

  /** A search shown from a cache entry made while the index was building: run again, from
   *  the start, the moment the index is ready. */
  let rerunWhenIndexed: string | null = null
  watch(
    () => isIndexing?.() ?? false,
    (indexing, wasIndexing) => {
      if (wasIndexing && !indexing && rerunWhenIndexed && rerunWhenIndexed === executedQuery.value) {
        const q = rerunWhenIndexed
        rerunWhenIndexed = null
        executeSearch(q).catch((err) => console.error('[useFullTextSearch] re-run after indexing failed:', err))
      }
    },
  )

  /**
   * Session restore / tab switch: show the cached hits AT ONCE — finished or not — and then
   * bring them up to date:
   *  - run while the index was building: run again from the start once it is ready (now,
   *    or when the build finishes);
   *  - unfinished: resume the stream after the last cached hit;
   *  - finished on a built index: nothing more to do.
   * False when there is nothing cached (the caller searches).
   */
  async function loadCachedResults(q: string): Promise<boolean> {
    const normalizedQuery = q.trim().toLowerCase()
    const cacheKey = _buildCacheKey(normalizedQuery)
    if (pinOwner) await cache.pin(pinOwner, cacheKey).catch(() => {/* non-fatal */})
    const cached = await cache.get(cacheKey, q).catch(() => null)
    if (!cached || (cached.results.length === 0 && cached.complete === false)) return false
    results.value = cached.results
    resultsVersion.value++
    streamedOrder = cached.results   // cached in the order it streamed
    searchTerms = cached.terms
    setSnippetContextWords(settings.searchContextMarginWords)
    executedQuery.value = q
    hasSearched.value = true
    searchError.value = null
    if (!cached.indexingComplete) {
      if (isIndexing?.()) rerunWhenIndexed = q
      else executeSearch(q).catch((err) => console.error('[useFullTextSearch] re-run of a mid-build search failed:', err))
    } else if (!cached.complete) {
      executeSearch(q, cached).catch((err) => console.error('[useFullTextSearch] resume after tab restore failed:', err))
    }
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
