/**
 * Full-text search composable.
 *
 * ONE path, both runtimes: the KitveiHakodesh service PUSHES result frames continuously over
 * one connection (`ftsSearchStream`) until the search finishes. In dev that connection is the
 * service's loopback HTTP host; in the hosted app it is the C# relay in front of the service's
 * named pipe. Aborting the stream is the cancel signal, and the service itself supersedes the
 * previous search when a new one starts. Hits arrive fully enriched (bookId + tocText).
 */
import { ref, shallowRef, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { serviceStream } from '@/webview-host/serviceClient'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { chronologicalKey } from './chronology'
import type { FullTextSearchResult, SearchFailReason, FullTextSearchSortOrder } from './fullTextSearchTypes'

// Adaptive flush cadence. Each flush reassigns `results` to a fresh array, which costs
// O(n) for the array copy AND makes @tanstack/vue-virtual rebuild its ENTIRE measurements
// array (memoized on `count`; when the viewport is at the top `min` is 0, so it recomputes
// every row 0..count — see virtual-core getMeasurements). Flushing every 150ms while the
// set grows into the hundreds-of-thousands (a high-frequency word) makes that O(n²) and
// freezes the page. So: flush fast while the list is small (snappy first paint), then back
// off sharply so the expensive rebuilds happen only a handful of times.
// Results are never capped — everything still streams in; only the paint cadence changes.
function _flushDelayFor(count: number): number {
  if (count < 5_000) return 150
  if (count < 50_000) return 600
  return 2000
}

/** One frame of the `ftsSearchStream` op. */
interface SearchStreamChunk {
  ready: boolean
  results: FullTextSearchResult[]
  done: boolean
  error?: string
}

export function useFullTextSearch(isIndexing?: () => boolean) {
  const cache = useSearchCacheStore()
  const settings = useSettingsStore()
  const booksStore = useBooksDataStore()
  const { searchMaxWordDistance, searchRequireOrdered, searchExpandKetiv, searchExpandRelated, searchGrammarWrap } = storeToRefs(settings)

  // Sort order is per-search and ephemeral — NOT persisted. Every new search resets it to
  // 'catalogOrder' (the streamed order), so results always finish in catalog order and the
  // user must actively pick 'relevance' from the toggle (which is only shown once streaming
  // completes) to reorder. See executeSearch (reset) and the watch below (user-driven re-sort).
  const sortOrder = ref<FullTextSearchSortOrder>('catalogOrder')

  function _buildQueryToSend(normalizedQuery: string): string {
    if (!settings.searchGrammarWrap) return normalizedQuery
    return normalizedQuery
      .split(/\s+/)
      .map((word) => {
        if (!word) return word
        if (/[*~|%]/.test(word)) return word  // already has special syntax
        return `%${word}%`
      })
      .join(' ')
  }

  function _buildCacheKey(normalizedQuery: string): string {
    const queryToSend = _buildQueryToSend(normalizedQuery)
    return [
      queryToSend,
      `d${settings.searchMaxWordDistance}`,
      settings.searchRequireOrdered ? 'ord' : '',
      settings.searchExpandKetiv ? 'ktv' : '',
      settings.searchExpandRelated ? 'rel' : '',
      `ctx${settings.searchContextMarginWords}`,
      // Results stream in catalog order since the FTS5 index; entries cached before that are
      // in line-id order (and from the old index), so they must not match.
      'catalog',
    ]
      .filter(Boolean)
      .join('|')
  }

  const results = shallowRef<FullTextSearchResult[]>([])
  const isSearching = ref(false)
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

  // Apply the current relevancy sort to the fully-collected result set. Called only
  // once the search has completed — sorting mid-stream would fight the incremental
  // append (results paint in catalog order as they arrive). 'catalogOrder' is the streamed
  // order itself; 'relevance' orders by word-distance score (smaller = closer) with the
  // streamed order as a stable tiebreaker. results is a shallowRef, so we always assign a
  // fresh array to trigger reactivity.
  async function finalizeSort() {
    if (results.value.length < 2) return

    // chronological/authorName need per-book metadata (period / authors) that
    // booksDataStore derives from the catalog lazily. Load it before sorting; guard
    // against a newer sort pick landing while we awaited.
    if (sortOrder.value === 'chronological' || sortOrder.value === 'authorName') {
      const picked = sortOrder.value
      await booksStore.ensureCommentaryMetadataLoaded()
      if (sortOrder.value !== picked) return
    }

    const sorted = [...results.value]
    const streamedPosition = new Map(streamedOrder.map((r, i) => [r, i]))
    const byStream = (a: FullTextSearchResult, b: FullTextSearchResult) =>
      (streamedPosition.get(a) ?? 0) - (streamedPosition.get(b) ?? 0)
    if (sortOrder.value === 'relevance') {
      // Sole relevancy key: minimum word distance (0 = query words adjacent → most relevant).
      // Ties keep their original streamed (catalog) order. We deliberately do NOT tiebreak on
      // `score` (character span) — that reorders equally-adjacent hits by irrelevant length
      // differences (nikud, word length), which reads as nonsense to the user.
      // `?? 0` guards results restored from a cache written before wordDistance existed on
      // the wire — treat a missing distance as "closest" rather than sorting to NaN.
      const wd = (r: FullTextSearchResult) => r.wordDistance ?? 0
      sorted.sort((a, b) => wd(a) - wd(b) || byStream(a, b))
    } else if (sortOrder.value === 'bookName') {
      // Alphabetical by book title (Hebrew collation), then streamed order so lines within the
      // same book stay in document order.
      sorted.sort((a, b) => (a.bookTitle ?? '').localeCompare(b.bookTitle ?? '', 'he') || byStream(a, b))
    } else if (sortOrder.value === 'authorName') {
      // Alphabetical by author name (Hebrew collation), then book name, then streamed order.
      // Authorless books (empty author string) sort last. book.authors is the comma-joined
      // author list; we collate on it as-is (its natural first author leads the string).
      const bookMap = booksStore.allBooksMap
      const authorOf = (r: FullTextSearchResult) => bookMap.get(r.bookId)?.authors || ''
      sorted.sort((a, b) => {
        const aa = authorOf(a), ab = authorOf(b)
        if (!aa !== !ab) return aa ? -1 : 1 // non-empty authors before authorless
        return aa.localeCompare(ab, 'he') ||
          (a.bookTitle ?? '').localeCompare(b.bookTitle ?? '', 'he') ||
          byStream(a, b)
      })
    } else if (sortOrder.value === 'chronological') {
      // Author death-year (primary) → precision tier (an exact author year leads an era
      // estimate landing on the same year) → catalog tree order → book name → streamed order.
      // Books with neither an author year nor one of the three trusted era categories carry
      // year = Infinity and sort LAST — an honest "unknown" at the end rather than a
      // fabricated position mid-list. Tree order keeps same-key books in their curated
      // catalog sequence (Tanach in canonical book order, Mishna in seder order) instead of
      // alphabetical. Keys are cached per bookId because the comparator runs O(n log n)
      // times and the author-year lookup normalizes name strings.
      const bookMap = booksStore.allBooksMap
      const keyCache = new Map<number, { year: number; precision: number; treeOrder: number }>()
      const keyOf = (r: FullTextSearchResult) => {
        let key = keyCache.get(r.bookId)
        if (!key) {
          const book = bookMap.get(r.bookId)
          // r.bookTitle backs up book.title so the canonical-work lookup still resolves for
          // a hit whose book is missing from allBooksMap.
          const { year, precision } = chronologicalKey(
            book ? { ...book, title: book.title ?? r.bookTitle } : { title: r.bookTitle },
          )
          // Finite fallback, not Infinity — the comparator subtracts these and
          // Infinity-Infinity is NaN (see YEAR_UNKNOWN in chronology/index.ts).
          key = { year, precision, treeOrder: book?.treeOrder ?? Number.MAX_SAFE_INTEGER }
          keyCache.set(r.bookId, key)
        }
        return key
      }
      sorted.sort((a, b) => {
        const keyA = keyOf(a)
        const keyB = keyOf(b)
        return (
          keyA.year - keyB.year ||
          keyA.precision - keyB.precision ||
          keyA.treeOrder - keyB.treeOrder ||
          (a.bookTitle ?? '').localeCompare(b.bookTitle ?? '', 'he') ||
          byStream(a, b)
        )
      })
    } else {
      // 'catalogOrder' — restore the streamed order. A no-op right after streaming, but
      // matters when the user switches back from another order on an already-sorted set.
      sorted.sort(byStream)
    }
    results.value = sorted
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
  }

  async function executeSearch(q: string) {
    if (!q.trim()) return
    await cancelSearch()

    isSearching.value = true
    hasSearched.value = true
    results.value = []
    streamedOrder = []
    sortOrder.value = 'catalogOrder'   // each new search starts in streamed order; user re-sorts after
    searchError.value = null
    executedQuery.value = q

    const normalizedQuery = q.trim().toLowerCase()
    const cacheKey = _buildCacheKey(normalizedQuery)
    // The IndexedDB cache serves session restore and tab switching (see loadCachedResults),
    // never a user-initiated search. Only a search run against a fully built index is
    // cached — partial results from a mid-build search would be served as complete and stale.
    const cacheable = !isIndexing?.()
    if (cacheable) await cache.init(cacheKey, q, false).catch(() => {/* best-effort */})

    const abort = new AbortController()
    _streamAbort = abort
    try {
      const acc: FullTextSearchResult[] = []
      const stream = serviceStream<SearchStreamChunk>('ftsSearchStream', {
        query: _buildQueryToSend(normalizedQuery),
        maxWordDistance: settings.searchMaxWordDistance,
        requireOrdered: settings.searchRequireOrdered,
        contextWords: settings.searchContextMarginWords,
        expandKetiv: settings.searchExpandKetiv,
        expandRelated: settings.searchExpandRelated,
      }, abort.signal)

      // Flush the accumulator into the reactive ref on the adaptive cadence
      // (_flushDelayFor). Each flush copies the whole accumulator (a new array is
      // required to trigger reactivity) and forces a full virtualizer measurement
      // rebuild, so flushing on every pushed frame is O(n²) over the stream and
      // freezes the page for high-frequency words. Backing off as the set grows bounds
      // those rebuilds; `dirty` tracks pending hits so the last isn't lost.
      let lastFlushAt = 0
      let dirty = false
      const flush = () => {
        results.value = acc.slice()
        streamedOrder = acc
        dirty = false
        lastFlushAt = performance.now()
      }

      for await (const chunk of stream) {
        if (executedQuery.value !== q) { abort.abort(); return } // superseded locally
        if (chunk.ready === false) {
          searchError.value = 'indexNotReady'
          return
        }
        const hits = chunk.results ?? []
        if (hits.length) {
          for (const c of hits) acc.push(c) // spread-push would overflow on very large chunks
          dirty = true
          if (performance.now() - lastFlushAt >= _flushDelayFor(acc.length)) flush()
          if (cacheable) await cache.appendBatch(cacheKey, hits).catch(() => {/* best-effort */})
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
          if (cacheable) await cache.markComplete(cacheKey, false).catch(() => {/* non-fatal */})
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
      if (executedQuery.value === q) isSearching.value = false
    }
  }

  function clearSearch() {
    results.value = []
    streamedOrder = []
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
    streamedOrder = cached.results   // cached in the order it streamed
    executedQuery.value = q
    hasSearched.value = true
    return true
  }

  return {
    results,
    isSearching,
    hasSearched,
    executedQuery,
    searchError,
    maxWordDistance: searchMaxWordDistance,
    requireOrdered: searchRequireOrdered,
    expandKetiv: searchExpandKetiv,
    expandRelated: searchExpandRelated,
    grammarWrap: searchGrammarWrap,
    sortOrder,
    executeSearch,
    cancelSearch,
    clearSearch,
    clearCachedResults,
    loadCachedResults,
  }
}
