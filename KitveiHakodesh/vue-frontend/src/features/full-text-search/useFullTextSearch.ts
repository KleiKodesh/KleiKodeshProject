/**
 * Full-text search composable — wraps C# streaming search (FtsLib backend).
 *
 * C# sends search stream events (searchBatch, searchComplete, etc.) via
 * PostWebMessageAsJson → JsBridge → window.__onWebviewEvent → onWebviewEvent().
 * C# sends indexing progress the same way via the ftsIndexProgress event.
 *
 * Falls back to sample data in dev when the C# host is not present.
 */
import { ref } from 'vue'
import { storeToRefs } from 'pinia'
import { isHosted, query, onWebviewEvent } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import { callBridgeAction } from '@/webview-host/bridge'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { useSettingsStore } from '@/stores/settingsStore'
import type { FullTextSearchResult, SearchFailReason } from './fullTextSearchTypes'

const DEV_SAMPLES: FullTextSearchResult[] = [
  {
    lineId: 1,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק א',
    score: 0,
    snippet: 'בראשית ברא אלקים את השמים ואת הארץ',
    matchedTerms: [],
  },
  {
    lineId: 2,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק א',
    score: 0,
    snippet: 'והארץ היתה תהו ובהו וחשך על פני תהום',
    matchedTerms: [],
  },
  {
    lineId: 3,
    bookId: 2,
    bookTitle: 'שמות',
    tocText: 'פרק א',
    score: 0,
    snippet: 'ואלה שמות בני ישראל הבאים מצרימה',
    matchedTerms: [],
  },
  {
    lineId: 4,
    bookId: 3,
    bookTitle: 'ויקרא',
    tocText: 'פרק א',
    score: 0,
    snippet: 'ויקרא אל משה וידבר אליו מאהל מועד',
    matchedTerms: [],
  },
  {
    lineId: 5,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק ב',
    score: 0,
    snippet: 'ויכלו השמים והארץ וכל צבאם',
    matchedTerms: [],
  },
  {
    lineId: 6,
    bookId: 4,
    bookTitle: 'משנה תורה להרמב"ם - ספר המדע',
    tocText: 'הלכות יסודי התורה › פרק ראשון › הלכה א',
    score: 0,
    snippet: 'יסוד היסודות ועמוד החכמות לידע שיש שם מצוי ראשון',
    matchedTerms: [],
  },
  {
    lineId: 7,
    bookId: 5,
    bookTitle: 'שולחן ערוך עם כל הנושאי כלים - אורח חיים',
    tocText: 'סימן א › סעיף א',
    score: 0,
    snippet: 'יתגבר כארי לעמוד בבוקר לעבודת בוראו שיהא הוא מעורר השחר',
    matchedTerms: [],
  },
  {
    lineId: 8,
    bookId: 6,
    bookTitle: 'תלמוד בבלי - מסכת ברכות',
    tocText: 'פרק ראשון - מאימתי › דף ב עמוד א',
    score: 0,
    snippet: 'מאימתי קורין את שמע בערבין משעה שהכהנים נכנסים לאכול בתרומתן',
    matchedTerms: [],
  },
]

// ── chrome.webview message listener (search stream events from C#) ────────────
// C# sends search events via window.__onWebviewEvent (routed through JsBridge).
// We maintain a single shared listener and route by searchId.

type SearchListeners = {
  onBatch: (results: FullTextSearchResult[]) => Promise<void>
  onComplete: () => Promise<void>
  onCancelled: () => void
  onError: (reason: SearchFailReason) => void
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
      listener.onError((msg.failReason as SearchFailReason) ?? 'searchFailed')
      _searchListeners.delete(searchId)
      break
  }
})

