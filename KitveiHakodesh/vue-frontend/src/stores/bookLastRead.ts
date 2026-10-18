import { dbGet, dbSet, dbDelete, dbCount, dbListEntries } from '@/utils/persistence'

/**
 * The `app-lastread` slice of persistence — where the reader last was in a book.
 *
 * Keyed by book id alone: global, not per tab and not per workspace. It deliberately
 * outlives tab close, which is what makes it different from the `BookState` in
 * `tabStatePersistence.ts` (same shape, but scoped to one tab's view of the book).
 *
 * Owns both caps: the in-memory one below, and the 1000-entry on-disk one enforced
 * by `writeLastRead`. Always write through `setLastReadPos` so both are applied.
 */

const LASTREAD_DB = 'app-lastread'

export interface LastReadState {
  scrollIndex: number
  scrollOffset: number
  selectedLineId?: number | null
  /**
   * Main text zoom. Carried here as well as on `BookState` because the commentary
   * panels' zoom already rides along inside `commentaryPanels` — without this,
   * reopening a book restored the commentary zoom but reset the text to default.
   */
  zoom?: number
  /** Both commentary panels' saved place, keyed by slot. */
  commentaryPanels?: import('@/features/book-view/bookViewTypes').CommentaryPanelPersistStates
  /** The TOC side panel, so reopening a book restores the panel too. */
  toc?: import('@/features/book-view/bookViewTypes').TocPersistState
  /**
   * The alternate version the book was last read in, by `book_version.versionTitle`.
   * Absent or null = the book's own merged text.
   *
   * The TITLE, not the row id: ids are local to whichever seforim DB is open and a
   * library update can renumber them, which would silently restore a DIFFERENT text
   * under the same number. The title is the upstream key and survives that. A title
   * that no longer resolves simply falls back to the merged text.
   */
  versionTitle?: string | null
  /**
   * When this position was last written. The on-disk cap evicts by it — without a
   * timestamp the only order available is the key cursor's (lexicographic by book id),
   * which would drop whichever books happen to sort lowest, not the stale ones.
   * Absent on entries written before this field existed; those evict first.
   */
  savedAt?: number
}

// ── On-disk store (LRU-capped) ────────────────────────────────────────────────

/** On-disk cap. Every book the user has ever opened lands here, so it needs a bound. */
const DISK_MAX = 1000

// In-memory UPPER BOUND on the number of lastread entries, so a save costs one put and
// no probe. -1 = unknown; the first write counts once. After that every book id not yet
// written this session bumps the bound (it MAY already exist on disk - that is what
// makes it a bound, not a count), and only when the bound reaches the cap is the real
// count taken before evicting. Ids written this session are known to exist.
let diskCount = -1
const writtenThisSession = new Set<number>()

function readLastRead(bookId: number): Promise<LastReadState | null> {
  return dbGet<LastReadState>(LASTREAD_DB, `lastread:${bookId}`)
}

async function writeLastRead(bookId: number, value: LastReadState): Promise<void> {
  const key = `lastread:${bookId}`

  await dbSet(LASTREAD_DB, key, { ...value, savedAt: Date.now() })

  if (!writtenThisSession.has(bookId)) {
    writtenThisSession.add(bookId)
    diskCount = diskCount === -1 ? await dbCount(LASTREAD_DB) : diskCount + 1
  }

  if (diskCount <= DISK_MAX) return
  // The bound reached the cap: take the real count before deciding to evict.
  diskCount = await dbCount(LASTREAD_DB)
  if (diskCount <= DISK_MAX) return

  // Over the cap — evict the LEAST RECENTLY SAVED entries. Reading every value is only
  // paid on the write that crosses the cap, and ranking demands the values: key order is
  // lexicographic, so evicting by it would delete the lowest-sorting book ids (very
  // possibly the ones being read right now) and keep whatever sorts high.
  const entries = await dbListEntries<LastReadState>(LASTREAD_DB)
  entries.sort((a, b) => (a[1]?.savedAt ?? 0) - (b[1]?.savedAt ?? 0))
  const keysToDelete = entries.slice(0, diskCount - DISK_MAX).map(([k]) => k)
  await Promise.all(keysToDelete.map((k) => dbDelete(LASTREAD_DB, k)))
  // An evicted id written again later is a NEW entry and must bump the bound again.
  for (const k of keysToDelete) writtenThisSession.delete(Number(k.slice('lastread:'.length)))
  diskCount -= keysToDelete.length
}

// ── In-memory cache ───────────────────────────────────────────────────────────

/**
 * In-memory cache. Not tied to any lifecycle — a book's last-read position stays
 * relevant after its tab closes — so it needs an explicit size cap instead.
 */
const lastReadCache = new Map<number, LastReadState | null>()

/** Cap for the in-memory cache only; the on-disk cap is `DISK_MAX` above. */
const MEMORY_CACHE_MAX = 200

/** A tab being opened awaits this before reading, so the previous write has committed. */
let pendingLastReadSave: Promise<void> | null = null

export function getLastReadPos(bookId: number): Promise<LastReadState | null> {
  if (lastReadCache.has(bookId)) return Promise.resolve(lastReadCache.get(bookId)!)
  const read = async () => {
    const value = await readLastRead(bookId)
    lastReadCache.set(bookId, value)
    return value
  }
  return pendingLastReadSave ? pendingLastReadSave.then(read) : read()
}

export function setLastReadPos(bookId: number, position: LastReadState): Promise<void> {
  lastReadCache.set(bookId, position)
  // FIFO eviction — Map iterates in insertion order, so the first key is the oldest.
  if (lastReadCache.size > MEMORY_CACHE_MAX) {
    lastReadCache.delete(lastReadCache.keys().next().value!)
  }
  pendingLastReadSave = writeLastRead(bookId, position)
  return pendingLastReadSave
}
