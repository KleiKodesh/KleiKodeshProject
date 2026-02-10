// Service to manage Web Worker for search and recent books operations
import type { HebrewBook } from '../types/HebrewBook'
import { HistoryService } from './hebrewBooksHistoryService'

interface HistoryEntry {
  id: string
  title: string
  author: string
  accessCount: number
  lastAccessed: number
  firstAccessed: number
}

interface SearchMessage {
  type: 'search'
  books: HebrewBook[]
  searchTerm: string
}

interface RecentMessage {
  type: 'recent'
  books: HebrewBook[]
  historyEntries: HistoryEntry[]
}

interface SearchResult {
  type: 'searchResult'
  results: HebrewBook[]
}

interface RecentResult {
  type: 'recentResult'
  results: HebrewBook[]
}

export class HebrewBooksSearchService {
  private worker: Worker | null = null
  private requestId = 0
  private pendingRequests = new Map<number, {
    resolve: (results: HebrewBook[]) => void
    reject: (error: Error) => void
  }>()

  constructor() {
    this.initWorker()
  }

  private initWorker() {
    try {
      // Create worker from the TypeScript file (Vite will handle compilation)
      this.worker = new Worker(
        new URL('../workers/searchWorker.ts', import.meta.url),
        { type: 'module' }
      )

      this.worker.onmessage = (e: MessageEvent<SearchResult | RecentResult>) => {
        const { type, results } = e.data

        if (type === 'searchResult' || type === 'recentResult') {
          // For now, resolve the most recent request (since we cancel previous ones)
          const latestRequest = Array.from(this.pendingRequests.values()).pop()
          if (latestRequest) {
            latestRequest.resolve(results)
          }
          this.pendingRequests.clear()
        }
      }

      this.worker.onerror = (error) => {
        console.error('Search worker error:', error)
        // Reject all pending requests
        this.pendingRequests.forEach(({ reject }) => {
          reject(new Error('Search worker error'))
        })
        this.pendingRequests.clear()
      }
    } catch (error) {
      console.error('Failed to create search worker:', error)
      this.worker = null
    }
  }

  async search(books: HebrewBook[], searchTerm: string): Promise<HebrewBook[]> {
    if (!this.worker) {
      // Fallback to main thread if worker failed to initialize
      return this.fallbackSearch(books, searchTerm)
    }

    // Cancel any pending requests
    this.pendingRequests.forEach(({ reject }) => {
      reject(new Error('Request cancelled'))
    })
    this.pendingRequests.clear()

    return new Promise((resolve, reject) => {
      const currentRequestId = ++this.requestId

      this.pendingRequests.set(currentRequestId, { resolve, reject })

      // Ensure books are cloneable by creating clean copies
      const cloneableBooks = books.map(book => ({
        id: book.id,
        title: book.title,
        author: book.author,
        printingPlace: book.printingPlace,
        printingYear: book.printingYear,
        pages: book.pages,
        userScore: book.userScore,
        lastAccessed: book.lastAccessed,
        _csvTags: book._csvTags
      }))

      const message: SearchMessage = {
        type: 'search',
        books: cloneableBooks,
        searchTerm
      }

      this.worker!.postMessage(message)
    })
  }

  async getRecentBooks(books: HebrewBook[], historyEntries: HistoryEntry[]): Promise<HebrewBook[]> {
    if (!this.worker) {
      // Fallback to main thread if worker failed to initialize
      return this.fallbackRecent(books, historyEntries)
    }

    // Cancel any pending requests
    this.pendingRequests.forEach(({ reject }) => {
      reject(new Error('Request cancelled'))
    })
    this.pendingRequests.clear()

    return new Promise((resolve, reject) => {
      const currentRequestId = ++this.requestId

      this.pendingRequests.set(currentRequestId, { resolve, reject })

      // Ensure books are cloneable by creating clean copies
      const cloneableBooks = books.map(book => ({
        id: book.id,
        title: book.title,
        author: book.author,
        printingPlace: book.printingPlace,
        printingYear: book.printingYear,
        pages: book.pages,
        userScore: book.userScore,
        lastAccessed: book.lastAccessed,
        _csvTags: book._csvTags
      }))

      // Ensure history entries are cloneable
      const cloneableHistoryEntries = historyEntries.map(entry => ({
        id: entry.id,
        title: entry.title,
        author: entry.author,
        accessCount: entry.accessCount,
        lastAccessed: entry.lastAccessed,
        firstAccessed: entry.firstAccessed
      }))

      const message: RecentMessage = {
        type: 'recent',
        books: cloneableBooks,
        historyEntries: cloneableHistoryEntries
      }

      this.worker!.postMessage(message)
    })
  }

  private fallbackSearch(books: HebrewBook[], searchTerm: string): HebrewBook[] {
    // Simple fallback search on main thread (for when worker fails)
    if (!searchTerm || searchTerm.trim() === '') {
      return []
    }

    const normalizedSearchTerm = searchTerm.trim().toLowerCase()
    const searchTerms = normalizedSearchTerm.split(' ').filter((term) => term.trim() !== '')

    if (searchTerms.length === 0) {
      return []
    }

    const results = books.filter((entry) => {
      const titleLower = entry.title.toLowerCase()
      const authorLower = entry.author.toLowerCase()

      return searchTerms.every((term) => {
        return titleLower.includes(term) || authorLower.includes(term)
      })
    })

    return results.sort((a, b) => a.title.localeCompare(b.title))
  }

  private fallbackRecent(books: HebrewBook[], historyEntries: HistoryEntry[]): HebrewBook[] {
    // Simple fallback recent books on main thread (for when worker fails)
    if (historyEntries.length === 0) {
      return []
    }

    // Sort by most recent and limit to prevent blocking
    const sortedHistory = historyEntries
      .sort((a, b) => b.lastAccessed - a.lastAccessed)
      .slice(0, 50)

    const bookMap = new Map(books.map(book => [book.id, book]))
    const recentBooks: HebrewBook[] = []

    for (const historyEntry of sortedHistory) {
      const book = bookMap.get(historyEntry.id)
      if (book) {
        recentBooks.push({
          ...book,
          userScore: historyEntry.accessCount,
          lastAccessed: historyEntry.lastAccessed
        })
      }
    }

    return recentBooks
  }

  destroy() {
    if (this.worker) {
      this.worker.terminate()
      this.worker = null
    }
    this.pendingRequests.clear()
  }

  // Get filtered books based on search term (using Web Worker for both search and recent books)
  async getFilteredBooks(books: HebrewBook[], searchTerm: string): Promise<HebrewBook[]> {
    const isSearching = searchTerm && searchTerm.trim() !== ''

    if (!isSearching) {
      // Default view: use Web Worker for recent books (simple, fast, no complex scoring)
      const historyEntries = await HistoryService.getAllHistory()
      return await this.getRecentBooks(books, historyEntries)
    }

    // Search mode: use Web Worker for non-blocking search
    return await this.search(books, searchTerm)
  }

  // Debounce utility for search input (async version)
  createDebouncedSearch(
    callback: (value: string) => Promise<void>,
    delay: number = 150,
  ): (value: string) => void {
    let timeoutId: NodeJS.Timeout | null = null

    return (value: string) => {
      if (timeoutId) {
        clearTimeout(timeoutId)
      }

      timeoutId = setTimeout(async () => {
        try {
          await callback(value)
        } catch (error) {
          console.error('Search callback error:', error)
        }
      }, delay)
    }
  }
}

// Singleton instance
export const hebrewBooksSearchService = new HebrewBooksSearchService()
