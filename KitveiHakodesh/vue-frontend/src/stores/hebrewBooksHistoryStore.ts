/**
 * HebrewBooks download history store.
 * Owns the app-hb-history IDB database entirely — type, schema, open, read, write.
 * No other file may access this database.
 *
 * The full history (max 25 entries) is kept in memory after the first IDB read.
 * All subsequent reads are synchronous from the in-memory cache.
 * IDB is only touched on the initial load and on every write.
 */
import { defineStore } from 'pinia'
import type { HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'

// ── IDB setup ─────────────────────────────────────────────────────────────────

const HB_HISTORY_DB = 'app-hb-history'
const HB_HISTORY_MAX = 25

export interface HbHistoryEntry extends HebrewBook {
  lastAccessed: number
}

let _db: IDBDatabase | null = null

function openDb(): Promise<IDBDatabase> {
  if (_db) return Promise.resolve(_db)
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(HB_HISTORY_DB, 1)
    req.onupgradeneeded = () => {
      const db = req.result
      if (!db.objectStoreNames.contains('history')) {
        const store = db.createObjectStore('history', { keyPath: 'id' })
        store.createIndex('lastAccessed', 'lastAccessed')
      }
    }
    req.onsuccess = () => {
      _db = req.result
      resolve(_db)
    }
    req.onerror = () => reject(req.error)
  })
}

async function idbLoadAll(): Promise<HbHistoryEntry[]> {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const req = db.transaction('history', 'readonly').objectStore('history').getAll()
    req.onsuccess = () =>
      resolve((req.result as HbHistoryEntry[]).sort((a, b) => b.lastAccessed - a.lastAccessed))
    req.onerror = () => reject(req.error)
  })
}

async function idbPut(entry: HbHistoryEntry): Promise<void> {
  const db = await openDb()
  const tx = db.transaction('history', 'readwrite')
  const store = tx.objectStore('history')
  store.put(entry)
  const countReq = store.count()
  countReq.onsuccess = () => {
    if (countReq.result > HB_HISTORY_MAX) {
      const all = store.getAll()
      all.onsuccess = () => {
        const sorted = (all.result as HbHistoryEntry[]).sort(
          (a, b) => a.lastAccessed - b.lastAccessed,
        )
        sorted.slice(0, countReq.result - HB_HISTORY_MAX).forEach((e) => store.delete(e.id))
      }
    }
  }
}

export function dropHbHistoryDb(): Promise<void> {
  _db?.close()
  _db = null
  return new Promise((resolve, reject) => {
    const req = indexedDB.deleteDatabase(HB_HISTORY_DB)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
    req.onblocked = () => resolve()
  })
}

// ── In-memory cache ───────────────────────────────────────────────────────────
// Sorted newest-first. null means not yet loaded from IDB.
// Max 25 entries — safe to keep entirely in memory for the app lifetime.

let _cache: HbHistoryEntry[] | null = null
let _loadPromise: Promise<HbHistoryEntry[]> | null = null

function ensureLoaded(): Promise<HbHistoryEntry[]> {
  if (_cache !== null) return Promise.resolve(_cache)
  if (_loadPromise) return _loadPromise
  _loadPromise = idbLoadAll().then((entries) => {
    _cache = entries
    _loadPromise = null
    return entries
  })
  return _loadPromise
}

// ── Store ─────────────────────────────────────────────────────────────────────

export const useHebrewBooksHistoryStore = defineStore('hebrewBooksHistory', () => {
  /**
   * Returns the history sorted newest-first.
   * Synchronous from memory after the first call; one IDB read on first call.
   */
  function getHistory(): Promise<HebrewBook[]> {
    return ensureLoaded() as Promise<HebrewBook[]>
  }

  /**
   * Record an access. Updates the in-memory cache immediately, then writes to IDB.
   */
  function trackAccess(book: HebrewBook): Promise<void> {
    const entry: HbHistoryEntry = { ...book, lastAccessed: Date.now() }

    // Update cache immediately so the next getHistory() call is already correct
    if (_cache !== null) {
      // Remove existing entry for this book (if any) then prepend the updated one
      _cache = [entry, ..._cache.filter((e) => e.id !== book.id)]
      // Trim to cap
      if (_cache.length > HB_HISTORY_MAX) {
        _cache = _cache.slice(0, HB_HISTORY_MAX)
      }
    }

    // Fire-and-forget IDB write — the cache is already correct
    return idbPut(entry)
  }

  return { getHistory, trackAccess }
})