async function enrichTocPaths(batch: FullTextSearchResult[]): Promise<void> {
  const lineIds = [...new Set(batch.map((r) => r.lineId))]
  if (!lineIds.length) return
  try {
    const rows = await query<{ lineId: number; bookId: number; tocPath: string }>(
      SQL.GET_TOC_PATHS_FOR_LINES(lineIds.length),
      lineIds,
    )
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
    const unenrichedIds = batch.filter((r) => r.bookId === 0).map((r) => r.lineId)
    if (unenrichedIds.length > 0) {
      const fallbackRows = await query<{ lineId: number; bookId: number }>(
        SQL.GET_BOOK_IDS_FOR_LINES(unenrichedIds.length),
        unenrichedIds,
      )
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
  const { searchMaxWordDistance, searchRequireOrdered, searchExpandKetiv, searchGrammarWrap } = storeToRefs(settings)

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
  }  const results = ref<FullTextSearchResult[]>([])
  const isSearching = ref(false)
  const hasSearched = ref(false)
  const executedQuery = ref('')
  const searchError = ref<SearchFailReason | null>(null)
  let currentSearchId: string | null = null
  let resultsReadyResolve: (() => void) | null = null

  function _cleanup() {
    if (currentSearchId) {
      _searchListeners.delete(currentSearchId)
      _pendingBatches.delete(currentSearchId)
      currentSearchId = null
    }
  }

  async function cancelSearch() {
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
  async function _startStream(normalizedQuery: string, skipCount: number) {
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
        await enrichTocPaths(batch)
        // Re-check after the async enrichment — currentSearchId may have changed
        // while enrichTocPaths was awaiting the SQL query.
        if (currentSearchId !== searchId) return
        results.value = [...results.value, ...batch]
        // Only persist to IDB when the index is fully built — partial results
        // from a mid-build search would be cached as complete and served stale.
        if (!isIndexing?.()) {
          try {
            await cache.appendBatch(normalizedQuery, JSON.parse(JSON.stringify(batch)))
          } catch {
            /* non-fatal — cache is best-effort */
          }
        }
      },
      onComplete: async () => {
        if (currentSearchId !== searchId) return
        isSearching.value = false
        if (!isIndexing?.()) {
          try {
            await cache.markComplete(normalizedQuery, false)
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
      onError: (reason) => {
        console.error('[useFullTextSearch] search error:', reason)
        if (currentSearchId !== searchId) return
        searchError.value = reason
        isSearching.value = false
        _cleanup()
      },
    })

    // Wait for the first batch to arrive so results are ready before returning
    await resultsReady
  }

  async function executeSearch(q: string) {
    if (!q.trim()) return

    if (currentSearchId) await cancelSearch()

    isSearching.value = true
    hasSearched.value = true
    results.value = []
    searchError.value = null
    executedQuery.value = q

    // Dev fallback — bridge not available in browser dev
    if (!isHosted || typeof window.__webviewAction !== 'function') {
      await new Promise((r) => setTimeout(r, 400))
      results.value = DEV_SAMPLES
      isSearching.value = false
      return
    }

    const normalizedQuery = q.trim().toLowerCase()

    // Always run a fresh search — the cache is only used for session restore
    // and tab switching (see loadCachedResults), never for a user-initiated search.
    try {
      if (!isIndexing?.()) await cache.init(normalizedQuery, normalizedQuery, false)
      await _startStream(_buildQueryToSend(normalizedQuery), 0)
    } catch (err) {
      console.error('[useFullTextSearch] failed to start search:', err)
      isSearching.value = false
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
    // Fire-and-forget — tab is closing, no need to await
    cache.remove(normalizedQuery).catch(() => {/* non-fatal */})
  }

  async function loadCachedResults(q: string): Promise<boolean> {
    // Don't restore from cache while indexing — the cached results are partial
    // and would be served as if they were complete.
    if (isIndexing?.()) return false
    const normalizedQuery = q.trim().toLowerCase()
    const cached = await cache.get(normalizedQuery, normalizedQuery)
    if (!cached || cached.results.length === 0) return false
    results.value = cached.results
    executedQuery.value = q
    hasSearched.value = true
    // If incomplete, resume streaming in the background
    if (!cached.complete) {
      isSearching.value = true
      _startStream(normalizedQuery, cached.results.length).catch((err) => {
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
    executeSearch,
    cancelSearch,
    clearSearch,
    clearCachedResults,
    loadCachedResults,
  }
}
