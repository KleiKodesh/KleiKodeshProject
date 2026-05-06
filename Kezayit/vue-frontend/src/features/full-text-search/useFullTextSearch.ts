/**
 * Full-text search composable — wraps C# streaming search (FtsLib backend).
 *
 * C# sends search stream events via PostWebMessageAsString → chrome.webview message events.
 * C# sends indexing progress via ExecuteScriptAsync → window.__onWebviewEvent.
 *
 * Falls back to sample data in dev when the C# host is not present.
 */
import { ref } from 'vue'
import { isHosted, query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import { callBridgeAction } from '@/webview-host/bridge'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import type { FullTextSearchResult } from './fullTextSearchTypes'

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
  onError: (err: string) => void
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
          listener.onError(msg.error ?? '')
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
    const rows = await query<{ lineId: number; tocPath: string }>(
      SQL.GET_TOC_PATHS_FOR_LINES(lineIds.length),
      lineIds,
    )
    const pathMap = new Map(rows.map((r) => [r.lineId, r.tocPath]))
    for (const r of batch) {
      const path = pathMap.get(r.lineId)
      if (path) r.tocText = path
    }
  } catch (err) {
    console.error('[useFullTextSearch] enrichTocPaths failed:', err)
  }
}

export function useFullTextSearch(isIndexing?: () => boolean) {
  const cache = useSearchCacheStore()
  const results = ref<FullTextSearchResult[]>([])
  const isSearching = ref(false)
  const hasSearched = ref(false)
  const executedQuery = ref('')
  const maxWordDistance = ref(10)
  const requireOrdered = ref(false)

  let currentSearchId: string | null = null

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
    ensureWebviewListener()
    const reply = await callBridgeAction<{ searchId: string }>(
      'FtsSearchStart',
      normalizedQuery,
      skipCount,
      maxWordDistance.value,
      requireOrdered.value,
    )
    const searchId = reply?.searchId
    if (!searchId) {
      // Index not ready
      isSearching.value = false
      return
    }
    currentSearchId = searchId

    _searchListeners.set(searchId, {
      onBatch: async (batch) => {
        if (currentSearchId !== searchId) return
        await enrichTocPaths(batch)
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
            await cache.markComplete(normalizedQuery)
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
      onError: (err) => {
        console.error('[useFullTextSearch] search error:', err)
        if (currentSearchId !== searchId) return
        isSearching.value = false
        _cleanup()
      },
    })
  }

  async function executeSearch(q: string) {
    if (!q.trim()) return

    if (currentSearchId) await cancelSearch()

    isSearching.value = true
    hasSearched.value = true
    results.value = []
    executedQuery.value = q

    // Dev fallback — bridge not available in browser dev
    if (!isHosted || typeof window.__webviewAction !== 'function') {
      await new Promise((r) => setTimeout(r, 400))
      results.value = DEV_SAMPLES
      isSearching.value = false
      return
    }

    const normalizedQuery = q.trim().toLowerCase()

    // Skip cache entirely while the index is still building — cached results
    // would be partial and would be served as complete on the next search.
    if (!isIndexing?.()) {
      const cached = await cache.get(normalizedQuery)

      if (cached?.complete) {
        // Full result set available — show immediately, no stream needed
        results.value = cached.results
        isSearching.value = false
        return
      }

      if (cached && cached.results.length > 0) {
        // Partial result set from a previous interrupted search — show what we have,
        // then resume streaming from where C# left off
        results.value = cached.results
        try {
          await _startStream(normalizedQuery, cached.results.length)
        } catch (err) {
          console.error('[useFullTextSearch] failed to resume stream:', err)
          isSearching.value = false
        }
        return
      }
    }

    // No cache (or indexing in progress) — fresh search
    try {
      if (!isIndexing?.()) await cache.init(normalizedQuery)
      await _startStream(normalizedQuery, 0)
    } catch (err) {
      console.error('[useFullTextSearch] failed to start search:', err)
      isSearching.value = false
    }
  }

  function clearSearch() {
    results.value = []
    hasSearched.value = false
    executedQuery.value = ''
  }

  async function loadCachedResults(q: string): Promise<boolean> {
    // Don't restore from cache while indexing — the cached results are partial
    // and would be served as if they were complete.
    if (isIndexing?.()) return false
    const normalizedQuery = q.trim().toLowerCase()
    const cached = await cache.get(normalizedQuery)
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
    maxWordDistance,
    requireOrdered,
    executeSearch,
    cancelSearch,
    clearSearch,
    loadCachedResults,
  }
}
