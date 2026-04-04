/**
 * Search result cache — persisted in app-settings IDB under `search:` prefix.
 * LRU-capped at 100 entries.
 *
 * Only stores may import from idbPersistence.ts — this store is the sole owner
 * of search cache persistence.
 */
import { defineStore } from 'pinia'
import { idbGet, idbSet, idbDelete } from '@/utils/idbPersistence'
import type { BloomSearchResult } from '@/components/search-db/searchTypes'

const PREFIX = 'search:'
const LRU_KEY = `${PREFIX}lru`
const MAX = 100

function cacheKey(query: string) {
  return `${PREFIX}${query}`
}

async function getLru(): Promise<string[]> {
  return (await idbGet<string[]>(LRU_KEY)) ?? []
}

export const useSearchCacheStore = defineStore('searchCache', () => {
  async function get(query: string): Promise<BloomSearchResult[] | null> {
    const results = await idbGet<BloomSearchResult[]>(cacheKey(query))
    if (!results) return null
    // bump to most-recent in LRU
    const lru = await getLru()
    const updated = [...lru.filter((q) => q !== query), query]
    await idbSet(LRU_KEY, updated)
    return results
  }

  async function set(query: string, results: BloomSearchResult[]): Promise<void> {
    const lru = await getLru()
    const without = lru.filter((q) => q !== query)
    // evict oldest if at cap
    if (without.length >= MAX) {
      const evict = without.shift()!
      await idbDelete(cacheKey(evict))
    }
    without.push(query)
    await Promise.all([idbSet(cacheKey(query), results), idbSet(LRU_KEY, without)])
  }

  async function clear(): Promise<void> {
    const lru = await getLru()
    await Promise.all([...lru.map((q) => idbDelete(cacheKey(q))), idbDelete(LRU_KEY)])
  }

  return { get, set, clear }
})
