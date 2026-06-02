/**
 * Full-text search composable — wraps C# streaming search (FtsLib backend).
 *
 * C# sends search stream events via PostWebMessageAsString → chrome.webview message events.
 * C# sends indexing progress via ExecuteScriptAsync → window.__onWebviewEvent.
 *
 * Falls back to sample data in dev when the C# host is not present.
 */
import { ref } from 'vue'
import { storeToRefs } from 'pinia'
import { isHosted, query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import { callBridgeAction } from '@/webview-host/bridge'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { expandKetivHaser } from '@/utils/hebrewKetivExpander'
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
// C# sends search events via PostWebMessageAsString → chrome.webview message events.
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
let _webviewListenerSetup = false

function ensureWebviewListener() {
  if (_webviewListenerSetup || !isHosted) return
  _webviewListenerSetup = true
  const wv = (window as any).chrome?.webview
  if (!wv) return
  wv.addEventListener('message', (event: MessageEvent) => {
    try {
      const msg = typeof event.data === 'string' ? JSON.parse(event.data) : event.data
      if (!msg?.type || !msg?.searchId) return
      const listener = _searchListeners.get(msg.searchId)
      if (!listener) return
      switch (msg.type) {
        case 'searchBatch': {
          // Chain onto any in-flight batch promise so enrichment is always sequential
          const prev = _pendingBatches.get(msg.searchId) ?? Promise.resolve()
          const next = prev.then(() => listener.onBatch(msg.results ?? []))
          _pendingBatches.set(msg.searchId, next)
          break
        }
        case 'searchComplete':
          // Wait for all in-flight batches to finish enrichment before completing
          ;(_pendingBatches.get(msg.searchId) ?? Promise.resolve())
            .then(() => {
              _pendingBatches.delete(msg.searchId)
              _searchListeners.delete(msg.searchId)
              return listener.onComplete()
            })
            .catch((err) => console.error('[useFullTextSearch] onComplete failed:', err))
          break
        case 'searchCancelled':
          _pendingBatches.delete(msg.searchId)
          listener.onCancelled()
          _searchListeners.delete(msg.searchId)
          break
        case 'searchError':
          _pendingBatches.delete(msg.searchId)
          listener.onError(msg.failReason ?? 'searchFailed')
          _searchListeners.delete(msg.searchId)
          break
      }
    } catch {
      /* ignore malformed messages */
    }
  })
}

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
  const { searchMaxWordDistance, searchRequireOrdered, searchExpandKetiv, searchWildcardWrap, searchGrammarWrap } = storeToRefs(settings)
  const results = ref<FullTextSearchResult[]>([])
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
  // cacheKey: the plain normalized query used as the IDB cache key.
  // queryToSend: the (possibly wildcard/grammar-wrapped) query string sent to C#.
  // excludedLineIds: lineIds already in the frontend cache — C# skips snippet
  // generation for these, so only genuinely new results are streamed back.
  // backgroundRefresh: when true the stream appends to existing results rather than
  // replacing them, and isSearching stays true only until the stream completes.
  async function _startStream(
    cacheKey: string,
    queryToSend: string,
    excludedLineIds: number[],
    backgroundRefresh = false,
  ): Promise<void> {
    ensureWebviewListener()
    const reply = await callBridgeAction<{ searchId: string; failReason: SearchFailReason | null }>(
      'FtsSearchStart',
      queryToSend,
      excludedLineIds,
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

    // For a background refresh the caller already has results to show — resolve immediately.
    // For a fresh stream resolve on the first batch so the caller can await visible results.
    let firstBatchReady = backgroundRefresh
    const resultsReady = new Promise<void>((resolve) => {
      if (backgroundRefresh) { resolve(); return }
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
        if (currentSearchId !== searchId) return
        if (backgroundRefresh) {
          // Replace results wholesale — the refresh stream starts from 0 and
          // overwrites the stale cached set with the up-to-date one.
          results.value = [...results.value, ...batch]
        } else {
          results.value = [...results.value, ...batch]
        }
        try {
          await cache.appendBatch(cacheKey, JSON.parse(JSON.stringify(batch)))
        } catch {
          /* non-fatal — cache is best-effort */
        }
      },
      onComplete: async () => {
        if (currentSearchId !== searchId) return
        isSearching.value = false
        try {
          await cache.markComplete(cacheKey, !(isIndexing?.()))
        } catch {
          /* non-fatal */
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

    await resultsReady
  }

  function _buildQueryToSend(normalizedQuery: string): string {
    if (!settings.searchWildcardWrap && !settings.searchGrammarWrap) return normalizedQuery
    return normalizedQuery
      .split(/\s+/)
      .map((word) => {
        if (!word) return word
        // Leave words that already carry an operator as-is
        if (/[*?~|%]/.test(word)) return word
        if (settings.searchWildcardWrap) return `*${word}*`
        return `%${word}%`
      })
      .join(' ')
  }

  async function executeSearch(q: string) {
    if (!q.trim()) return

    if (currentSearchId) await cancelSearch()

    hasSearched.value = true
    searchError.value = null
    executedQuery.value = q

    // Dev fallback — bridge not available in browser dev
    if (!isHosted || typeof window.__webviewAction !== 'function') {
      isSearching.value = true
      results.value = []
      await new Promise((r) => setTimeout(r, 400))
      results.value = DEV_SAMPLES
      isSearching.value = false
      return
    }

    const normalizedQuery = q.trim().toLowerCase()
    const queryToSend = _buildQueryToSend(normalizedQuery)
    const indexingNow = isIndexing?.() ?? false

    // ── Cache lookup ──────────────────────────────────────────────────────────
    // A complete cache entry written while the index was fully built is a perfect
    // hit — serve it instantly with no stream needed.
    //
    // A complete entry written during indexing may be stale (the index has grown
    // since) — serve it instantly for immediate results, then refresh in the
    // background so the cache is updated for next time.
    //
    // An incomplete entry means the previous stream was interrupted — resume it.
    const cached = await cache.get(normalizedQuery)

    if (cached && cached.results.length > 0) {
      // Serve cached results immediately so the UI is responsive
      results.value = cached.results
      isSearching.value = true

      if (cached.complete && cached.indexingComplete) {
        // Perfect hit — index was complete when this was cached, no refresh needed
        isSearching.value = false
        return
      }

      if (cached.complete && !cached.indexingComplete && !indexingNow) {
        // Was cached during indexing but index is now complete — refresh the full set
        results.value = []
        await cache.init(normalizedQuery, true)
        await _startStream(normalizedQuery, queryToSend, [], false)
        return
      }

      if (cached.complete && !cached.indexingComplete && indexingNow) {
        // Cached during indexing and still indexing — show stale results, refresh silently.
        // Do NOT wipe the cache — keep the existing entry and append only new items to it
        // so the full merged set is available next time.
        const excludedIds = cached.results.map((r) => r.lineId)
        _startStream(normalizedQuery, queryToSend, excludedIds, true).catch((err) => {
          console.error('[useFullTextSearch] background refresh failed:', err)
          isSearching.value = false
        })
        return
      }

      // Incomplete entry — resume the stream, skipping lines already cached.
      // Keep the existing cache entry and append only new items to it.
      const excludedIds = cached.results.map((r) => r.lineId)
      await _startStream(normalizedQuery, queryToSend, excludedIds, false)
      return
    }

    // ── Cache miss — fresh search ─────────────────────────────────────────────
    results.value = []
    isSearching.value = true
    try {
      await cache.init(normalizedQuery, !indexingNow)
      await _startStream(normalizedQuery, queryToSend, [], false)
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

  async function loadCachedResults(q: string): Promise<boolean> {
    const normalizedQuery = q.trim().toLowerCase()
    const cached = await cache.get(normalizedQuery)
    if (!cached || cached.results.length === 0) return false
    results.value = cached.results
    executedQuery.value = q
    hasSearched.value = true
    // If the stream was interrupted, resume it in the background
    if (!cached.complete) {
      isSearching.value = true
      const excludedIds = cached.results.map((r) => r.lineId)
      const queryToSend = _buildQueryToSend(normalizedQuery)
      _startStream(normalizedQuery, queryToSend, excludedIds, false).catch((err) => {
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
    wildcardWrap: searchWildcardWrap,
    grammarWrap: searchGrammarWrap,
    executeSearch,
    cancelSearch,
    clearSearch,
    loadCachedResults,
  }
}
