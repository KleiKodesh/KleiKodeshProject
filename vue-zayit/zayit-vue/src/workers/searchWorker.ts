// Web Worker for Hebrew Books search and recent history processing
import type { HebrewBook } from '../types/HebrewBook'

// Simple CSV tags parser (copied from utils since workers can't import modules easily)
function parseTagsFromCsv(csvTags: string): string[] {
    if (!csvTags || csvTags.trim() === '') {
        return []
    }

    return csvTags
        .split(';')
        .map(tag => tag.trim())
        .filter(tag => tag !== '')
}

interface HistoryEntry {
    id: string
    title: string
    author: string
    lastAccessed: number
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

// Listen for messages from main thread
self.onmessage = function (e: MessageEvent<SearchMessage | RecentMessage>) {
    const { type } = e.data

    if (type === 'search') {
        const { books, searchTerm } = e.data as SearchMessage
        const results = performSearch(books, searchTerm)

        const response: SearchResult = {
            type: 'searchResult',
            results
        }

        self.postMessage(response)
    } else if (type === 'recent') {
        const { books, historyEntries } = e.data as RecentMessage
        const results = getRecentBooks(books, historyEntries)

        const response: RecentResult = {
            type: 'recentResult',
            results
        }

        self.postMessage(response)
    }
}

function performSearch(books: HebrewBook[], searchTerm: string): HebrewBook[] {
    if (!searchTerm || searchTerm.trim() === '') {
        return []
    }

    // Normalize search term (trim and convert to lowercase for better matching)
    const normalizedSearchTerm = searchTerm.trim().toLowerCase()
    const searchTerms = normalizedSearchTerm.split(' ').filter((term) => term.trim() !== '')

    if (searchTerms.length === 0) {
        return []
    }

    let results: HebrewBook[]

    if (searchTerms.length === 1 && searchTerms[0] && searchTerms[0].length <= 3) {
        // Single short term (1-3 characters): try StartsWith first, fallback to includes
        const term = searchTerms[0]

        // First try: StartsWith logic (more precise)
        let startsWithResults = books.filter((entry) => {
            const titleLower = entry.title.toLowerCase()
            const authorLower = entry.author.toLowerCase()

            if (titleLower.startsWith(term) || authorLower.startsWith(term)) {
                return true
            }

            // Only parse tags if needed
            if (entry._csvTags) {
                const tags = parseTagsFromCsv(entry._csvTags)
                return tags.some(tag => tag.toLowerCase().startsWith(term))
            }

            return false
        })

        // If startsWith found results, use them
        if (startsWithResults.length > 0) {
            results = startsWithResults
        } else {
            // Fallback: includes logic (broader search)
            results = books.filter((entry) => {
                const titleLower = entry.title.toLowerCase()
                const authorLower = entry.author.toLowerCase()

                if (titleLower.includes(term) || authorLower.includes(term)) {
                    return true
                }

                // Only parse tags if needed
                if (entry._csvTags) {
                    const tags = parseTagsFromCsv(entry._csvTags)
                    return tags.some(tag => tag.toLowerCase().includes(term))
                }

                return false
            })
        }
    } else {
        // Multiple terms or longer single term (4+ characters): use includes logic (case-insensitive)
        results = books.filter((entry) => {
            const titleLower = entry.title.toLowerCase()
            const authorLower = entry.author.toLowerCase()

            // Pre-parse tags once per entry
            const tags = entry._csvTags ? parseTagsFromCsv(entry._csvTags) : []
            const tagsLower = tags.map(tag => tag.toLowerCase())

            return searchTerms.every((term) => {
                return (
                    titleLower.includes(term) ||
                    authorLower.includes(term) ||
                    tagsLower.some(tag => tag.includes(term))
                )
            })
        })
    }

    // Sort by title alphabetically (search results don't use popularity)
    return results.sort((a, b) => a.title.localeCompare(b.title))
}

function getRecentBooks(books: HebrewBook[], historyEntries: HistoryEntry[]): HebrewBook[] {
    // If no history, return empty array (no books to show)
    if (historyEntries.length === 0) {
        return []
    }

    // History is already sorted by LRU (most recent first) from HistoryService
    // Create a map for quick book lookup
    const bookMap = new Map(books.map(book => [book.id, book]))

    // Map history entries to books (only include books that exist)
    const recentBooks: HebrewBook[] = []

    for (const historyEntry of historyEntries) {
        const book = bookMap.get(historyEntry.id)
        if (book) {
            recentBooks.push({
                ...book,
                lastAccessed: historyEntry.lastAccessed
            })
        }
    }

    return recentBooks
}