/**
 * HebrewBooks download history store.
 * Owns the app-hb-history IDB database entirely — type, schema, open, read, write.
 * No other file may access this database.
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

async function idbGetHistory(): Promise<HbHistoryEntry[]> {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const req = db.transaction('history', 'readonly').objectStore('history').getAll()
    req.onsuccess = () =>
      resolve((req.result as HbHistoryEntry[]).sort((a, b) => b.lastAccessed - a.lastAccessed))
    req.onerror = () => reject(req.error)
  })
}

async function idbTrackAccess(entry: HbHistoryEntry): Promise<void> {
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

// ── Store ─────────────────────────────────────────────────────────────────────

export const useHebrewBooksHistoryStore = defineStore('hebrewBooksHistory', () => {
  function getHistory(): Promise<HebrewBook[]> {
    return idbGetHistory() as Promise<HebrewBook[]>
  }

  function trackAccess(book: HebrewBook): Promise<void> {
    const entry: HbHistoryEntry = { ...book, lastAccessed: Date.now() }
    return idbTrackAccess(entry)
  }

  return { getHistory, trackAccess }
})
