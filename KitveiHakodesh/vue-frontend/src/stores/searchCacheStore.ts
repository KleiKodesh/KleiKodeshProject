/**
 * Search result cache — persisted in app-search-cache IDB under `search:` prefix.
 * LRU-capped at 100 queries.
 *
 * Cache keys encode the final query string plus every search option that changes which
 * hits the service returns (e.g. "^זימון|d10|ctx8|…"). The display-LRU stores only the
 * plain normalized query strings so the datalist shows clean text, not encoded keys.
 *
 * Layout per cache key K — the long key appears ONCE, on the meta record; everything else
 * is addressed by the small numeric id the meta carries:
 *   `search:meta:K`        — SearchCacheMeta (id, chunk count, flags, term table)
 *   `search:c:<id>:<i>`    — chunk i of the results, PACKED (see PackedChunk)
 *
 * SAVED AS IT STREAMS. Every chunk write is followed by a meta write counting it, so an
 * interrupted search (tab switched, app closed) reads back as the hits it had — the caller
 * shows them and RESUMES the stream after the last one (see useFullTextSearch). `complete`
 * says the stream finished; `indexingComplete` says the index was fully built when the
 * search ran (a search run mid-build is shown, then re-run once the index is ready).
 *
 * Chunks are stored (and read) in write order, so the concatenated result array is the
 * exact streamed order. Scroll restore and resume both key off it.
 *
 * SIZE BUDGET. Every meta counts its chunks' bytes. Entries no open tab shows are evicted,
 * least recently used first, until they all fit in BYTE_BUDGET. An entry an open tab shows
 * is PINNED (`search:pins`, owner = workspace:tab) and never evicted or capped, whatever it
 * costs — its tab's scroll restore must stay instant. Closing the tab unpins it.
 *
 * WRITE PATH IS OFF THE STREAM LOOP: appended hits are buffered and written as one chunk
 * per CHUNK_FLUSH_FRAMES frames or CHUNK_FLUSH_HITS hits; every write joins one pending
 * chain, so writes stay ordered and nothing is awaited per frame.
 */
import { defineStore } from 'pinia'
import { idbGet, idbSet, idbDelete, idbDeleteByPrefix } from '@/utils/persistence'
import type { FullTextSearchResult } from '@/features/full-text-search/fullTextSearchTypes'

export interface SearchCacheEntry {
  results: FullTextSearchResult[]
  /** The search's matched-term table — what each hit's snippet-mark term ids index into. */
  terms: string[]
  /** True when the stream finished (not cancelled or interrupted). */
  complete: boolean
  /** True when the FTS index was fully built when this search ran. False: the hits may be
   *  incomplete or stale — show them, then re-run once the index is ready. */
  indexingComplete: boolean
}

/** Small per-query record, always read first. */
interface SearchCacheMeta {
  /** Numeric id the chunk records are addressed by. */
  id: number
  /** Number of `search:c:<id>:<i>` records (i from 0..chunks-1). */
  chunks: number
  complete: boolean
  indexingComplete: boolean
  /** The term table as of the last chunk written. */
  terms: string[]
  /** Bytes of every chunk written so far. Missing on metas from before the size budget (0). */
  bytes?: number
}

/**
 * One chunk of hits as COLUMNS of int32: IndexedDB's structured clone copies a typed array
 * as raw bytes, so a chunk is stored with no per-hit field names and read back without
 * walking an object graph. Hit i's marks are marks[markEnds[i-1] .. markEnds[i]).
 */
interface PackedChunk {
  n: number
  lineId: Int32Array
  bookId: Int32Array
  score: Int32Array
  wordDistance: Int32Array
  snippetStart: Int32Array
  snippetEnd: Int32Array
  snippetFlags: Int32Array
  markEnds: Int32Array
  marks: Int32Array
}

