/**
 * Bloom search composable — wraps C# streaming search via the webview event bus.
 *
 * The new project's bridge uses:
 *   window.__webviewAction('BloomSearchStart', { query }) → searchId
 *   window.__webviewAction('BloomSearchCancel', { searchId })
 *   window.__onWebviewEvent fires with { type, searchId, results?, error? }
 *
 * Falls back to sample data in dev when the C# host is not present.
 */
import { ref } from 'vue'
import { onWebviewEvent } from '@/host/db'
import { isHosted } from '@/host/db'
import { cacheGet, cacheSet } from './searchCache'
import type { BloomSearchResult } from './searchTypes'

const DEV_SAMPLES: BloomSearchResult[] = [
  { lineId: 1, bookId: 1, bookTitle: 'בראשית', tocText: 'פרק א', score: 0.95, proximityScore: 0.9,  snippet: 'בראשית ברא אלקים את השמים ואת הארץ' },
  { lineId: 2, bookId: 1, bookTitle: 'בראשית', tocText: 'פרק א', score: 0.92, proximityScore: 0.88, snippet: 'והארץ היתה תהו ובהו וחשך על פני תהום' },
  { lineId: 3, bookId: 2, bookTitle: 'שמות',   tocText: 'פרק א', score: 0.88, proximityScore: 0.85, snippet: 'ואלה שמות בני ישראל הבאים מצרימה' },
  { lineId: 4, bookId: 3, bookTitle: 'ויקרא',  tocText: 'פרק א', score: 0.85, proximityScore: 0.82, snippet: 'ויקרא אל משה וידבר אליו מאהל מועד' },
  { lineId: 5, bookId: 1, bookTitle: 'בראשית', tocText: 'פרק ב', score: 0.82, proximityScore: 0.8,  snippet: 'ויכלו השמים והארץ וכל צבאם' },
]

function callAction<T>(name: string, args?: object): Promise<T> {
  if (typeof window.__webviewAction !== 'function')
    return Promise.reject(new Error('bridge not available'))
  return window.__webviewAction(name, args) as Promise<T>
}

export function useBloomSearch() {
  const results      = ref<BloomSearchResult[]>([])
  const isSearching  = ref(false)
  const hasSearched  = ref(false)
  const executedQuery = ref('')

  let currentSearchId: string | null = null
  let unregisterEvent: (() => void) | null = null

  function _cleanup() {
    unregisterEvent?.()
    unregisterEvent = null
    currentSearchId = null
  }

  async function cancelSearch() {
    if (!currentSearchId) return
    const id = currentSearchId
    _cleanup()
    isSearching.value = false
    try { await callAction('BloomSearchCancel', { searchId: id }) } catch { /* ignore */ }
  }

  async function executeSearch(query: string) {
    if (!query.trim()) return

    // Cancel any in-flight search
    if (currentSearchId) await cancelSearch()

    isSearching.value  = true
    hasSearched.value  = true
    results.value      = []
    executedQuery.value = query

    // Dev fallback — no C# host
    if (!isHosted) {
      await new Promise(r => setTimeout(r, 400))
      results.value  = DEV_SAMPLES
      isSearching.value = false
      return
    }

    // Check cache first
    const cached = await cacheGet(query.trim().toLowerCase())
    if (cached) {
      results.value     = cached
      isSearching.value = false
      return
    }

    try {
      const searchId = await callAction<string>('BloomSearchStart', { query })
      currentSearchId = searchId

      unregisterEvent = onWebviewEvent((msg) => {
        if (msg.searchId !== searchId) return
        if (currentSearchId !== searchId) return   // stale

        switch (msg.type) {
          case 'searchBatch':
            results.value = [...results.value, ...(msg.results as BloomSearchResult[])]
            break
          case 'searchComplete':
            isSearching.value = false
            if (results.value.length > 0)
              cacheSet(query.trim().toLowerCase(), results.value).catch(() => {})
            _cleanup()
            break
          case 'searchCancelled':
            isSearching.value = false
            _cleanup()
            break
          case 'searchError':
            console.error('[useBloomSearch] search error:', msg.error)
            isSearching.value = false
            _cleanup()
            break
        }
      })
    } catch (err) {
      console.error('[useBloomSearch] failed to start search:', err)
      isSearching.value = false
    }
  }

  function clearSearch() {
    results.value       = []
    hasSearched.value   = false
    executedQuery.value = ''
  }

  async function loadCachedResults(query: string): Promise<boolean> {
    const cached = await cacheGet(query.trim().toLowerCase())
    if (!cached) return false
    results.value       = cached
    executedQuery.value = query
    hasSearched.value   = true
    return true
  }

  return { results, isSearching, hasSearched, executedQuery, executeSearch, cancelSearch, clearSearch, loadCachedResults }
}
