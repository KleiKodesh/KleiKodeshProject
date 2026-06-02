/**
 * Search result cache — persisted in app-search-cache IDB under `search:` prefix.
 * LRU-capped at 100 queries.
 *
 * Each entry stores the full result set, a `complete` flag (stream finished), and an
 * `indexingComplete` flag (true when the entry was written after the FTS index was fully
 * built). Entries written during indexing are served immediately for instant results but
 * are always refreshed in the background — the index may have grown since they were cached.
 *
 * Results are NOT cached in memory — they can be large (hundreds of items with snippets).
 */
import { defineStore } from 'pinia'
import { idbGet, idbSet, idbDelete } from '@/utils/persistence'
import type { FullTextSearchResult } from '@/features/full-text-search/fullTextSearchTypes'

export interface SearchCacheEntry {
  results: FullTextSearchResult[]
  /** True when the stream finished (not cancelled or interrupted). */
  complete: boolean
  /**
   * True when this entry was written while the FTS index was fully built.
   * False means the entry was cached during indexing — results may be incomplete
   * and must be refreshed the next time the user searches for this query.
   */
  indexingComplete: boolean
}

const PREFIX = 'search:'
const LRU_KEY = `${PREFIX}lru`
const MAX = 100

function cacheKey(query: string) {
  return `${PREFIX}${query}`
}

async function getLru(): Promise<string[]> {
  return (await idbGet<string[]>(LRU_KEY)) ?? []
}

async function touchLru(query: string): Promise<void> {
  const lru = await getLru()
  const updated = [...lru.filter((q) => q !== query), query]
  await idbSet(LRU_KEY, updated)
}

async function evictIfNeeded(query: string): Promise<void> {
  const lru = await getLru()
  const without = lru.filter((q) => q !== query)
  if (without.length < MAX) return
  const evict = without.shift()!
  await idbDelete(cacheKey(evict))
  await idbSet(LRU_KEY, without)
}

export const useSearchCacheStore = defineStore('searchCache', () => {
  async function get(query: string): Promise<SearchCacheEntry | null> {
    const entry = await idbGet<SearchCacheEntry>(cacheKey(query))
    if (!entry) return null
    await touchLru(query)
    return entry
  }

  /** Write the initial entry (or overwrite) when a new search starts. */
  async function init(query: string, indexingComplete: boolean): Promise<void> {
    await evictIfNeeded(query)
    await idbSet(cacheKey(query), {
      results: [],
      complete: false,
      indexingComplete,
    } satisfies SearchCacheEntry)
    await touchLru(query)
  }

  /** Append a batch of results to an existing entry. Fire-and-forget safe — caller awaits. */
  async function appendBatch(query: string, batch: FullTextSearchResult[]): Promise<void> {
    const entry = await idbGet<SearchCacheEntry>(cacheKey(query))
    if (!entry) return
    entry.results.push(...batch)
    await idbSet(cacheKey(query), entry)
  }

  /** Mark the entry as complete. */
  async function markComplete(query: string, indexingComplete: boolean): Promise<void> {
    const entry = await idbGet<SearchCacheEntry>(cacheKey(query))
    if (!entry) return
    entry.complete = true
    entry.indexingComplete = indexingComplete
    await idbSet(cacheKey(query), entry)
  }

  async function clear(): Promise<void> {
    const lru = await getLru()
    await Promise.all([...lru.map((q) => idbDelete(cacheKey(q))), idbDelete(LRU_KEY)])
  }

  /** Remove a single query entry and evict it from the LRU list. */
  async function remove(query: string): Promise<void> {
    const lru = await getLru()
    const updated = lru.filter((q) => q !== query)
    await Promise.all([
      idbDelete(cacheKey(query)),
      idbSet(LRU_KEY, updated),
    ])
  }

  /** Returns the most-recently-used queries, newest first. */
  async function getRecentQueries(limit = 10): Promise<string[]> {
    const lru = await getLru()
    return lru.slice().reverse().slice(0, limit)
  }

  return { get, init, appendBatch, markComplete, clear, remove, getRecentQueries }
})