function pack(hits: readonly FullTextSearchResult[]): PackedChunk {
  const n = hits.length
  const columns = {
    lineId: new Int32Array(n), bookId: new Int32Array(n), score: new Int32Array(n),
    wordDistance: new Int32Array(n), snippetStart: new Int32Array(n), snippetEnd: new Int32Array(n),
    snippetFlags: new Int32Array(n), markEnds: new Int32Array(n),
  }
  let markCount = 0
  for (const hit of hits) markCount += hit.snippetMarks?.length ?? 0
  const marks = new Int32Array(markCount)
  let at = 0
  for (let i = 0; i < n; i++) {
    const hit = hits[i]!
    columns.lineId[i] = hit.lineId
    columns.bookId[i] = hit.bookId
    columns.score[i] = hit.score
    columns.wordDistance[i] = hit.wordDistance ?? 0
    columns.snippetStart[i] = hit.snippetStart
    columns.snippetEnd[i] = hit.snippetEnd
    columns.snippetFlags[i] = hit.snippetFlags ?? 0
    const hitMarks = hit.snippetMarks ?? []
    marks.set(hitMarks, at)
    at += hitMarks.length
    columns.markEnds[i] = at
  }
  return { n, ...columns, marks }
}

function packedBytes(chunk: PackedChunk): number {
  return chunk.lineId.byteLength * 8 + chunk.marks.byteLength
}

function unpackInto(chunk: PackedChunk, out: FullTextSearchResult[]): void {
  let from = 0
  for (let i = 0; i < chunk.n; i++) {
    const to = chunk.markEnds[i]!
    out.push({
      lineId: chunk.lineId[i]!,
      bookId: chunk.bookId[i]!,
      score: chunk.score[i]!,
      wordDistance: chunk.wordDistance[i]!,
      snippetStart: chunk.snippetStart[i]!,
      snippetEnd: chunk.snippetEnd[i]!,
      snippetFlags: chunk.snippetFlags[i]!,
      // A view into the chunk's one marks array — no copy per hit.
      snippetMarks: chunk.marks.subarray(from, to),
    })
    from = to
  }
}

const PREFIX = 'search:'
const META_PREFIX = `${PREFIX}meta:`
const CHUNK_PREFIX = `${PREFIX}c:`
/** The chunk records of the format before packed chunks — dropped on the next clear. */
const LEGACY_CHUNK_PREFIX = `${PREFIX}chunk:`
const NEXT_ID_KEY = `${PREFIX}next-id`
const LRU_KEY = `${PREFIX}lru`
/**
 * Parallel to LRU_KEY — stores the plain normalized query string for each entry in the
 * same positional order as the main LRU array. Used by getRecentQueries so the datalist
 * shows clean text (e.g. "זימון") rather than encoded cache keys (e.g. "זימון|d10|ww").
 */
const DISPLAY_LRU_KEY = `${PREFIX}display-lru`
const MAX = 100
/** Owner (workspace:tab) → the cache key that tab shows. Pinned keys are never evicted. */
const PINS_KEY = `${PREFIX}pins`
/** Total bytes the UNPINNED entries may hold. */
const BYTE_BUDGET = 100 * 1024 * 1024
/** A buffered chunk is written once this many frames have been appended to it... */
const CHUNK_FLUSH_FRAMES = 8
/** ...or once it holds this many hits, whichever comes first. */
const CHUNK_FLUSH_HITS = 4096

/**
 * The search being written right now: its meta kept in memory and the hits buffered for
 * the next chunk. One at a time — a new init/resume replaces it.
 */
interface ActiveSearchCacheWrite {
  key: string
  meta: SearchCacheMeta
  /** The search's live term table — copied into the meta at each chunk write. */
  terms: string[]
  bufferedHits: FullTextSearchResult[]
  bufferedFrames: number
}
let activeWrite: ActiveSearchCacheWrite | null = null

/** Every IDB write of the active search joins this chain, so chunk i always lands before
 *  chunk i+1 and before the meta that counts it. Never rejects — a failed write is logged
 *  and the chain goes on, so one bad put cannot wedge every later cache operation. */
let writeChain: Promise<void> = Promise.resolve()
/** Set when a queued write of the active search failed: its entry is then missing a chunk
 *  and is deleted rather than counted. Reset by init/resume. */
