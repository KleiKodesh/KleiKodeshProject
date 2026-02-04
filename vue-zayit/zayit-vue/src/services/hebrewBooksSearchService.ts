import type { HebrewBook } from '../types/HebrewBook'
import { HistoryService } from './hebrewBooksHistoryService'
import { searchWorkerService } from './searchWorkerService'

export class HebrewBooksSearchService {
  // Get filtered books based on search term (using Web Worker for both search and recent books)
  static async getFilteredBooks(books: HebrewBook[], searchTerm: string): Promise<HebrewBook[]> {
    const isSearching = searchTerm && searchTerm.trim() !== ''

    if (!isSearching) {
      // Default view: use Web Worker for recent books (simple, fast, no complex scoring)
      const historyEntries = await HistoryService.getAllHistory()
      return await searchWorkerService.getRecentBooks(books, historyEntries)
    }

    // Search mode: use Web Worker for non-blocking search
    return await searchWorkerService.search(books, searchTerm)
  }

  // Debounce utility for search input (async version)
  static createDebouncedSearch(
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