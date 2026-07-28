import { idbGetLastRead, idbSetLastRead } from '@/utils/persistence'
import type { LastReadState } from '@/utils/persistence'

/**
 * The `app-lastread` slice of persistence — where the reader last was in a book.
 *
 * Keyed by book id alone: global, not per tab and not per workspace. It deliberately
 * outlives tab close, which is what makes it different from the `BookState` in
 * `tabStatePersistence.ts` (same shape, but scoped to one tab's view of the book).
 *
 * On-disk entries are LRU-capped at 1000 by `idbSetLastRead`. Always write through
 * `setLastReadPos` so that cap is enforced.
 */

/**
 * In-memory cache. Not tied to any lifecycle — a book's last-read position stays
 * relevant after its tab closes — so it needs an explicit size cap instead.
 */
const lastReadCache = new Map<number, LastReadState | null>()

/** Cap for the in-memory cache only; the on-disk cap of 1000 lives in `idbSetLastRead`. */
const MEMORY_CACHE_MAX = 200

/** A tab being opened awaits this before reading, so the previous write has committed. */
let pendingLastReadSave: Promise<void> | null = null

export function getLastReadPos(bookId: number): Promise<LastReadState | null> {
  if (lastReadCache.has(bookId)) return Promise.resolve(lastReadCache.get(bookId)!)
  const read = async () => {
    const value = await idbGetLastRead(bookId)
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
  pendingLastReadSave = idbSetLastRead(bookId, position)
  return pendingLastReadSave
}
