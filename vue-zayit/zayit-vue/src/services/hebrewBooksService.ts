/**
 * Simple Hebrew Books Service
 * - Loads CSV once
 * - Maintains LRU history in IndexedDB
 * - Fast in-memory search
 */

import Papa from 'papaparse'
import type { HebrewBook } from '../types/HebrewBook'

interface HistoryEntry {
    id: string
    title: string
    author: string
    printingPlace: string
    printingYear: string
    pages: string
    _csvTags: string
    lastAccessed: number
}

class HebrewBooksService {
    private books: HebrewBook[] = []
    private catalogLoaded = false
    private catalogLoading = false
    private db: IDBDatabase | null = null

    // Load CSV catalog
    async loadCatalog(): Promise<void> {
        if (this.catalogLoaded || this.catalogLoading) return

        this.catalogLoading = true

        try {
            const response = await fetch('/HebrewBooks.csv')
            const csvText = await response.text()

            const result = await new Promise<HebrewBook[]>((resolve, reject) => {
                Papa.parse(csvText, {
                    complete: (results) => {
                        const books = results.data
                            .filter((row: any) => row.length >= 7)
                            .map((row: any) => ({
                                id: row[0],
                                title: row[1],
                                author: row[2],
                                printingPlace: row[3],
                                printingYear: row[4],
                                pages: row[5],
                                _csvTags: row[6] || '',
                                lastAccessed: undefined
                            }))
                            .filter((book: HebrewBook) => book.title && book.title.trim() !== '')

                        resolve(books)
                    },
                    error: reject
                })
            })

            this.books = result
            this.catalogLoaded = true
        } finally {
            this.catalogLoading = false
        }
    }

    // Get history (LRU sorted) - returns full book data from IndexedDB
    async getHistory(): Promise<HebrewBook[]> {
        await this.initDB()
        const historyEntries = await this.getAllHistoryEntries()

        // Return history entries directly as books (they have full data)
        return historyEntries.map(entry => ({
            id: entry.id,
            title: entry.title,
            author: entry.author,
            printingPlace: entry.printingPlace,
            printingYear: entry.printingYear,
            pages: entry.pages,
            _csvTags: entry._csvTags,
            lastAccessed: entry.lastAccessed
        }))
    }

    // Search books (simple, fast, in-memory)
    search(term: string): HebrewBook[] {
        if (!term || !term.trim()) return []
        if (!this.catalogLoaded) return []

        const searchTerms = term.toLowerCase().trim().split(' ').filter(t => t)
        if (searchTerms.length === 0) return []

        // Don't search for single character terms
        if (searchTerms.length === 1 && searchTerms[0] && searchTerms[0].length === 1) return []

        return this.books
            .filter(book => {
                const title = book.title.toLowerCase()
                const author = book.author.toLowerCase()
                const tags = book._csvTags?.toLowerCase() || ''

                return searchTerms.every(term =>
                    title.includes(term) || author.includes(term) || tags.includes(term)
                )
            })
            .sort((a, b) => a.title.localeCompare(b.title))
    }

    // Track book access (LRU) - store full book data
    async trackAccess(bookId: string, book: HebrewBook): Promise<void> {
        await this.initDB()
        if (!this.db) return

        const transaction = this.db.transaction(['bookHistory'], 'readwrite')
        const store = transaction.objectStore('bookHistory')

        const entry: HistoryEntry = {
            id: bookId,
            title: book.title,
            author: book.author,
            printingPlace: book.printingPlace || '',
            printingYear: book.printingYear || '',
            pages: book.pages || '',
            _csvTags: book._csvTags || '',
            lastAccessed: Date.now()
        }

        store.put(entry)

        // Cleanup old entries (keep max 25)
        const countRequest = store.count()
        countRequest.onsuccess = () => {
            if (countRequest.result > 25) {
                const getAllRequest = store.getAll()
                getAllRequest.onsuccess = () => {
                    const entries = getAllRequest.result as HistoryEntry[]
                    entries.sort((a, b) => a.lastAccessed - b.lastAccessed)
                    const toDelete = entries.slice(0, countRequest.result - 25)
                    toDelete.forEach(e => store.delete(e.id))
                }
            }
        }
    }

    // IndexedDB helpers
    private async initDB(): Promise<void> {
        if (this.db) return

        return new Promise((resolve, reject) => {
            const request = indexedDB.open('HebrewBooksHistory', 1)

            request.onerror = () => reject(request.error)
            request.onsuccess = () => {
                this.db = request.result
                resolve()
            }

            request.onupgradeneeded = (event) => {
                const db = (event.target as IDBOpenDBRequest).result
                if (!db.objectStoreNames.contains('bookHistory')) {
                    const store = db.createObjectStore('bookHistory', { keyPath: 'id' })
                    store.createIndex('lastAccessed', 'lastAccessed', { unique: false })
                }
            }
        })
    }

    private async getAllHistoryEntries(): Promise<HistoryEntry[]> {
        if (!this.db) return []

        return new Promise((resolve, reject) => {
            const transaction = this.db!.transaction(['bookHistory'], 'readonly')
            const store = transaction.objectStore('bookHistory')
            const request = store.getAll()

            request.onsuccess = () => {
                const entries = request.result as HistoryEntry[]
                entries.sort((a, b) => b.lastAccessed - a.lastAccessed)
                resolve(entries)
            }
            request.onerror = () => reject(request.error)
        })
    }

    // Getters
    isCatalogLoaded(): boolean {
        return this.catalogLoaded
    }
}

export const hebrewBooksService = new HebrewBooksService()
