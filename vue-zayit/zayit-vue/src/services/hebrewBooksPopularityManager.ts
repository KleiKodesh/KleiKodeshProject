import type { HebrewBook } from '../types/HebrewBook'
import { HistoryService } from './hebrewBooksHistoryService'

export class PopularityManager {
  // Calculate combined popularity score using IndexedDB history
  static async calculateCombinedScore(book: HebrewBook): Promise<number> {
    try {
      const historyEntry = await HistoryService.getBookHistory(book.ID_Book)
      if (!historyEntry) return 0
      
      return HistoryService.calculatePopularityScore(historyEntry)
    } catch (error) {
      console.error('Error calculating combined score:', error)
      return 0
    }
  }

  // Get books ranked by popularity (only show books with user interactions)
  static async getPopularityRankedBooks(books: HebrewBook[], limit?: number): Promise<HebrewBook[]> {
    try {
      // Get all history entries
      const historyEntries = await HistoryService.getAllHistory()
      
      // If no history, return empty array (no books to show)
      if (historyEntries.length === 0) {
        return []
      }
      
      const historyMap = new Map(historyEntries.map(entry => [entry.id, entry]))

      // Only include books that have history entries
      const booksWithHistory = books
        .filter(book => historyMap.has(book.ID_Book))
        .map(book => {
          const historyEntry = historyMap.get(book.ID_Book)!
          const popularityScore = HistoryService.calculatePopularityScore(historyEntry)
          
          return {
            ...book,
            userScore: historyEntry.accessCount,
            lastAccessed: historyEntry.lastAccessed,
            combinedScore: popularityScore
          }
        })

      // Sort by combined score (popularity + recency + frequency)
      const sortedBooks = booksWithHistory.sort((a, b) => {
        if (b.combinedScore !== a.combinedScore) {
          return b.combinedScore - a.combinedScore
        }
        return a.Title.localeCompare(b.Title)
      })

      // Apply limit if provided
      return limit ? sortedBooks.slice(0, limit) : sortedBooks
    } catch (error) {
      console.error('Error getting popularity ranked books:', error)
      // If history fails, return empty array (don't show any books)
      return []
    }
  }

  // Track user interaction with a book using IndexedDB
  static async trackBookInteraction(books: HebrewBook[], bookId: string): Promise<HebrewBook[]> {
    try {
      const book = books.find(b => b.ID_Book === bookId)
      if (!book) return books

      // Track in IndexedDB
      await HistoryService.trackBookInteraction(bookId, book.Title, book.Author)

      // Update the book in the array with new interaction data
      const updatedBooks = [...books]
      const bookIndex = updatedBooks.findIndex(b => b.ID_Book === bookId)
      
      if (bookIndex !== -1) {
        const currentBook = updatedBooks[bookIndex]
        if (currentBook) {
          updatedBooks[bookIndex] = {
            ID_Book: currentBook.ID_Book,
            Title: currentBook.Title,
            Author: currentBook.Author,
            Printing_Place: currentBook.Printing_Place,
            Printing_Year: currentBook.Printing_Year,
            Pages: currentBook.Pages,
            Tags: currentBook.Tags,
            userScore: (currentBook.userScore || 0) + 1,
            lastAccessed: Date.now()
          }
        }
      }
      
      return updatedBooks
    } catch (error) {
      console.error('Error tracking book interaction:', error)
      return books
    }
  }

  // Load user interactions from IndexedDB (replaces localStorage method)
  static async loadUserInteractions(books: HebrewBook[]): Promise<HebrewBook[]> {
    try {
      const historyEntries = await HistoryService.getAllHistory()
      const historyMap = new Map(historyEntries.map(entry => [entry.id, entry]))
      
      return books.map(book => {
        const historyEntry = historyMap.get(book.ID_Book)
        if (historyEntry) {
          return {
            ...book,
            userScore: historyEntry.accessCount,
            lastAccessed: historyEntry.lastAccessed
          }
        }
        return book
      })
    } catch (error) {
      console.error('Error loading user interactions:', error)
      return books
    }
  }

  // Clear all history (for privacy/reset purposes)
  static async clearAllHistory(): Promise<void> {
    await HistoryService.clearAllHistory()
  }
}