let activeWriteFailed = false
function enqueueWrite(write: () => Promise<void>): void {
  writeChain = writeChain.then(write).catch((err) => {
    activeWriteFailed = true
    console.warn('[searchCache] write failed:', err)
  })
}

/** Move the active search's buffered hits into the write chain as the next chunk, then
 *  the meta that counts it — so what is on disk is always a readable prefix. */
function flushBufferedChunk(): void {
  const active = activeWrite
  if (!active || active.bufferedHits.length === 0) return
  const chunk = pack(active.bufferedHits)
  const index = active.meta.chunks
  active.bufferedHits = []
  active.bufferedFrames = 0
  active.meta.chunks = index + 1
  active.meta.bytes = (active.meta.bytes ?? 0) + packedBytes(chunk)
  const meta: SearchCacheMeta = { ...active.meta, terms: active.terms.slice() }
  const key = active.key
  enqueueWrite(() => idbSet(chunkKey(meta.id, index), chunk))
  enqueueWrite(async () => {
    if (!activeWriteFailed) return idbSet(metaKey(key), meta)
    // A chunk is missing: drop the entry, and stop writing this search — later chunks
    // would land under an id no meta names any more, and never be reclaimed.
    if (activeWrite?.key === key) activeWrite = null
    await deleteEntry(key)
    await idbDeleteByPrefix(`${CHUNK_PREFIX}${meta.id}:`)
  })
}

/** Drop the active search (buffered hits included) and wait for its queued writes. */
async function settleActiveWrite(): Promise<void> {
  activeWrite = null
  await writeChain
}

function metaKey(query: string) {
  return `${META_PREFIX}${query}`
}
function chunkKey(id: number, index: number) {
  return `${CHUNK_PREFIX}${id}:${index}`
}

async function nextId(): Promise<number> {
  const id = (await idbGet<number>(NEXT_ID_KEY)) ?? 1
  await idbSet(NEXT_ID_KEY, id + 1)
  return id
}

async function getLru(): Promise<string[]> {
  return (await idbGet<string[]>(LRU_KEY)) ?? []
}

async function getDisplayLru(): Promise<string[]> {
  return (await idbGet<string[]>(DISPLAY_LRU_KEY)) ?? []
}

function touchLru(key: string, displayQuery: string): Promise<void> {
  return serial(async () => {
  const [lru, displayLru] = await Promise.all([getLru(), getDisplayLru()])
  const existingIndex = lru.indexOf(key)
  const updatedLru = [...lru.filter((k) => k !== key), key]
  // Keep display LRU in sync: remove the old entry at the same position, append new one
  const updatedDisplayLru =
    existingIndex !== -1
      ? [...displayLru.filter((_, index) => index !== existingIndex), displayQuery]
      : [...displayLru, displayQuery]
  await Promise.all([idbSet(LRU_KEY, updatedLru), idbSet(DISPLAY_LRU_KEY, updatedDisplayLru)])
  })
}

/** Delete a query's meta record and every one of its chunk records. */
async function deleteEntry(key: string): Promise<void> {
  const meta = await idbGet<SearchCacheMeta>(metaKey(key))
  await Promise.all([
    idbDelete(metaKey(key)),
    // Trailing ':' scopes the prefix to THIS id's chunks — without it, id 1 would also
    // match the chunks of id 10.
    meta?.id != null ? idbDeleteByPrefix(`${CHUNK_PREFIX}${meta.id}:`) : Promise.resolve(),
  ])
}

/**
 * Every read-modify-write of the LRU lists and the pins runs on this one chain, so two of
 * them never interleave: no lost LRU slot, no display list drifting out of step, and no
 * eviction that misses a pin made a moment earlier. Never nest a serial() inside another.
 */
let lruChain: Promise<unknown> = Promise.resolve()
function serial<T>(work: () => Promise<T>): Promise<T> {
  const run = lruChain.then(work)
  lruChain = run.catch(() => {})
  return run
}

function updatePins(change: (pins: Record<string, string>) => void): Promise<void> {
  return serial(async () => {
    const pins = (await idbGet<Record<string, string>>(PINS_KEY)) ?? {}
    change(pins)
    await idbSet(PINS_KEY, pins)
  })
}

