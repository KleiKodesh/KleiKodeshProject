interface HistoryEntry {
  id: string // Book ID
  title: string
  author: string
  lastAccessed: number // Only timestamp matters for LRU
}

export class HistoryService {
  private static readonly DB_NAME = 'HebrewBooksHistory'
  private static readonly DB_VERSION = 1
  private static readonly STORE_NAME = 'bookHistory'
  private static readonly MAX_HISTORY_ITEMS = 100 // LRU cap

  private static db: IDBDatabase | null = null

  // Initialize IndexedDB
  static async initDB(): Promise<void> {
    if (this.db) return

    return new Promise((resolve, reject) => {
      const request = indexedDB.open(this.DB_NAME, this.DB_VERSION)

      request.onerror = () => {
        console.error('Failed to open IndexedDB:', request.error)
        reject(request.error)
      }

      request.onsuccess = () => {
        this.db = request.result
        resolve()
      }

      request.onupgradeneeded = (event) => {
        const db = (event.target as IDBOpenDBRequest).result

        // Create object store if it doesn't exist
        if (!db.objectStoreNames.contains(this.STORE_NAME)) {
          const store = db.createObjectStore(this.STORE_NAME, { keyPath: 'id' })
          store.createIndex('lastAccessed', 'lastAccessed', { unique: false })
        }
      }
    })
  }

  // Track book interaction - simple LRU: update timestamp, jump to top
  static async trackBookInteraction(bookId: string, title: string, author: string): Promise<void> {
    try {
      await this.initDB()
      if (!this.db) throw new Error('Database not initialized')

      const transaction = this.db.transaction([this.STORE_NAME], 'readwrite')
      const store = transaction.objectStore(this.STORE_NAME)

      const entry: HistoryEntry = {
        id: bookId,
        title,
        author,
        lastAccessed: Date.now()
      }

      // Put will update if exists, insert if new
      store.put(entry)

      // Clean up old entries if we exceed the cap
      await this.cleanupOldEntries()
    } catch (error) {
      console.error('Failed to track book interaction:', error)
    }
  }

  // Get all history entries sorted by LRU (most recent first)
  static async getAllHistory(): Promise<HistoryEntry[]> {
    try {
      await this.initDB()
      if (!this.db) return []

      return new Promise((resolve, reject) => {
        const transaction = this.db!.transaction([this.STORE_NAME], 'readonly')
        const store = transaction.objectStore(this.STORE_NAME)
        const request = store.getAll()

        request.onsuccess = () => {
          const entries = request.result as HistoryEntry[]
          // Sort by most recent first (LRU order)
          entries.sort((a, b) => b.lastAccessed - a.lastAccessed)
          resolve(entries)
        }

        request.onerror = () => {
          console.error('Failed to get history:', request.error)
          reject(request.error)
        }
      })
    } catch (error) {
      console.error('Failed to get history:', error)
      return []
    }
  }

  // Get history entry for a specific book
  static async getBookHistory(bookId: string): Promise<HistoryEntry | null> {
    try {
      await this.initDB()
      if (!this.db) return null

      return new Promise((resolve, reject) => {
        const transaction = this.db!.transaction([this.STORE_NAME], 'readonly')
        const store = transaction.objectStore(this.STORE_NAME)
        const request = store.get(bookId)

        request.onsuccess = () => {
          resolve(request.result as HistoryEntry || null)
        }

        request.onerror = () => {
          console.error('Failed to get book history:', request.error)
          reject(request.error)
        }
      })
    } catch (error) {
      console.error('Failed to get book history:', error)
      return null
    }
  }

  // Clean up old entries to maintain LRU cap
  private static async cleanupOldEntries(): Promise<void> {
    try {
      if (!this.db) return

      const transaction = this.db.transaction([this.STORE_NAME], 'readwrite')
      const store = transaction.objectStore(this.STORE_NAME)
      const countRequest = store.count()

      countRequest.onsuccess = () => {
        const count = countRequest.result

        if (count > this.MAX_HISTORY_ITEMS) {
          // Get all entries sorted by last accessed (oldest first)
          const index = store.index('lastAccessed')
          const getAllRequest = index.getAll()

          getAllRequest.onsuccess = () => {
            const entries = getAllRequest.result as HistoryEntry[]
            entries.sort((a, b) => a.lastAccessed - b.lastAccessed)

            // Delete the oldest entries (LRU eviction)
            const entriesToDelete = entries.slice(0, count - this.MAX_HISTORY_ITEMS)

            entriesToDelete.forEach(entry => {
              store.delete(entry.id)
            })
          }
        }
      }
    } catch (error) {
      console.error('Failed to cleanup old entries:', error)
    }
  }

  // Clear all history (for privacy/reset purposes)
  static async clearAllHistory(): Promise<void> {
    try {
      await this.initDB()
      if (!this.db) return

      const transaction = this.db.transaction([this.STORE_NAME], 'readwrite')
      const store = transaction.objectStore(this.STORE_NAME)
      store.clear()
    } catch (error) {
      console.error('Failed to clear history:', error)
    }
  }
}