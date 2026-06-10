/**
 * Full-text search composable — two-phase approach:
 *
 * Phase 1 — FtsSearchStart: C# runs index-only pass and returns all matching
 *   { lineId, bookId, bookTitle } immediately via an idsComplete push event.
 *   No snippet work, very fast. Frontend gets the full result list and renders
 *   placeholder rows instantly.
 *
 * Phase 2 — FtsGetSnippets: Vue calls this synchronous RPC with the lineIds
 *   currently visible in the virtualizer viewport. C# generates snippets for
 *   exactly those lines and returns them. Called on scroll as new rows come
 *   into view.
 *
 * No persistence — the search is fast enough to re-run on demand.
 */
import { ref } from 'vue'
import { storeToRefs } from 'pinia'
import { isHosted, query, onWebviewEvent } from '@/webview-host/seforimDb'
import { callBridgeAction } from '@/webview-host/bridge'
import { useSettingsStore } from '@/stores/settingsStore'
import { SQL } from '@/webview-host/queries.sql'
import type { FullTextSearchResult, SearchFailReason } from './fullTextSearchTypes'

// ── Dev sample data ───────────────────────────────────────────────────────────

const DEV_SAMPLES: FullTextSearchResult[] = [
  { lineId: 1, bookId: 1, bookTitle: 'בראשית', tocText: 'פרק א', score: 0, snippet: 'בראשית ברא אלקים את <mark>השמים</mark> ואת הארץ', matchedTerms: ['השמים'] },
  { lineId: 2, bookId: 1, bookTitle: 'בראשית', tocText: 'פרק א', score: 0, snippet: 'והארץ היתה תהו ובהו וחשך על פני תהום', matchedTerms: [] },
  { lineId: 3, bookId: 2, bookTitle: 'שמות', tocText: 'פרק א', score: 0, snippet: 'ואלה שמות בני ישראל הבאים מצרימה', matchedTerms: [] },
  { lineId: 4, bookId: 3, bookTitle: 'ויקרא', tocText: 'פרק א', score: 0, snippet: 'ויקרא אל משה וידבר אליו מאהל מועד', matchedTerms: [] },
  { lineId: 5, bookId: 1, bookTitle: 'בראשית', tocText: 'פרק ב', score: 0, snippet: 'ויכלו השמים והארץ וכל צבאם', matchedTerms: [] },
  { lineId: 6, bookId: 4, bookTitle: 'משנה תורה להרמב"ם', tocText: 'הלכות יסודי התורה › פרק ראשון', score: 0, snippet: 'יסוד היסודות ועמוד החכמות לידע שיש שם מצוי ראשון', matchedTerms: [] },
]

