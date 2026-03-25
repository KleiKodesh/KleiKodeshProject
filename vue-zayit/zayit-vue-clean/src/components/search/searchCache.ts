/**
 * Search result cache — uses a dedicated IDB database separate from app-state.
 * LRU-capped at 100 entries.
 */
import type { BloomSearchResult } from './searchTypes'

interface CachedSearch {
  query: string
  results: BloomSearchResult[]
  timestamp: number
}

const DB_NAME = 'ZayitSearchCache'
const STORE   = 'searches'
const MAX     = 100

let db: IDBDatabase | null = null

function open(): Promise<IDBDatabase> {
  if (db) return Promise.resolve(db)
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, 1)
    req.onupgradeneeded = () => {
      const d = req.result
      if (!d.objectStoreNames.contains(STORE)) {
        const s = d.createObjectStore(STORE, { keyPath: 'query' })
        s.createIndex('timestamp', 'timestamp', { unique: false })
      }
    }
    req.onsuccess = () => { db = req.result; resolve(db) }
    req.onerror  = () => reject(req.error)
  })
}

export async function cacheGet(query: string): Promise<BloomSearchResult[] | null> {
  const idb = await open()
  return new Promise((resolve, reject) => {
    const req = idb.transaction(STORE).objectStore(STORE).get(query)
    req.onsuccess = () => {
      const hit = req.result as CachedSearch | undefined
      if (hit) {
        // refresh timestamp async
        cacheSet(query, hit.results).catch(() => {})
        resolve(hit.results)
      } else {
        resolve(null)
      }
    }
    req.onerror = () => reject(req.error)
  })
}

export async function cacheSet(query: string, results: BloomSearchResult[]): Promise<void> {
  const idb = await open()
  // evict oldest if at cap
  const count = await new Promise<number>((res, rej) => {
    const r = idb.transaction(STORE).objectStore(STORE).count()
    r.onsuccess = () => res(r.result)
    r.onerror   = () => rej(r.error)
  })
  if (count >= MAX) {
    await new Promise<void>((res, rej) => {
      const r = idb.transaction(STORE, 'readwrite').objectStore(STORE).index('timestamp').openCursor()
      r.onsuccess = () => { r.result?.delete(); res() }
      r.onerror   = () => rej(r.error)
    })
  }
  await new Promise<void>((res, rej) => {
    const r = idb.transaction(STORE, 'readwrite').objectStore(STORE)
      .put({ query, results, timestamp: Date.now() } satisfies CachedSearch)
    r.onsuccess = () => res()
    r.onerror   = () => rej(r.error)
  })
}

export async function cacheClear(): Promise<void> {
  const idb = await open()
  return new Promise((res, rej) => {
    const r = idb.transaction(STORE, 'readwrite').objectStore(STORE).clear()
    r.onsuccess = () => res()
    r.onerror   = () => rej(r.error)
  })
}
