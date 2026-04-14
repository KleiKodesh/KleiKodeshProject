/**
 * Search result cache — persisted in app-settings IDB under `search:` prefix.
 * LRU-capped at 100 entries.
 *
 * Results are NOT cached in memory — they can be large (hundreds of items with snippets).
 * Only the LRU key list is kept in memory to avoid an extra IDB read on every cache hit.
 */
import { defineStore } from 'pinia'
import { idbGet, idbSet, idbDelete } from '@/utils/persistence'
import type { BloomSearchResult } from '@/components/search-db/searchTypes'

const PREFIX = 'search:'
const LRU_KEY = `${PREFIX}lru`
const MAX = 100

function cacheKey(query: string) {
  return `${PREFIX}${query}`
}

// Only the LRU list is kept in memory — not the results themselves
let _lru: string[] | null = null

async function ensureLru(): Promise<string[]> {
  if (_lru !== null) return _lru
  _lru = (await idbGet<string[]>(LRU_KEY)) ?? []
  return _lru
}

export const useSearchCacheStore = defineStore('searchCache', () => {
  async function get(query: string): Promise<BloomSearchResult[] | null> {
    const results = await idbGet<BloomSearchResult[]>(cacheKey(query))
    if (!results) return null
    // Bump in LRU — update memory and persist async
    const lru = await ensureLru()
    const updated = [...lru.filter((q) => q !== query), query]
    _lru = updated
    idbSet(LRU_KEY, updated) // fire-and-forget
    return results
  }

  async function set(query: string, results: BloomSearchResult[]): Promise<void> {
    const lru = await ensureLru()
    const without = lru.filter((q) => q !== query)
    if (without.length >= MAX) {
      const evict = without.shift()!
      idbDelete(cacheKey(evict)) // fire-and-forget
    }
    without.push(query)
    _lru = without
    // Both writes fire-and-forget — caller doesn't need to wait
    idbSet(cacheKey(query), results)
    idbSet(LRU_KEY, without)
  }

  async function clear(): Promise<void> {
    const lru = await ensureLru()
    _lru = []
    await Promise.all([...lru.map((q) => idbDelete(cacheKey(q))), idbDelete(LRU_KEY)])
  }

  return { get, set, clear }
})
