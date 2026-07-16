/**
 * Full-text search composable — wraps C# streaming search (FtsLib backend).
 *
 * C# sends search stream events (searchBatch, searchComplete, etc.) via
 * PostWebMessageAsJson → JsBridge → window.__onWebviewEvent → onWebviewEvent().
 * C# sends indexing progress the same way via the ftsIndexProgress event.
 *
 * Falls back to sample data in dev when the C# host is not present.
 */
import { ref, shallowRef, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { isHosted, onWebviewEvent } from '@/webview-host/seforimDb'
import { getTocPathsForLines, getBookIdsForLines } from '@/webview-host/seforimApi'
import { serviceStream } from '@/webview-host/serviceClient'
import { callBridgeAction } from '@/webview-host/bridge'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { eraRank, authorYear } from './ftsChronology'
import type { FullTextSearchResult, SearchFailReason, FullTextSearchSortOrder } from './fullTextSearchTypes'


// ── chrome.webview message listener (search stream events from C#) ────────────
// C# sends search events via window.__onWebviewEvent (routed through JsBridge).
// We maintain a single shared listener and route by searchId.

type SearchListeners = {
  onBatch: (results: FullTextSearchResult[]) => Promise<void>
  onComplete: () => Promise<void>
  onCancelled: () => void
  onError: (reason: SearchFailReason, detail?: string) => void
}

// Tracks in-flight onBatch promises so onComplete waits for all enrichment to finish
const _pendingBatches = new Map<string, Promise<void>>()
const _searchListeners = new Map<string, SearchListeners>()

// Single module-level listener registered once — routes all search stream events
onWebviewEvent((msg) => {
  const searchId = msg.searchId as string | undefined
  if (!msg.type || !searchId) return
  const listener = _searchListeners.get(searchId)
  if (!listener) return
  switch (msg.type) {
    case 'searchBatch': {
      const prev = _pendingBatches.get(searchId) ?? Promise.resolve()
      const next = prev.then(() => listener.onBatch((msg.results as FullTextSearchResult[]) ?? []))
      _pendingBatches.set(searchId, next)
      break
    }
    case 'searchComplete':
      ;(_pendingBatches.get(searchId) ?? Promise.resolve())
        .then(() => {
          _pendingBatches.delete(searchId)
          _searchListeners.delete(searchId)
          return listener.onComplete()
        })
        .catch((err) => console.error('[useFullTextSearch] onComplete failed:', err))
      break
    case 'searchCancelled':
      _pendingBatches.delete(searchId)
      listener.onCancelled()
      _searchListeners.delete(searchId)
      break
    case 'searchError':
      _pendingBatches.delete(searchId)
      listener.onError(
        (msg.failReason as SearchFailReason) ?? 'searchFailed',
        (msg.errorMessage as string | undefined) ?? (msg.error as string | undefined),
      )
      _searchListeners.delete(searchId)
      break
  }
})

// FTS results are never capped, so the line-id list handed to enrichment can be
// huge (tens of thousands). SQLite caps parameters per statement (~32766), so a
// single IN(...) query silently returns nothing past that — split into chunks.
const ENRICH_CHUNK = 20000
async function chunkByLineIds<T>(ids: number[], fn: (chunk: number[]) => Promise<T[]>): Promise<T[]> {
  if (ids.length <= ENRICH_CHUNK) return fn(ids)
  const out: T[] = []
  for (let i = 0; i < ids.length; i += ENRICH_CHUNK) out.push(...(await fn(ids.slice(i, i + ENRICH_CHUNK))))
  return out
}

async function enrichTocPaths(batch: FullTextSearchResult[]): Promise<void> {
  const lineIds = [...new Set(batch.map((r) => r.lineId))]
  if (!lineIds.length) return
  try {
    const rows = await chunkByLineIds(lineIds, getTocPathsForLines)
    const dataMap = new Map(rows.map((r) => [r.lineId, { bookId: r.bookId, tocPath: r.tocPath }]))
    for (const r of batch) {
      const data = dataMap.get(r.lineId)
      if (data) {
        r.bookId = data.bookId
        r.tocText = data.tocPath
      }
    }

    // Fallback for lines with no line_toc entry (e.g. custom books with negative IDs).
    // The TOC path query joins through line_toc → tocEntry and returns nothing for
    // such lines, leaving bookId as 0. Fetch bookId directly from the line table.
    const unenrichedIds = batch.filter((r) => !r.bookId).map((r) => r.lineId)
    if (unenrichedIds.length > 0) {
      const fallbackRows = await chunkByLineIds(unenrichedIds, getBookIdsForLines)
      const fallbackMap = new Map(fallbackRows.map((r) => [r.lineId, r.bookId]))
      for (const r of batch) {
        if (r.bookId === 0) {
          const bookId = fallbackMap.get(r.lineId)
          if (bookId != null) r.bookId = bookId
        }
      }
    }
  } catch (err) {
    console.error('[useFullTextSearch] enrichTocPaths failed:', err)
  }
}

export function useFullTextSearch(isIndexing?: () => boolean) {
  const cache = useSearchCacheStore()
  const settings = useSettingsStore()
  const booksStore = useBooksDataStore()
  const { searchMaxWordDistance, searchRequireOrdered, searchExpandKetiv, searchGrammarWrap } = storeToRefs(settings)

  // Sort order is per-search and ephemeral — NOT persisted. Every new search resets it to
  // 'lineId' (original streamed order), so results always finish in document order and the
  // user must actively pick 'relevance' from the toggle (which is only shown once streaming
  // completes) to reorder. See executeSearch (reset) and the watch below (user-driven re-sort).
  const sortOrder = ref<FullTextSearchSortOrder>('lineId')

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
      `ctx${settings.searchContextMarginWords}`,
    ]
      .filter(Boolean)
      .join('|')
  }  const results = shallowRef<FullTextSearchResult[]>([])
  const isSearching = ref(false)
  const hasSearched = ref(false)
  const executedQuery = ref('')
  const searchError = ref<SearchFailReason | null>(null)
  let currentSearchId: string | null = null
  let resultsReadyResolve: (() => void) | null = null
  // Dev: the in-flight ftsSearchStream — aborting it closes the connection, which is
  // the service's cancel signal (no cancel op, no polling).
  let _devStreamAbort: AbortController | null = null

  // Accumulation buffer — batches are held here and flushed to `results` at most
  // every FLUSH_INTERVAL_MS. This prevents Vue from re-rendering on every C#
  // batch when the result set is large.
  const FLUSH_INTERVAL_MS = 150
  let _pendingBuffer: FullTextSearchResult[] = []
  let _flushTimer: ReturnType<typeof setTimeout> | null = null

  // Safety timeout — if the C# search stream goes silent (no complete/error/cancel
  // event arrives within this window), we assume the search thread crashed or the
  // bridge dropped the message and reset to a recoverable state automatically.
  const SEARCH_TIMEOUT_MS = 60_000
  let _searchTimeoutTimer: ReturnType<typeof setTimeout> | null = null

  function _scheduleFlush() {
    if (_flushTimer !== null) return
    _flushTimer = setTimeout(() => {
      _flushTimer = null
      if (_pendingBuffer.length === 0) return
      const flushed = _pendingBuffer
      _pendingBuffer = []
      results.value = [...results.value, ...flushed]
    }, FLUSH_INTERVAL_MS)
  }

  function _flushNow() {
    if (_flushTimer !== null) {
      clearTimeout(_flushTimer)
      _flushTimer = null
    }
    if (_pendingBuffer.length > 0) {
      const flushed = _pendingBuffer
      _pendingBuffer = []
      results.value = [...results.value, ...flushed]
    }
  }

  // Apply the current relevancy sort to the fully-collected result set. Called only
  // once the search has completed — sorting mid-stream would fight the incremental
  // append (results paint in ascending line-ID order as they arrive). 'lineId' is the
  // natural streamed order, so it's a no-op; 'relevance' orders by word-distance score
  // (smaller = closer) with line ID as a stable tiebreaker. results is a shallowRef, so
  // we always assign a fresh array to trigger reactivity.
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
    if (sortOrder.value === 'relevance') {
      // Sole relevancy key: minimum word distance (0 = query words adjacent → most relevant).
      // Ties keep their original streamed (line-ID) order. We deliberately do NOT tiebreak on
      // `score` (character span) — that reorders equally-adjacent hits by irrelevant length
      // differences (nikud, word length), which reads as nonsense to the user.
      // `?? 0` guards results restored from a cache written before wordDistance existed on
      // the wire — treat a missing distance as "closest" rather than sorting to NaN.
      const wd = (r: FullTextSearchResult) => r.wordDistance ?? 0
      sorted.sort((a, b) => wd(a) - wd(b) || a.lineId - b.lineId)
    } else if (sortOrder.value === 'bookName') {
      // Alphabetical by book title (Hebrew collation), then line ID so lines within the
      // same book stay in document order.
      sorted.sort((a, b) => (a.bookTitle ?? '').localeCompare(b.bookTitle ?? '', 'he') || a.lineId - b.lineId)
    } else if (sortOrder.value === 'authorName') {
      // Alphabetical by author name (Hebrew collation), then book name, then line ID.
      // Authorless books (empty author string) sort last. book.authors is the comma-joined
      // author list; we collate on it as-is (its natural first author leads the string).
      const bookMap = booksStore.allBooksMap
      const authorOf = (r: FullTextSearchResult) => bookMap.get(r.bookId)?.authors || ''
      sorted.sort((a, b) => {
        const aa = authorOf(a), ab = authorOf(b)
        if (!aa !== !ab) return aa ? -1 : 1 // non-empty authors before authorless
        return aa.localeCompare(ab, 'he') ||
          (a.bookTitle ?? '').localeCompare(b.bookTitle ?? '', 'he') ||
          a.lineId - b.lineId
      })
    } else if (sortOrder.value === 'chronological') {
      // Era rank (from the book's period) → author death-year within the era (where the
      // author is in the curated map) → book name → line ID. Books whose author year is
      // unknown sort after dated books of the same era (so an era's dated works lead,
      // undated ones trail), then alphabetically.
      const bookMap = booksStore.allBooksMap
      const rankOf = (r: FullTextSearchResult) => eraRank(bookMap.get(r.bookId)?.period)
      const yearOf = (r: FullTextSearchResult) =>
        authorYear(bookMap.get(r.bookId)?.authors) ?? Number.POSITIVE_INFINITY
      sorted.sort((a, b) =>
        rankOf(a) - rankOf(b) ||
        yearOf(a) - yearOf(b) ||
        (a.bookTitle ?? '').localeCompare(b.bookTitle ?? '', 'he') ||
        a.lineId - b.lineId)
    } else {
      // 'lineId' — restore the original streamed order (ascending line ID). This is a
      // no-op right after streaming, but matters when the user switches back from
      // another order on an already-sorted set.
      sorted.sort((a, b) => a.lineId - b.lineId)
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

  function _startSearchTimeout(searchId: string) {
    _clearSearchTimeout()
    _searchTimeoutTimer = setTimeout(() => {
      _searchTimeoutTimer = null
      // Only reset if this specific search is still in-flight
      if (currentSearchId !== searchId) return
      console.warn('[useFullTextSearch] Search timed out — resetting state for recovery')
      _flushNow()
      isSearching.value = false
      searchError.value = 'searchFailed'
      _cleanup()
    }, SEARCH_TIMEOUT_MS)
  }

  function _clearSearchTimeout() {
    if (_searchTimeoutTimer !== null) {
      clearTimeout(_searchTimeoutTimer)
      _searchTimeoutTimer = null
    }
  }

  function _cleanup() {
    _clearSearchTimeout()
    if (_flushTimer !== null) {
      clearTimeout(_flushTimer)
      _flushTimer = null
    }
    _pendingBuffer = []
    if (currentSearchId) {
      _searchListeners.delete(currentSearchId)
      _pendingBatches.delete(currentSearchId)
      currentSearchId = null
    }
  }

  async function cancelSearch() {
    // Dev: closing the stream IS the cancel — the service sees the broken pipe.
    if (_devStreamAbort) {
      _devStreamAbort.abort()
      _devStreamAbort = null
      isSearching.value = false
    }
    if (!currentSearchId) return
    const id = currentSearchId
    _cleanup()
    isSearching.value = false
    try {
      await callBridgeAction('FtsSearchCancel', id)
    } catch {
      /* ignore */
    }
  }

  // Start the C# search stream and wire up listeners.
  // skipCount: number of results already in cache — C# will skip that many before streaming.
  async function _startStream(normalizedQuery: string, skipCount: number, cacheKey?: string) {
    const reply = await callBridgeAction<{ searchId: string; failReason: SearchFailReason | null }>(
      'FtsSearchStart',
      normalizedQuery,
      skipCount,
      settings.searchMaxWordDistance,
      settings.searchRequireOrdered,
      settings.searchContextMarginWords,
      settings.searchExpandKetiv,
    )
    const searchId = reply?.searchId
    if (!searchId) {
      searchError.value = reply?.failReason ?? 'indexNotReady'
      isSearching.value = false
      return
    }
    currentSearchId = searchId
    _startSearchTimeout(searchId)

    // Create a promise that resolves when the first batch arrives
    let firstBatchReady = false
    const resultsReady = new Promise<void>((resolve) => {
      resultsReadyResolve = () => {
        if (!firstBatchReady) {
          firstBatchReady = true
          resolve()
        }
      }
    })

    _searchListeners.set(searchId, {
      onBatch: async (batch) => {
        if (currentSearchId !== searchId) return
        resultsReadyResolve?.()
        _startSearchTimeout(searchId) // reset the watchdog on every batch
        await enrichTocPaths(batch)
        // Re-check after the async enrichment — currentSearchId may have changed
        // while enrichTocPaths was awaiting the SQL query.
        if (currentSearchId !== searchId) return
        _pendingBuffer.push(...batch)
        _scheduleFlush()
        // Only persist to IDB when the index is fully built — partial results
        // from a mid-build search would be cached as complete and served stale.
        if (!isIndexing?.()) {
          try {
            await cache.appendBatch(cacheKey ?? normalizedQuery, batch)
          } catch {
            /* non-fatal — cache is best-effort */
          }
        }
      },
      onComplete: async () => {
        if (currentSearchId !== searchId) return
        _flushNow()
        await finalizeSort()
        isSearching.value = false
        if (!isIndexing?.()) {
          try {
            await cache.markComplete(cacheKey ?? normalizedQuery, false)
          } catch {
            /* non-fatal */
          }
        }
        _cleanup()
      },
      onCancelled: () => {
        if (currentSearchId !== searchId) return
        isSearching.value = false
        _cleanup()
      },
      onError: (reason, detail) => {
        console.error('[useFullTextSearch] search error:', reason, ...(detail ? [detail] : []))
        if (currentSearchId !== searchId) return
        _flushNow()
        searchError.value = reason
        isSearching.value = false
        // Delete the partial cache entry so it is never served as a resumable
        // result on the next session. The stream didn't complete, so whatever
        // was written by appendBatch is an incomplete and potentially corrupt
        // snapshot (e.g. index was merging mid-stream).
        if (cacheKey) {
          cache.remove(cacheKey).catch(() => {/* non-fatal */})
        }
        _cleanup()
      },
    })

    // Wait for the first batch to arrive so results are ready before returning
    await resultsReady
  }

  // How many times to auto-retry on indexMerging before giving up
  const MERGE_RETRY_DELAY_MS = 1500
  const MERGE_RETRY_MAX = 4

  async function executeSearch(q: string, _mergeRetryCount = 0) {
    if (!q.trim()) return

    if (currentSearchId) await cancelSearch()

    isSearching.value = true
    hasSearched.value = true
    results.value = []
    sortOrder.value = 'lineId'   // each new search starts in original order; user re-sorts after
    searchError.value = null
    executedQuery.value = q

    // Dev path — no C# bridge in the browser; the service PUSHES result frames
    // continuously over one connection (ftsSearchStream) until the search finishes.
    // No polling: aborting the stream is the cancel signal, and the service itself
    // supersedes the previous search when a new one starts.
    if (!isHosted || typeof window.__webviewAction !== 'function') {
      const normalizedQuery = q.trim().toLowerCase()
      // Abort the previous stream (if any) and own a fresh controller for this one.
      _devStreamAbort?.abort()
      const abort = new AbortController()
      _devStreamAbort = abort
      try {
        const acc: FullTextSearchResult[] = []
        const stream = serviceStream<{
          ready: boolean
          results: FullTextSearchResult[]
          done: boolean
          error?: string
        }>('ftsSearchStream', {
          query: _buildQueryToSend(normalizedQuery),
          maxWordDistance: settings.searchMaxWordDistance,
          requireOrdered: settings.searchRequireOrdered,
          contextWords: settings.searchContextMarginWords,
          expandKetiv: settings.searchExpandKetiv,
        }, abort.signal)

        for await (const chunk of stream) {
          if (executedQuery.value !== q) { abort.abort(); return } // superseded locally
          if (chunk.ready === false) {
            searchError.value = 'indexNotReady'
            return
          }
          const hits = chunk.results ?? []
          if (hits.length) {
            // Hits arrive fully enriched (bookId + tocText) from the service — no
            // client-side enrichment round-trip.
            for (const c of hits) acc.push(c) // spread-push would overflow on very large chunks
            results.value = acc.slice() // one flush per pushed frame — the service paces the cadence
          }
          if (chunk.error) {
            if (!acc.length) searchError.value = 'searchFailed'
            break
          }
          if (chunk.done) {
            // Search complete — apply the sort now (no-op for 'lineId').
            // Done here rather than in `finally` so it runs only on genuine completion,
            // not when the loop exits early via supersession/error.
            await finalizeSort()
            break
          }
        }
      } catch (err) {
        // An aborted stream is a cancel (new search / navigation), not a failure.
        if (!abort.signal.aborted) {
          console.error('[useFullTextSearch] dev FTS failed:', err)
          searchError.value = 'searchFailed'
        }
      } finally {
        if (_devStreamAbort === abort) _devStreamAbort = null
        if (executedQuery.value === q) isSearching.value = false
      }
      return
    }

    const normalizedQuery = q.trim().toLowerCase()

    // Always run a fresh search — the cache is only used for session restore
    // and tab switching (see loadCachedResults), never for a user-initiated search.
    try {
      const cacheKey = _buildCacheKey(normalizedQuery)
      if (!isIndexing?.()) await cache.init(cacheKey, q, false)
      await _startStream(_buildQueryToSend(normalizedQuery), 0, cacheKey)
    } catch (err) {
      console.error('[useFullTextSearch] failed to start search:', err)
      isSearching.value = false
    }

    // Auto-retry on indexMerging — the merge is transient and typically resolves
    // within a few seconds. Retry up to MERGE_RETRY_MAX times with a short delay.
    if (searchError.value === 'indexMerging' && _mergeRetryCount < MERGE_RETRY_MAX) {
      console.warn(
        `[useFullTextSearch] indexMerging — retrying in ${MERGE_RETRY_DELAY_MS}ms (attempt ${_mergeRetryCount + 1}/${MERGE_RETRY_MAX})`,
      )
      searchError.value = null
      isSearching.value = true
      await new Promise((resolve) => setTimeout(resolve, MERGE_RETRY_DELAY_MS))
      // Check the query hasn't changed while we were waiting
      if (executedQuery.value === q) {
        await executeSearch(q, _mergeRetryCount + 1)
      }
    }
  }

  function clearSearch() {
    results.value = []
    hasSearched.value = false
    executedQuery.value = ''
    searchError.value = null
  }

  function clearCachedResults(q: string): void {
    const normalizedQuery = q.trim().toLowerCase()
    cache.remove(_buildCacheKey(normalizedQuery)).catch(() => {/* non-fatal */})
  }

  async function loadCachedResults(q: string): Promise<boolean> {
    if (isIndexing?.()) return false
    const normalizedQuery = q.trim().toLowerCase()
    const cacheKey = _buildCacheKey(normalizedQuery)
    const cached = await cache.get(cacheKey, q)
    if (!cached || cached.results.length === 0) return false
    results.value = cached.results
    executedQuery.value = q
    hasSearched.value = true
    if (!cached.complete) {
      isSearching.value = true
      _startStream(_buildQueryToSend(normalizedQuery), cached.results.length, cacheKey).catch((err) => {
        console.error('[useFullTextSearch] failed to resume stream after tab restore:', err)
        isSearching.value = false
      })
    }
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
    grammarWrap: searchGrammarWrap,
    sortOrder,
    executeSearch,
    cancelSearch,
    clearSearch,
    clearCachedResults,
    loadCachedResults,
  }
}
