/**
 * Search result cache — persisted in app-search-cache IDB under `search:` prefix.
 * LRU-capped at 100 queries.
 *
 * Cache keys encode the plain query plus all advanced search options that affect which
 * results C# returns (e.g. "זימון|d10|ord|ktv|ww"). The display-LRU stores only the
 * plain normalized query strings so the datalist shows clean text, not encoded keys.
 *
 * Each entry stores results as individual chunk keys rather than one monolithic blob.
 * This avoids the read-modify-write amplification of growing a single array on every
 * batch: appending is one `put` of just the new batch (O(batch)), never a re-read +
 * re-clone + re-write of everything received so far (which was O(n²) over a stream).
 *
 * Layout per cache key K:
 *   `search:meta:K`       — SearchCacheMeta (small; chunk count + completion flags)
 *   `search:chunk:K:0`    — first batch of results
 *   `search:chunk:K:1`    — second batch … etc., read back in ascending order.
 *
 * Chunks are stored (and read) in write order, so the concatenated result array is
 * byte-for-byte the same order as it streamed in. Scroll restore and stream-resume
 * both key off array position / length, so this ordering guarantee is load-bearing.
 */
import { defineStore } from 'pinia'
import { idbGet, idbSet, idbDelete, idbDeleteByPrefix } from '@/utils/persistence'
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

/**
 * Small per-query record, always read first. `chunks` is the number of
 * `search:chunk:K:<i>` records (i from 0..chunks-1) holding this query's results.
 */
interface SearchCacheMeta {
  chunks: number
  complete: boolean
  indexingComplete: boolean
}

const PREFIX = 'search:'
const META_PREFIX = `${PREFIX}meta:`
const CHUNK_PREFIX = `${PREFIX}chunk:`
const LRU_KEY = `${PREFIX}lru`
/**
 * Parallel to LRU_KEY — stores the plain normalized query string for each entry in the
 * same positional order as the main LRU array. Used by getRecentQueries so the datalist
 * shows clean text (e.g. "זימון") rather than encoded cache keys (e.g. "זימון|d10|ww").
 */
const DISPLAY_LRU_KEY = `${PREFIX}display-lru`
const MAX = 100

function metaKey(query: string) {
  return `${META_PREFIX}${query}`
}
function chunkKey(query: string, index: number) {
  return `${CHUNK_PREFIX}${query}:${index}`
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

/** Delete a query's meta record and every one of its chunk records. */
async function deleteEntry(key: string): Promise<void> {
  await Promise.all([
    idbDelete(metaKey(key)),
    // Trailing ':' scopes the prefix to THIS query's chunks — without it, key "foo"
    // would also match chunks of "foobar". chunkKey() always appends ":<index>".
    idbDeleteByPrefix(`${CHUNK_PREFIX}${key}:`),
  ])
}

async function evictIfNeeded(key: string): Promise<void> {
  const [lru, displayLru] = await Promise.all([getLru(), getDisplayLru()])
  const without = lru.filter((k) => k !== key)
  if (without.length < MAX) return
  // Evict the least-recently-used entry (first element)
  const evictKey = without.shift()!
  // The two lists are index-parallel over the UNFILTERED lru, so the display entry to drop
  // is the one at the evicted key's index THERE. displayLru.slice(1) is only the same thing
  // while `key` is not itself at index 0; when it is, the two lists shift apart by one and
  // stay that way, and getRecentQueries then shows query text belonging to another entry.
  const evictIndex = lru.indexOf(evictKey)
  const updatedDisplayLru =
    evictIndex !== -1 ? displayLru.filter((_, i) => i !== evictIndex) : displayLru.slice(1)
  await Promise.all([
    deleteEntry(evictKey),
    idbSet(LRU_KEY, without),
    idbSet(DISPLAY_LRU_KEY, updatedDisplayLru),
  ])
}

export const useSearchCacheStore = defineStore('searchCache', () => {
  async function get(key: string, displayQuery: string): Promise<SearchCacheEntry | null> {
    const meta = await idbGet<SearchCacheMeta>(metaKey(key))
    // No meta record → either never cached, or an entry written in the pre-chunk
    // single-blob format. Treat both as a miss: the caller falls back to a fresh
    // search, which is invisible to the user (the cache is best-effort). Old blobs
    // are left to be reclaimed by LRU eviction / full reset.
    if (!meta) return null
    // Read chunks in write order (0..chunks-1) and concatenate. This preserves the
    // exact streamed order, which scroll restore (index-into-array) and stream
    // resume (skip = results.length) both depend on.
    const results: FullTextSearchResult[] = []
    for (let i = 0; i < meta.chunks; i++) {
      const chunk = await idbGet<FullTextSearchResult[]>(chunkKey(key, i))
      if (chunk) results.push(...chunk)
    }
    await touchLru(key, displayQuery)
    return { results, complete: meta.complete, indexingComplete: meta.indexingComplete }
  }

  /** Write the initial (empty) entry when a new search starts. */
  async function init(key: string, displayQuery: string, indexingComplete: boolean): Promise<void> {
    await evictIfNeeded(key)
    // A prior search under the same key may have left stale chunks — clear them so a
    // resumed read never concatenates old chunks past the new meta's count. (init sets
    // chunks:0, so a stale chunk:K:0 would otherwise be orphaned but never read; still,
    // clearing keeps the store consistent and reclaims the space immediately.)
    await deleteEntry(key)
    await idbSet(metaKey(key), {
      chunks: 0,
      complete: false,
      indexingComplete,
    } satisfies SearchCacheMeta)
    await touchLru(key, displayQuery)
  }

  /** Append a batch of results as a new chunk. Fire-and-forget safe — caller awaits. */
  async function appendBatch(key: string, batch: FullTextSearchResult[]): Promise<void> {
    const meta = await idbGet<SearchCacheMeta>(metaKey(key))
    if (!meta) return
    if (batch.length === 0) return
    // IDB's structured clone algorithm rejects arrays/objects created in the WebView2
    // host realm (via PostWebMessageAsString → JSON.parse in the host context). JSON
    // round-trip re-creates them as plain clonable values IDB accepts. We round-trip
    // ONLY the new batch here — never the accumulated set — so the cost is O(batch),
    // not O(total) per append.
    const clonable = JSON.parse(JSON.stringify(batch)) as FullTextSearchResult[]
    await idbSet(chunkKey(key, meta.chunks), clonable)
    // Bump the chunk count only after the chunk write lands, so a crash between the two
    // leaves meta pointing at fewer chunks (a readable prefix) rather than at a chunk
    // index that doesn't exist yet.
    await idbSet(metaKey(key), { ...meta, chunks: meta.chunks + 1 } satisfies SearchCacheMeta)
  }

  /** Mark the entry as complete. */
  async function markComplete(key: string, indexingComplete: boolean): Promise<void> {
    const meta = await idbGet<SearchCacheMeta>(metaKey(key))
    if (!meta) return
    await idbSet(metaKey(key), { ...meta, complete: true, indexingComplete } satisfies SearchCacheMeta)
  }

  async function clear(): Promise<void> {
    const lru = await getLru()
    await Promise.all([
      ...lru.map((k) => deleteEntry(k)),
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
      deleteEntry(key),
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
      const q = displayLru[i] ?? ''
      if (q && !seen.has(q)) {
        seen.add(q)
        unique.push(q)
      }
    }
    return unique
  }

  return { get, init, appendBatch, markComplete, clear, remove, getRecentQueries }
})
