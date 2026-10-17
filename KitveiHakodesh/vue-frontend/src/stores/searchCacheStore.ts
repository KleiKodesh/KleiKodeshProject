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
 * batch: appending is one `put` of just the new chunk (O(chunk)), never a re-read +
 * re-clone + re-write of everything received so far (which was O(n²) over a stream).
 *
 * Layout per cache key K:
 *   `search:meta:K`       — SearchCacheMeta (small; chunk count + completion flags)
 *   `search:chunk:K:0`    — first chunk of results
 *   `search:chunk:K:1`    — second chunk … etc., read back in ascending order.
 *
 * Chunks are stored (and read) in write order, so the concatenated result array is
 * byte-for-byte the same order as it streamed in. Scroll restore and stream-resume
 * both key off array position / length, so this ordering guarantee is load-bearing.
 *
 * WRITE PATH IS OFF THE STREAM LOOP. A broad query pushes thousands of frames; one IDB
 * transaction per frame (let alone three: read meta, put chunk, put meta) throttled the
 * whole stream. So, for the ACTIVE key, the meta record lives in memory and is written
 * once at markComplete; appended hits are buffered and written as one chunk per
 * CHUNK_FLUSH_FRAMES frames or CHUNK_FLUSH_HITS hits, whichever first; and nothing is
 * awaited per frame — every write joins one pending chain, so writes stay ordered, and
 * only markComplete / remove / init / get wait for that chain. A crash mid-search leaves
 * the on-disk meta at chunks:0, complete:false — a miss, re-run from scratch. Accepted.
 */
import { defineStore } from 'pinia'
import { idbGet, idbSet, idbDelete, idbDeleteByPrefix } from '@/utils/persistence'
import type { FullTextSearchResult } from '@/features/full-text-search/fullTextSearchTypes'

export interface SearchCacheEntry {
  results: FullTextSearchResult[]
  /** The search's matched-term table — what each hit's `termIds` index into. */
  terms: string[]
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
  /** Written with `complete` — an incomplete entry is re-run, never resumed, so the
   *  table is only needed once the stream finished. */
  terms?: string[]
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
/** A buffered chunk is written once this many frames have been appended to it... */
const CHUNK_FLUSH_FRAMES = 8
/** ...or once it holds this many hits, whichever comes first. */
const CHUNK_FLUSH_HITS = 4096

/**
 * The search being written right now: its meta kept in memory (written to IDB only at
 * markComplete) and the hits buffered for the next chunk. One at a time — a new init
 * replaces it. Null when no search is being cached.
 */
interface ActiveSearchCacheWrite {
  key: string
  meta: SearchCacheMeta
  bufferedHits: FullTextSearchResult[]
  bufferedFrames: number
}
let activeWrite: ActiveSearchCacheWrite | null = null

/** Every IDB write of the active search joins this chain, so chunk i always lands before
 *  chunk i+1 and before the meta that counts it. Never rejects — a failed write is logged
 *  and the chain goes on, so one bad put cannot wedge every later cache operation. */
let writeChain: Promise<void> = Promise.resolve()
/** Set when a queued write of the active search failed: its entry is then missing a chunk
 *  and must never be marked complete. Reset by init. */
let activeWriteFailed = false
function enqueueWrite(write: () => Promise<void>): void {
  writeChain = writeChain.then(write).catch((err) => {
    activeWriteFailed = true
    console.warn('[searchCache] write failed:', err)
  })
}

/** Move the active search's buffered hits into the write chain as the next chunk. */
function flushBufferedChunk(): void {
  const active = activeWrite
  if (!active || active.bufferedHits.length === 0) return
  const chunk = active.bufferedHits
  const index = active.meta.chunks
  active.bufferedHits = []
  active.bufferedFrames = 0
  active.meta.chunks = index + 1
  // Stored as-is: hits are plain page-realm objects of numbers and int arrays (decoded
  // from MessagePack in this realm on both runtimes), which IDB's structured clone takes
  // directly. No serialisation round-trip — this is one `put` of just the new chunk.
  enqueueWrite(() => idbSet(chunkKey(active.key, index), chunk))
}

/** Drop the active search (buffered hits included) and wait for its queued writes. */
async function settleActiveWrite(): Promise<void> {
  activeWrite = null
  await writeChain
}

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
    // Queued chunk writes of a search that just finished must land before they are read.
    await writeChain
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
    return { results, terms: meta.terms ?? [], complete: meta.complete, indexingComplete: meta.indexingComplete }
  }

  /** Write the initial (empty) entry when a new search starts. */
  async function init(key: string, displayQuery: string, indexingComplete: boolean): Promise<void> {
    // A previous search's queued chunk writes must not land after this key's wipe below.
    await settleActiveWrite()
    await evictIfNeeded(key)
    // A prior search under the same key may have left stale chunks — clear them so a
    // resumed read never concatenates old chunks past the new meta's count. (init sets
    // chunks:0, so a stale chunk:K:0 would otherwise be orphaned but never read; still,
    // clearing keeps the store consistent and reclaims the space immediately.)
    await deleteEntry(key)
    const meta: SearchCacheMeta = { chunks: 0, complete: false, indexingComplete }
    // On disk at chunks:0 until markComplete: an interrupted search reads back as a miss.
    await idbSet(metaKey(key), meta)
    await touchLru(key, displayQuery)
    activeWriteFailed = false
    activeWrite = { key, meta, bufferedHits: [], bufferedFrames: 0 }
  }

  /**
   * Append one frame's hits to the active search. Synchronous and never awaited by the
   * stream loop: the hits are buffered, and a chunk write is queued only every
   * CHUNK_FLUSH_FRAMES frames / CHUNK_FLUSH_HITS hits. Ignored for a key that is not the
   * active one (a superseded search still draining its stream).
   */
  function appendBatch(key: string, batch: FullTextSearchResult[]): void {
    const active = activeWrite
    if (!active || active.key !== key || batch.length === 0) return
    for (const hit of batch) active.bufferedHits.push(hit) // spread-push would overflow on very large frames
    active.bufferedFrames++
    if (active.bufferedFrames >= CHUNK_FLUSH_FRAMES || active.bufferedHits.length >= CHUNK_FLUSH_HITS)
      flushBufferedChunk()
  }

  /**
   * The stream finished: write the last chunk, then the meta — complete, with the chunk
   * count and the search's matched-term table — and wait until everything has landed.
   * The meta goes last, so a crash before it leaves the entry a miss, never a short set
   * read back as complete.
   */
  async function markComplete(key: string, indexingComplete: boolean, terms: string[]): Promise<void> {
    const active = activeWrite
    if (!active || active.key !== key) return
    flushBufferedChunk()
    const meta: SearchCacheMeta = { ...active.meta, complete: true, indexingComplete, terms }
    // Decided inside the chain, after every chunk write has either landed or failed.
    enqueueWrite(() => (activeWriteFailed ? deleteEntry(key) : idbSet(metaKey(key), meta)))
    await settleActiveWrite()
  }

  async function clear(): Promise<void> {
    await settleActiveWrite()
    const lru = await getLru()
    await Promise.all([
      ...lru.map((k) => deleteEntry(k)),
      idbDelete(LRU_KEY),
      idbDelete(DISPLAY_LRU_KEY),
    ])
  }

  /** Remove a single entry and evict it from both LRU lists. */
  async function remove(key: string): Promise<void> {
    // The active search's queued chunks must land before the wipe, not after it.
    if (activeWrite?.key === key) await settleActiveWrite()
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
