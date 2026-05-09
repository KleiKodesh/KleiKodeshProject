/**
 * LRU disk cache for book catalog TOC search results.
 *
 * Caches the output of the Phase 2 TOC heuristics search so that repeated
 * queries (e.g. the user types the same phrase again) skip the DB round-trips
 * entirely and return instantly.
 *
 * Cap: 25 entries. Only this feature touches this cache, so a plain module
 * (no Pinia) co-located here is the right pattern — see dictCache.ts in the
 * dictionary feature as the reference for this case.
 *
 * Structure follows the established IDB-backed LRU pattern:
 *   - PREFIX constant for all entry keys
 *   - Separate LRU_KEY entry holding the ordered list of cached query keys
 *   - get: reads entry, calls touchLru on hit
 *   - set: calls evictIfNeeded before writing
 *   - clear: deletes all entries in the LRU list plus the LRU key itself
 *
 * The cache key is the normalized query string (words joined by space) so
 * "בראשית פרק" and "בראשית  פרק" resolve to the same key.
 *
 * Results are never cached in memory — only the LRU key list may be kept in
 * memory. TOC result arrays can be large and belong in IDB only.
 */

import {
  idbCatalogTocCacheGet,
  idbCatalogTocCacheSet,
  idbCatalogTocCacheDelete,
} from '@/utils/persistence'
import type { TocFsItem } from './useBookCatalogSearch'

// ─── Constants ────────────────────────────────────────────────────────────────

const PREFIX = 'toc:'
const LRU_KEY = `${PREFIX}lru`
const CAP = 25

// ─── Types ────────────────────────────────────────────────────────────────────

export interface CatalogTocCacheEntry {
  items: TocFsItem[]
}

// ─── LRU helpers ─────────────────────────────────────────────────────────────

function cacheKey(normalizedQuery: string): string {
  return `${PREFIX}${normalizedQuery}`
}

async function getLruList(): Promise<string[]> {
  return (await idbCatalogTocCacheGet<string[]>(LRU_KEY)) ?? []
}

async function touchLru(normalizedQuery: string): Promise<void> {
  const lru = await getLruList()
  const updated = [...lru.filter((q) => q !== normalizedQuery), normalizedQuery]
  await idbCatalogTocCacheSet(LRU_KEY, updated)
}

async function evictIfNeeded(normalizedQuery: string): Promise<void> {
  const lru = await getLruList()
  const without = lru.filter((q) => q !== normalizedQuery)
  if (without.length < CAP) return
  const evict = without.shift()!
  await idbCatalogTocCacheDelete(cacheKey(evict))
  await idbCatalogTocCacheSet(LRU_KEY, without)
}

// ─── Public API ───────────────────────────────────────────────────────────────

/**
 * Returns the cached TOC search results for the given normalized query, or
 * null on a cache miss. Moves the entry to the most-recently-used position.
 */
export async function getCatalogTocCache(
  normalizedQuery: string,
): Promise<CatalogTocCacheEntry | null> {
  const entry = await idbCatalogTocCacheGet<CatalogTocCacheEntry>(cacheKey(normalizedQuery))
  if (!entry) return null
  await touchLru(normalizedQuery)
  return entry
}

/**
 * Stores TOC search results for the given normalized query.
 * Evicts the least-recently-used entry first if the cache is at capacity.
 */
export async function setCatalogTocCache(
  normalizedQuery: string,
  items: TocFsItem[],
): Promise<void> {
  await evictIfNeeded(normalizedQuery)
  await idbCatalogTocCacheSet(cacheKey(normalizedQuery), { items } satisfies CatalogTocCacheEntry)
  await touchLru(normalizedQuery)
}

/**
 * Deletes all cached entries and the LRU key.
 * Called as part of the full app reset via idbClearAll (which drops the
 * entire database), so this function is only needed for targeted clears.
 */
export async function clearCatalogTocCache(): Promise<void> {
  const lru = await getLruList()
  await Promise.all(lru.map((q) => idbCatalogTocCacheDelete(cacheKey(q))))
  await idbCatalogTocCacheDelete(LRU_KEY)
}
