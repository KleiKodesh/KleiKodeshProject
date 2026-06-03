/**
 * Search result cache — persisted in app-search-cache IDB under `search:` prefix.
 * LRU-capped at 100 queries.
 *
 * Cache keys encode the plain query plus all advanced search options that affect which
 * results C# returns (e.g. "זימון|d10|ord|ktv|ww"). The display-LRU stores only the
 * plain normalized query strings so the datalist shows clean text, not encoded keys.
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
/**
 * Parallel to LRU_KEY — stores the plain normalized query string for each entry in the
 * same positional order as the main LRU array. Used by getRecentQueries so the datalist
 * shows clean text (e.g. "זימון") rather than encoded cache keys (e.g. "זימון|d10|ww").
 */
const DISPLAY_LRU_KEY = `${PREFIX}display-lru`
const MAX = 100

function cacheKey(query: string) {
  return `${PREFIX}${query}`
}

async function getLru(): Promise<string[]> {
  return (await idbGet<string[]>(LRU_KEY)) ?? []
}

async function getDisplayLru(): Promise<string[]> {
  return (await idbGet<string[]>(DISPLAY_LRU_KEY)) ?? []
}

async function touchLru(key: string, displayQuery: string): Promise<void> {
  const [lru, displayLru] = await Promise.all([getLru(), getDisplayLru()])
  const existingIndex = lru.indexOf(key)
  const updatedLru = [...lru.filter((k) => k !== key), key]
  // Keep display LRU in sync: remove the old entry at the same position, append new one
  const updatedDisplayLru =
    existingIndex !== -1
      ? [...displayLru.filter((_, index) => index !== existingIndex), displayQuery]
      : [...displayLru, displayQuery]
  await Promise.all([idbSet(LRU_KEY, updatedLru), idbSet(DISPLAY_LRU_KEY, updatedDisplayLru)])
}

async function evictIfNeeded(key: string): Promise<void> {
  const [lru, displayLru] = await Promise.all([getLru(), getDisplayLru()])
  const without = lru.filter((k) => k !== key)
  if (without.length < MAX) return
  // Evict the least-recently-used entry (first element)
  const evictKey = without.shift()!
  const updatedDisplayLru = displayLru.slice(1)
  await Promise.all([
    idbDelete(cacheKey(evictKey)),
    idbSet(LRU_KEY, without),
    idbSet(DISPLAY_LRU_KEY, updatedDisplayLru),
  ])
}

export const useSearchCacheStore = defineStore('searchCache', () => {
  async function get(key: string, displayQuery: string): Promise<SearchCacheEntry | null> {
    const entry = await idbGet<SearchCacheEntry>(cacheKey(key))
    if (!entry) return null
    await touchLru(key, displayQuery)
    return entry
  }

  /** Write the initial entry (or overwrite) when a new search starts. */
  async function init(key: string, displayQuery: string, indexingComplete: boolean): Promise<void> {
    await evictIfNeeded(key)
    await idbSet(cacheKey(key), {
      results: [],
      complete: false,
      indexingComplete,
    } satisfies SearchCacheEntry)
    await touchLru(key, displayQuery)
  }

  /** Append a batch of results to an existing entry. Fire-and-forget safe — caller awaits. */
  async function appendBatch(key: string, batch: FullTextSearchResult[]): Promise<void> {
    const entry = await idbGet<SearchCacheEntry>(cacheKey(key))
    if (!entry) return
    entry.results.push(...batch)
    await idbSet(cacheKey(key), entry)
  }

  /** Mark the entry as complete. */
  async function markComplete(key: string, indexingComplete: boolean): Promise<void> {
    const entry = await idbGet<SearchCacheEntry>(cacheKey(key))
    if (!entry) return
    entry.complete = true
    entry.indexingComplete = indexingComplete
    await idbSet(cacheKey(key), entry)
  }

  async function clear(): Promise<void> {
    const lru = await getLru()
    await Promise.all([
      ...lru.map((k) => idbDelete(cacheKey(k))),
      idbDelete(LRU_KEY),
      idbDelete(DISPLAY_LRU_KEY),
    ])
  }

  /** Remove a single entry and evict it from both LRU lists. */
  async function remove(key: string): Promise<void> {
    const [lru, displayLru] = await Promise.all([getLru(), getDisplayLru()])
    const index = lru.indexOf(key)
    const updatedLru = lru.filter((k) => k !== key)
    const updatedDisplayLru =
      index !== -1 ? displayLru.filter((_, i) => i !== index) : displayLru
    await Promise.all([
      idbDelete(cacheKey(key)),
      idbSet(LRU_KEY, updatedLru),
      idbSet(DISPLAY_LRU_KEY, updatedDisplayLru),
    ])
  }

  /**
   * Returns the most-recently-used plain query strings, newest first.
   * Duplicates are collapsed — if the same query was searched with different options,
   * only the most recent occurrence appears.
   */
  async function getRecentQueries(limit = 10): Promise<string[]> {
    const displayLru = await getDisplayLru()
    const seen = new Set<string>()
    const unique: string[] = []
    for (let i = displayLru.length - 1; i >= 0 && unique.length < limit; i--) {
      const q = displayLru[i]
      if (!seen.has(q)) {
        seen.add(q)
        unique.push(q)
      }
    }
    return unique
  }

  return { get, init, appendBatch, markComplete, clear, remove, getRecentQueries }
})