/** Keys that must not be evicted: every open tab's entry, and the one being written. */
async function pinnedKeys(): Promise<Set<string>> {
  const pins = await idbGet<Record<string, string>>(PINS_KEY)
  const pinned = new Set(Object.values(pins ?? {}))
  if (activeWrite) pinned.add(activeWrite.key)
  return pinned
}

/** Evict unpinned entries, least recently used first, until they fit in BYTE_BUDGET. */
function enforceByteBudget(): Promise<void> {
  return serial(async () => {
  const [lru, pinned] = await Promise.all([getLru(), pinnedKeys()])
  const unpinned = lru.filter((k) => !pinned.has(k))
  const sizes = await Promise.all(unpinned.map((k) => entryBytes(k)))
  let total = sizes.reduce((a, b) => a + b, 0)
  for (let i = 0; i < unpinned.length && total > BYTE_BUDGET; i++) {
    await evictEntry(unpinned[i]!)
    total -= sizes[i]!
  }
  })
}

/** An entry's bytes on disk. A meta from before the budget has no count: its chunks are
 *  measured once and the count written back. */
async function entryBytes(key: string): Promise<number> {
  const meta = await idbGet<SearchCacheMeta>(metaKey(key))
  if (!meta || meta.id == null) return 0
  if (meta.bytes != null) return meta.bytes
  const chunks = await Promise.all(
    Array.from({ length: meta.chunks }, (_, i) => idbGet<PackedChunk>(chunkKey(meta.id, i))),
  )
  const bytes = chunks.reduce((sum, chunk) => sum + (chunk ? packedBytes(chunk) : 0), 0)
  await idbSet(metaKey(key), { ...meta, bytes })
  return bytes
}

/** Drop one entry and its slot in both LRU lists. */
async function evictEntry(key: string): Promise<void> {
  const [lru, displayLru] = await Promise.all([getLru(), getDisplayLru()])
  const index = lru.indexOf(key)
  await Promise.all([
    deleteEntry(key),
    idbSet(LRU_KEY, lru.filter((k) => k !== key)),
    idbSet(DISPLAY_LRU_KEY, index !== -1 ? displayLru.filter((_, i) => i !== index) : displayLru),
  ])
}

function evictIfNeeded(key: string): Promise<void> {
  return serial(async () => {
  const [lru, displayLru, pinned] = await Promise.all([getLru(), getDisplayLru(), pinnedKeys()])
  const without = lru.filter((k) => k !== key)
  if (without.length < MAX) return
  // Evict the least-recently-used entry no open tab shows. All pinned: keep them, go over MAX.
  const evictKey = without.find((k) => !pinned.has(k))
  if (evictKey === undefined) return
  // The two lists are index-parallel over the UNFILTERED lru, so the display entry to drop
  // is the one at the evicted key's index THERE. displayLru.slice(1) is only the same thing
  // while `key` is not itself at index 0; when it is, the two lists shift apart by one and
  // stay that way, and getRecentQueries then shows query text belonging to another entry.
  const evictIndex = lru.indexOf(evictKey)
  const updatedDisplayLru =
    evictIndex !== -1 ? displayLru.filter((_, i) => i !== evictIndex) : displayLru.slice(1)
  await Promise.all([
    deleteEntry(evictKey),
    idbSet(LRU_KEY, lru.filter((k) => k !== evictKey)),
    idbSet(DISPLAY_LRU_KEY, updatedDisplayLru),
  ])
  })
}

