/**
 * Search result cache — persisted in the main app-state IDB under `search:` prefix.
 * LRU-capped at 100 entries.
 *
 * Keys:
 *   search:lru          — string[] of query keys in LRU order (oldest first)
 *   search:{query}      — BloomSearchResult[]
 */
import { idbGet, idbSet, idbDelete } from '@/utils/idbPersistence'
import type { BloomSearchResult } from './searchTypes'

const PREFIX = 'search:'
const LRU_KEY = `${PREFIX}lru`
const MAX = 100

function key(query: string) { return `${PREFIX}${query}` }

async function getLru(): Promise<string[]> {
  return (await idbGet<string[]>(LRU_KEY)) ?? []
}

async function setLru(lru: string[]): Promise<void> {
  await idbSet(LRU_KEY, lru)
}

export async function cacheGet(query: string): Promise<BloomSearchResult[] | null> {
  const results = await idbGet<BloomSearchResult[]>(key(query))
  if (!results) return null
  // bump to most-recent in LRU
  const lru = await getLru()
  const updated = [...lru.filter(q => q !== query), query]
  await setLru(updated)
  return results
}

export async function cacheSet(query: string, results: BloomSearchResult[]): Promise<void> {
  const lru = await getLru()
  const without = lru.filter(q => q !== query)

  // evict oldest if at cap
  if (without.length >= MAX) {
    const evict = without.shift()!
    await idbDelete(key(evict))
  }

  without.push(query)
  await Promise.all([
    idbSet(key(query), results),
    setLru(without),
  ])
}

export async function cacheClear(): Promise<void> {
  const lru = await getLru()
  await Promise.all([
    ...lru.map(q => idbDelete(key(q))),
    idbDelete(LRU_KEY),
  ])
}
