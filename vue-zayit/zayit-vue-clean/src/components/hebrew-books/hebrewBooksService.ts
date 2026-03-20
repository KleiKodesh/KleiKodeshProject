export interface HebrewBook {
  id: string
  title: string
  author: string
  printingPlace: string
  printingYear: string
  pages: string
  _csvTags: string
  lastAccessed?: number
}

interface HistoryEntry extends HebrewBook {
  lastAccessed: number
}

class HebrewBooksService {
  private books: HebrewBook[] = []
  private catalogLoaded = false
  private catalogLoading = false
  private db: IDBDatabase | null = null

  async loadCatalog(): Promise<void> {
    if (this.catalogLoaded || this.catalogLoading) return
    this.catalogLoading = true
    try {
      const text = await fetch('/HebrewBooks.csv').then(r => r.text())
      this.books = text
        .split('\n')
        .map(line => line.split(','))
        .filter(row => row.length >= 6 && row[1]?.trim())
        .map(row => ({
          id: row[0]?.trim() ?? '',
          title: row[1]?.trim() ?? '',
          author: row[2]?.trim() ?? '',
          printingPlace: row[3]?.trim() ?? '',
          printingYear: row[4]?.trim() ?? '',
          pages: row[5]?.trim() ?? '',
          _csvTags: row[6]?.trim() ?? '',
        }))
      this.catalogLoaded = true
    } finally {
      this.catalogLoading = false
    }
  }

  search(term: string): HebrewBook[] {
    if (!term.trim() || !this.catalogLoaded) return []
    const terms = term.toLowerCase().trim().split(' ').filter(t => t.length > 1)
    if (!terms.length) return []
    return this.books
      .filter(b => terms.every(t =>
        b.title.toLowerCase().includes(t) ||
        b.author.toLowerCase().includes(t) ||
        b._csvTags.toLowerCase().includes(t)
      ))
      .sort((a, b) => a.title.localeCompare(b.title))
  }

  async getHistory(): Promise<HebrewBook[]> {
    await this.initDB()
    const entries = await this.getAllHistory()
    return entries.map(e => ({ ...e }))
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
          const sorted = (all.result as HistoryEntry[]).sort((a, b) => a.lastAccessed - b.lastAccessed)
          sorted.slice(0, countReq.result - 25).forEach(e => store.delete(e.id))
        }
      }
    }
  }

  private async initDB(): Promise<void> {
    if (this.db) return
    return new Promise((resolve, reject) => {
      const req = indexedDB.open('HBHistory', 1)
      req.onerror = () => reject(req.error)
      req.onsuccess = () => { this.db = req.result; resolve() }
      req.onupgradeneeded = e => {
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
      req.onsuccess = () => resolve((req.result as HistoryEntry[]).sort((a, b) => b.lastAccessed - a.lastAccessed))
      req.onerror = () => reject(req.error)
    })
  }
}

export const hebrewBooksService = new HebrewBooksService()
