import type { HebrewBook } from './hebrewBooksCatalog'

interface HistoryEntry extends HebrewBook {
  lastAccessed: number
}

class HebrewBooksHistory {
  private db: IDBDatabase | null = null

  async getHistory(): Promise<HebrewBook[]> {
    await this.initDB()
    return this.getAllHistory()
  }

  async trackAccess(book: HebrewBook): Promise<void> {
    await this.initDB()
    if (!this.db) return
    const tx = this.db.transaction(['history'], 'readwrite')
    const store = tx.objectStore('history')
    store.put({ ...book, lastAccessed: Date.now() } satisfies HistoryEntry)
    const countReq = store.count()
    countReq.onsuccess = () => {
      if (countReq.result > 25) {
        const all = store.getAll()
        all.onsuccess = () => {
          const sorted = (all.result as HistoryEntry[]).sort(
            (a, b) => a.lastAccessed - b.lastAccessed,
          )
          sorted.slice(0, countReq.result - 25).forEach((e) => store.delete(e.id))
        }
      }
    }
  }

  private async initDB(): Promise<void> {
    if (this.db) return
    return new Promise((resolve, reject) => {
      const req = indexedDB.open('HBHistory', 1)
      req.onerror = () => reject(req.error)
      req.onsuccess = () => {
        this.db = req.result
        resolve()
      }
      req.onupgradeneeded = (e) => {
        const db = (e.target as IDBOpenDBRequest).result
        if (!db.objectStoreNames.contains('history')) {
          const s = db.createObjectStore('history', { keyPath: 'id' })
          s.createIndex('lastAccessed', 'lastAccessed')
        }
      }
    })
  }

  private async getAllHistory(): Promise<HistoryEntry[]> {
    if (!this.db) return []
    return new Promise((resolve, reject) => {
      const req = this.db!.transaction(['history'], 'readonly').objectStore('history').getAll()
      req.onsuccess = () =>
        resolve((req.result as HistoryEntry[]).sort((a, b) => b.lastAccessed - a.lastAccessed))
      req.onerror = () => reject(req.error)
    })
  }
}

export const hebrewBooksHistory = new HebrewBooksHistory()
