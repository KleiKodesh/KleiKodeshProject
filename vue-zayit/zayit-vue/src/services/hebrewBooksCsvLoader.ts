import Papa from 'papaparse'
import type { HebrewBook } from '../types/HebrewBook'

export class CsvLoader {
  static async loadBooks(): Promise<HebrewBook[]> {
    try {
      const response = await fetch('/HebrewBooks.csv')
      const csvText = await response.text()

      return new Promise((resolve, reject) => {
        Papa.parse(csvText, {
          complete: (results) => {
            try {
              const books = results.data
                .filter((row: any) => row.length >= 7) // We have 7 columns including tags
                .map((row: any) => ({
                  id: row[0],
                  title: row[1],
                  author: row[2],
                  printingPlace: row[3],
                  printingYear: row[4],
                  pages: row[5],
                  // Don't store tags in the object, just use them for search/display
                  userScore: 0,
                  lastAccessed: undefined,
                  _csvTags: row[6] || '', // Store raw CSV tags for search/display
                }))
                .filter((book: HebrewBook) => book.title && book.title.trim() !== '')

              resolve(books)
            } catch (error) {
              reject(error)
            }
          },
          error: (error: any) => {
            reject(error)
          },
        })
      })
    } catch (error) {
      console.error('Error loading CSV:', error)
      throw error
    }
  }
}