export function useFullTextSearch() {
  const settings = useSettingsStore()
  const {
    searchMaxWordDistance,
    searchRequireOrdered,
    searchExpandKetiv,
    searchWildcardWrap,
    searchGrammarWrap,
  } = storeToRefs(settings)

  const results = ref<FullTextSearchResult[]>([])
  const isSearching = ref(false)
  const hasSearched = ref(false)
  const executedQuery = ref('')
  const searchError = ref<SearchFailReason | null>(null)

  let currentSearchId: string | null = null
  let unregisterEvent: (() => void) | null = null

  function _cleanup() {
    if (unregisterEvent) { unregisterEvent(); unregisterEvent = null }
    currentSearchId = null
  }

  async function cancelSearch() {
    if (!currentSearchId) return
    const id = currentSearchId
    _cleanup()
    isSearching.value = false
    try { await callBridgeAction('FtsSearchCancel', id) } catch { /* ignore */ }
  }

  function _buildQueryToSend(normalizedQuery: string): string {
    if (!settings.searchWildcardWrap && !settings.searchGrammarWrap) return normalizedQuery
    return normalizedQuery
      .split(/\s+/)
      .map((word) => {
        if (!word) return word
        if (/[*?~|%]/.test(word)) return word
        if (settings.searchWildcardWrap) return `*${word}*`
        return `%${word}%`
      })
      .join(' ')
  }

  async function executeSearch(rawQuery: string) {
    if (!rawQuery.trim()) return
    if (currentSearchId) await cancelSearch()

    hasSearched.value = true
    searchError.value = null
    executedQuery.value = rawQuery

    if (!isHosted || typeof window.__webviewAction !== 'function') {
      isSearching.value = true
      results.value = []
      await new Promise((resolve) => setTimeout(resolve, 400))
      results.value = DEV_SAMPLES
      isSearching.value = false
      return
    }

    const queryToSend = _buildQueryToSend(rawQuery.trim().toLowerCase())
    isSearching.value = true
    results.value = []

    let reply: { searchId: string; failReason: SearchFailReason | null } | null = null
    try {
      reply = await callBridgeAction<{ searchId: string; failReason: SearchFailReason | null }>(
        'FtsSearchStart',
        queryToSend,
        settings.searchExpandKetiv,
      )
    } catch {
      searchError.value = 'searchFailed'
      isSearching.value = false
      return
    }

    const searchId = reply?.searchId
    if (!searchId) {
      searchError.value = reply?.failReason ?? 'indexNotReady'
      isSearching.value = false
      return
    }
    currentSearchId = searchId

    unregisterEvent = onWebviewEvent((msg) => {
      if (msg.searchId !== searchId) return

      if (msg.type === 'idsComplete') {
        const lineIds = ((msg as any).lineIds as number[]) ?? []
        _cleanup()
        isSearching.value = false
        results.value = lineIds.map((lineId) => ({
          lineId,
          bookId: 0,
          bookTitle: '',
          tocText: '',
          score: 0,
          snippet: '',
          matchedTerms: [],
        }))
        return
      }

      if (msg.type === 'idsCancelled') {
        _cleanup()
        isSearching.value = false
        return
      }

      if (msg.type === 'idsError') {
        _cleanup()
        // indexMerging is transient — retry once after a short delay
        if (msg.failReason === 'indexMerging') {
          setTimeout(() => executeSearch(rawQuery), 1500)
          return
        }
        searchError.value = (msg.failReason as SearchFailReason) ?? 'searchFailed'
        isSearching.value = false
        return
      }
    })
  }

  type SnippetWindowEntry = {
    snippet: string
    score: number
    matchedTerms: string[]
    tocText: string
    isWeakMatch: boolean
    bookTitle: string
  }

  /**
   * Fetches snippets for a window of lineIds (the visible viewport).
   * Called by the results list as rows scroll into view.
   * Returns a map of lineId → { snippet, score, matchedTerms, tocText, isWeakMatch, bookTitle }.
   */
  async function fetchSnippetsForWindow(lineIds: number[]): Promise<Map<number, SnippetWindowEntry>> {
    if (!lineIds.length) return new Map()

    const queryToSend = _buildQueryToSend(executedQuery.value.trim().toLowerCase())

    type SnippetRow = { lineId: number; score: number; snippet: string; matchedTerms: string[]; isWeakMatch: boolean }

    const [snippetReply, tocRows] = await Promise.all([
      isHosted && typeof window.__webviewAction === 'function'
        ? callBridgeAction<{ snippets: SnippetRow[] }>(
            'FtsGetSnippets',
            lineIds,
            queryToSend,
            settings.searchMaxWordDistance,
            settings.searchRequireOrdered,
            settings.searchContextMarginWords,
            settings.searchExpandKetiv,
          ).catch(() => ({ snippets: [] as SnippetRow[] }))
        : Promise.resolve({ snippets: [] as SnippetRow[] }),

      query<{ lineId: number; tocPath: string; bookTitle: string }>(
        SQL.GET_TOC_PATHS_AND_TITLES_FOR_LINES(lineIds.length),
        lineIds,
      ).catch(() => [] as Array<{ lineId: number; tocPath: string; bookTitle: string }>),
    ])

    const resultMap = new Map<number, SnippetWindowEntry>()
    const tocByLineId = new Map(tocRows.map((r) => [r.lineId, r.tocPath]))
    const bookTitleByLineId = new Map(tocRows.map((r) => [r.lineId, r.bookTitle]))

    for (const s of snippetReply.snippets) {
      resultMap.set(s.lineId, {
        snippet: s.snippet,
        score: s.score,
        matchedTerms: s.matchedTerms,
        tocText: tocByLineId.get(s.lineId) ?? '',
        isWeakMatch: s.isWeakMatch,
        bookTitle: bookTitleByLineId.get(s.lineId) ?? '',
      })
    }

    for (const lineId of lineIds) {
      if (!resultMap.has(lineId)) {
        resultMap.set(lineId, {
          snippet: '',
          score: 0,
          matchedTerms: [],
          tocText: tocByLineId.get(lineId) ?? '',
          isWeakMatch: false,
          bookTitle: bookTitleByLineId.get(lineId) ?? '',
        })
      }
    }

    return resultMap
  }

  function clearSearch() {
    results.value = []
    hasSearched.value = false
    executedQuery.value = ''
    searchError.value = null
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
    fetchSnippetsForWindow,
  }
}
