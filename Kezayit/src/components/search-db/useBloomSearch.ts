/**
 * Bloom search composable — wraps C# streaming search.
 *
 * C# sends search stream events via PostWebMessageAsString → chrome.webview message events.
 * C# sends indexing progress via ExecuteScriptAsync → window.__onWebviewEvent.
 *
 * Falls back to sample data in dev when the C# host is not present.
 */
import { ref } from 'vue'
import { isHosted } from '@/host/db'
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import type { BloomSearchResult } from './searchTypes'

const DEV_SAMPLES: BloomSearchResult[] = [
  {
    lineId: 1,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק א',
    score: 0.95,
    proximityScore: 0.9,
    snippet: 'בראשית ברא אלקים את השמים ואת הארץ',
  },
  {
    lineId: 2,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק א',
    score: 0.92,
    proximityScore: 0.88,
    snippet: 'והארץ היתה תהו ובהו וחשך על פני תהום',
  },
  {
    lineId: 3,
    bookId: 2,
    bookTitle: 'שמות',
    tocText: 'פרק א',
    score: 0.88,
    proximityScore: 0.85,
    snippet: 'ואלה שמות בני ישראל הבאים מצרימה',
  },
  {
    lineId: 4,
    bookId: 3,
    bookTitle: 'ויקרא',
    tocText: 'פרק א',
    score: 0.85,
    proximityScore: 0.82,
    snippet: 'ויקרא אל משה וידבר אליו מאהל מועד',
  },
  {
    lineId: 5,
    bookId: 1,
    bookTitle: 'בראשית',
    tocText: 'פרק ב',
    score: 0.82,
    proximityScore: 0.8,
    snippet: 'ויכלו השמים והארץ וכל צבאם',
  },
  {
    lineId: 6,
    bookId: 4,
    bookTitle: 'משנה תורה להרמב"ם - ספר המדע',
    tocText: 'הלכות יסודי התורה › פרק ראשון › הלכה א',
    score: 0.78,
    proximityScore: 0.75,
    snippet: 'יסוד היסודות ועמוד החכמות לידע שיש שם מצוי ראשון',
  },
  {
    lineId: 7,
    bookId: 5,
    bookTitle: 'שולחן ערוך עם כל הנושאי כלים - אורח חיים',
    tocText: 'סימן א › סעיף א',
    score: 0.75,
    proximityScore: 0.72,
    snippet: 'יתגבר כארי לעמוד בבוקר לעבודת בוראו שיהא הוא מעורר השחר',
  },
  {
    lineId: 8,
    bookId: 6,
    bookTitle: 'תלמוד בבלי - מסכת ברכות',
    tocText: 'פרק ראשון - מאימתי › דף ב עמוד א',
    score: 0.72,
    proximityScore: 0.7,
    snippet: 'מאימתי קורין את שמע בערבין משעה שהכהנים נכנסים לאכול בתרומתן',
  },
]

function callAction<T>(name: string, ...params: unknown[]): Promise<T> {
  if (typeof window.__webviewAction !== 'function')
    return Promise.reject(new Error('bridge not available'))
  // The new project's bridge uses positional params array, not named args object
  return window.__webviewAction(name, params as unknown as object) as Promise<T>
}

// ── chrome.webview message listener (search stream events from C#) ────────────
// C# sends search events via PostWebMessageAsString → chrome.webview message events.
// We maintain a single shared listener and route by searchId.

type SearchListeners = {
  onBatch: (results: BloomSearchResult[]) => void
  onComplete: () => void
  onCancelled: () => void
  onError: (err: string) => void
}

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
        case 'searchBatch':
          listener.onBatch(msg.results ?? [])
          break
        case 'searchComplete':
          listener.onComplete()
          _searchListeners.delete(msg.searchId)
          break
        case 'searchCancelled':
          listener.onCancelled()
          _searchListeners.delete(msg.searchId)
          break
        case 'searchError':
          listener.onError(msg.error ?? '')
          _searchListeners.delete(msg.searchId)
          break
      }
    } catch {
      /* ignore malformed messages */
    }
  })
}

async function enrichTocPaths(batch: BloomSearchResult[]): Promise<void> {
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
    console.error('[useBloomSearch] enrichTocPaths failed:', err)
  }
}

export function useBloomSearch() {
  const cache = useSearchCacheStore()
  const results = ref<BloomSearchResult[]>([])
  const isSearching = ref(false)
  const hasSearched = ref(false)
  const executedQuery = ref('')

  let currentSearchId: string | null = null

  function _cleanup() {
    if (currentSearchId) {
      _searchListeners.delete(currentSearchId)
      currentSearchId = null
    }
  }

  async function cancelSearch() {
    if (!currentSearchId) return
    const id = currentSearchId
    _cleanup()
    isSearching.value = false
    try {
      await callAction('BloomSearchCancel', id)
    } catch {
      /* ignore */
    }
  }

  async function executeSearch(query: string) {
    if (!query.trim()) return

    if (currentSearchId) await cancelSearch()

    isSearching.value = true
    hasSearched.value = true
    results.value = []
    executedQuery.value = query

    // Dev fallback — bridge not available in browser dev
    if (!isHosted || typeof window.__webviewAction !== 'function') {
      await new Promise((r) => setTimeout(r, 400))
      results.value = DEV_SAMPLES
      isSearching.value = false
      return
    }

    // Cache check
    const cached = await cache.get(query.trim().toLowerCase())
    if (cached) {
      console.log('[useBloomSearch] cache hit for:', query)
      results.value = cached
      isSearching.value = false
      return
    }
    console.log('[useBloomSearch] cache miss for:', query)

    try {
      ensureWebviewListener()
      const reply = await callAction<{ searchId: string }>('BloomSearchStart', query)
      const searchId = reply?.searchId
      if (!searchId) {
        // Index not ready — caller should check indexing status
        isSearching.value = false
        return
      }
      currentSearchId = searchId

      _searchListeners.set(searchId, {
        onBatch: async (batch) => {
          if (currentSearchId === searchId) {
            await enrichTocPaths(batch)
            results.value = [...results.value, ...batch]
          }
        },
        onComplete: () => {
          if (currentSearchId === searchId) {
            isSearching.value = false
            if (results.value.length > 0)
              cache
                .set(query.trim().toLowerCase(), results.value)
                .catch((err) => console.error('[useBloomSearch] cacheSet failed:', err))
            _cleanup()
          }
        },
        onCancelled: () => {
          if (currentSearchId === searchId) {
            isSearching.value = false
            _cleanup()
          }
        },
        onError: (err) => {
          console.error('[useBloomSearch] search error:', err)
          if (currentSearchId === searchId) {
            isSearching.value = false
            _cleanup()
          }
        },
      })
    } catch (err) {
      console.error('[useBloomSearch] failed to start search:', err)
      isSearching.value = false
    }
  }

  function clearSearch() {
    results.value = []
    hasSearched.value = false
    executedQuery.value = ''
  }

  async function loadCachedResults(query: string): Promise<boolean> {
    const cached = await cache.get(query.trim().toLowerCase())
    if (!cached) return false
    results.value = cached
    executedQuery.value = query
    hasSearched.value = true
    return true
  }

  return {
    results,
    isSearching,
    hasSearched,
    executedQuery,
    executeSearch,
    cancelSearch,
    clearSearch,
    loadCachedResults,
  }
}
