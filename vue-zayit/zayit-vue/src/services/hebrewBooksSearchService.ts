import type { HebrewBook } from '../types/HebrewBook'
import { PopularityManager } from './hebrewBooksPopularityManager'

export class HebrewBooksSearchService {
  private static readonly DEFAULT_DISPLAY_LIMIT = 50 // Cap for default view
  private static readonly SEARCH_RESULTS_LIMIT = 100 // Cap for search results

  // Get filtered books based on search term
  static async getFilteredBooks(books: HebrewBook[], searchTerm: string): Promise<HebrewBook[]> {
    const isSearching = searchTerm && searchTerm.trim() !== ''

    if (!isSearching) {
      // Default view: show popular books only with cap
      return await PopularityManager.getPopularityRankedBooks(books, this.DEFAULT_DISPLAY_LIMIT)
    }

    // Search mode: show all matching results with cap
    return this.searchBooks(books, searchTerm).slice(0, this.SEARCH_RESULTS_LIMIT)
  }

  // Search logic with improved Hebrew text handling
  static searchBooks(books: HebrewBook[], searchTerm: string): HebrewBook[] {
    if (!searchTerm || searchTerm.trim() === '') {
      return []
    }

    // Normalize search term (trim and convert to lowercase for better matching)
    const normalizedSearchTerm = searchTerm.trim()
    const searchTerms = normalizedSearchTerm.split(' ').filter((term) => term.trim() !== '')

    if (searchTerms.length === 0) {
      return []
    }

    let results: HebrewBook[]

    if (searchTerms.length === 1 && searchTerms[0] && searchTerms[0].length < 5) {
      // Single short term: use StartsWith logic (case-insensitive)
      const term = searchTerms[0].toLowerCase()
      results = books.filter(
        (entry) =>
          entry.Title.toLowerCase().startsWith(term) ||
          entry.Author.toLowerCase().startsWith(term) ||
          entry.Tags.toLowerCase().startsWith(term),
      )
    } else {
      // Multiple terms or long single term: ALL terms must be found somewhere (case-insensitive)
      results = books.filter((entry) => {
        const titleLower = entry.Title.toLowerCase()
        const authorLower = entry.Author.toLowerCase()
        const tagsLower = entry.Tags.toLowerCase()

        return searchTerms.every(
          (term) =>
            term &&
            (titleLower.includes(term.toLowerCase()) ||
              authorLower.includes(term.toLowerCase()) ||
              tagsLower.includes(term.toLowerCase())),
        )
      })
    }

    // Sort by title alphabetically (search results don't use popularity)
    return results.sort((a, b) => a.Title.localeCompare(b.Title))
  }

  // Debounce utility for search input
  static createDebouncedSearch(
    callback: (value: string) => void,
    delay: number = 300,
  ): (value: string) => void {
    let timeoutId: NodeJS.Timeout | null = null

    return (value: string) => {
      if (timeoutId) {
        clearTimeout(timeoutId)
      }

      timeoutId = setTimeout(() => {
        callback(value)
      }, delay)
    }
  }
}
