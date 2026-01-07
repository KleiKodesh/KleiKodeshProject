interface HistoryEntry {
  id: string // Book ID
  title: string
  author: string
  accessCount: number
  lastAccessed: number
  firstAccessed: number
}

export class HistoryService {
  private static readonly DB_NAME = 'HebrewBooksHistory'
  private static readonly DB_VERSION = 1
  private static readonly STORE_NAME = 'bookHistory'
  private static readonly MAX_HISTORY_ITEMS = 100 // Cap for history items

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
          store.createIndex('accessCount', 'accessCount', { unique: false })
        }
      }
    })
  }

  // Track book interaction
  static async trackBookInteraction(bookId: string, title: string, author: string): Promise<void> {
    try {
      await this.initDB()
      if (!this.db) throw new Error('Database not initialized')

      const transaction = this.db.transaction([this.STORE_NAME], 'readwrite')
      const store = transaction.objectStore(this.STORE_NAME)

      // Get existing entry
      const getRequest = store.get(bookId)

      getRequest.onsuccess = async () => {
        const existingEntry = getRequest.result as HistoryEntry | undefined
        const now = Date.now()

        const entry: HistoryEntry = existingEntry ? {
          ...existingEntry,
          accessCount: existingEntry.accessCount + 1,
          lastAccessed: now
        } : {
          id: bookId,
          title,
          author,
          accessCount: 1,
          lastAccessed: now,
          firstAccessed: now
        }

        // Update the entry
        store.put(entry)

        // Clean up old entries if we exceed the cap
        await this.cleanupOldEntries()
      }

      getRequest.onerror = () => {
        console.error('Failed to get history entry:', getRequest.error)
      }
    } catch (error) {
      console.error('Failed to track book interaction:', error)
    }
  }

  // Get all history entries
  static async getAllHistory(): Promise<HistoryEntry[]> {
    try {
      await this.initDB()
      if (!this.db) return []

      return new Promise((resolve, reject) => {
        const transaction = this.db!.transaction([this.STORE_NAME], 'readonly')
        const store = transaction.objectStore(this.STORE_NAME)
        const request = store.getAll()

        request.onsuccess = () => {
          resolve(request.result as HistoryEntry[])
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

  // Clean up old entries to maintain the cap
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

            // Delete the oldest entries to get back under the cap
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

  // Calculate popularity score based on access patterns
  static calculatePopularityScore(entry: HistoryEntry): number {
    const now = Date.now()
    const daysSinceLastAccess = (now - entry.lastAccessed) / (1000 * 60 * 60 * 24)
    const daysSinceFirstAccess = (now - entry.firstAccessed) / (1000 * 60 * 60 * 24)

    // Base score from access count
    let score = entry.accessCount * 10

    // Recency bonus (more recent = higher score)
    if (daysSinceLastAccess < 1) score += 50      // Last day
    else if (daysSinceLastAccess < 7) score += 25 // Last week
    else if (daysSinceLastAccess < 30) score += 10 // Last month

    // Frequency bonus (more frequent over time = higher score)
    if (daysSinceFirstAccess > 0) {
      const accessFrequency = entry.accessCount / daysSinceFirstAccess
      score += accessFrequency * 20
    }

    return Math.round(score)
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