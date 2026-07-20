/**
 * Recent full-text-search queries, persisted to localStorage.
 *
 * Backs the <datalist> on the full-text search input so the browser offers the
 * user's own recent queries as native suggestions. This is app-owned (works
 * identically in dev Chromium and the WebView2 host, unlike the browser's
 * built-in form-history autofill, which WebView2 disables).
 *
 * Newest-first, deduped by trimmed value, capped at RECENTS_MAX.
 */
import { ref, readonly } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'

const LS_KEY = 'fts.recents'
const RECENTS_MAX = 15

// Module-level singleton — one shared recents list across all FTS bar instances.
const recents = ref<string[]>(lsGet<string[]>(LS_KEY) ?? [])

export function useFtsSearchRecents() {
  function record(query: string) {
    const q = query.trim()
    if (!q) return
    const next = [q, ...recents.value.filter((r) => r !== q)].slice(0, RECENTS_MAX)
    recents.value = next
    lsSet(LS_KEY, next)
  }

  return { recents: readonly(recents), record }
}