export const useSearchCacheStore = defineStore('searchCache', () => {
  async function get(key: string, displayQuery: string): Promise<SearchCacheEntry | null> {
    // Queued chunk writes must land before they are read.
    await writeChain
    const meta = await idbGet<SearchCacheMeta>(metaKey(key))
    // No meta, or one from before packed chunks (no id): a miss — the caller searches.
    if (!meta || meta.id == null) return null
    // Read every chunk in one go, concatenated in write order (the exact streamed order).
    const chunks = await Promise.all(
      Array.from({ length: meta.chunks }, (_, i) => idbGet<PackedChunk>(chunkKey(meta.id, i))),
    )
    const results: FullTextSearchResult[] = []
    for (const chunk of chunks) {
      if (!chunk) break   // a missing chunk ends the readable prefix
      unpackInto(chunk, results)
    }
    void touchLru(key, displayQuery)
    return { results, terms: meta.terms ?? [], complete: meta.complete, indexingComplete: meta.indexingComplete }
  }

  /** Start a fresh entry for a new search (any earlier one under this key is dropped). */
  async function init(key: string, displayQuery: string, indexingComplete: boolean, terms: string[]): Promise<void> {
    // A previous search's queued chunk writes must not land after this key's wipe below.
    await settleActiveWrite()
    await evictIfNeeded(key)
    await deleteEntry(key)
    const meta: SearchCacheMeta = { id: await nextId(), chunks: 0, complete: false, indexingComplete, terms: [] }
    await idbSet(metaKey(key), meta)
    await touchLru(key, displayQuery)
    activeWriteFailed = false
    activeWrite = { key, meta, terms, bufferedHits: [], bufferedFrames: 0 }
  }

  /**
   * Continue writing an existing, unfinished entry — a RESUMED search appends after the
   * chunks already stored. False when there is no entry to continue (the caller inits).
   */
  async function resume(key: string, terms: string[]): Promise<boolean> {
    await settleActiveWrite()
    const meta = await idbGet<SearchCacheMeta>(metaKey(key))
    if (!meta || meta.id == null) return false
    activeWriteFailed = false
    activeWrite = { key, meta: { ...meta }, terms, bufferedHits: [], bufferedFrames: 0 }
    return true
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

  /** The stream stopped before finishing (cancelled, tab left): write what is buffered so
   *  the entry holds every hit received, ready to resume. */
  function flush(key: string): void {
    if (activeWrite?.key === key) flushBufferedChunk()
  }

  /** The stream finished: write the last chunk, then the meta marked complete. */
  async function markComplete(key: string): Promise<void> {
    const active = activeWrite
    if (!active || active.key !== key) return
    flushBufferedChunk()
    const meta: SearchCacheMeta = { ...active.meta, complete: true, terms: active.terms.slice() }
    enqueueWrite(() => (activeWriteFailed ? deleteEntry(key) : idbSet(metaKey(key), meta)))
    await settleActiveWrite()
    await enforceByteBudget()
  }

  /** The tab `owner` (workspace:tab) now shows `key`: pin it, and release what it showed before. */
  async function pin(owner: string, key: string): Promise<void> {
    let released = false
    await updatePins((pins) => {
      released = pins[owner] !== undefined && pins[owner] !== key
      pins[owner] = key
    })
    if (released) await enforceByteBudget()
  }

  /** The tab `owner` closed: its entry becomes an ordinary one, subject to the budget. */
  async function unpin(owner: string): Promise<void> {
    await updatePins((pins) => { delete pins[owner] })
    await enforceByteBudget()
  }

  /** A workspace was deleted: release every pin its tabs held (owners `<prefix>...`). */
  async function unpinAllWithPrefix(prefix: string): Promise<void> {
    await updatePins((pins) => {
      for (const owner of Object.keys(pins)) if (owner.startsWith(prefix)) delete pins[owner]
    })
    await enforceByteBudget()
  }

  async function clear(): Promise<void> {
    await settleActiveWrite()
    const lru = await getLru()
    await Promise.all([
      ...lru.map((k) => deleteEntry(k)),
      idbDeleteByPrefix(LEGACY_CHUNK_PREFIX),
      idbDelete(LRU_KEY),
      idbDelete(DISPLAY_LRU_KEY),
    ])
  }

  /** Remove a single entry and evict it from both LRU lists. */
  async function remove(key: string): Promise<void> {
    // The active search's queued chunks must land before the wipe, not after it.
    if (activeWrite?.key === key) await settleActiveWrite()
    await serial(() => evictEntry(key))
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

  return { get, init, resume, appendBatch, flush, markComplete, clear, remove, getRecentQueries, pin, unpin, unpinAllWithPrefix }
})
