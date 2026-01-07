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
                .filter((row: any) => row.length >= 7) // Now we have 7 columns
                .map((row: any) => ({
                  ID_Book: row[0],
                  Title: row[1],
                  Author: row[2],
                  Printing_Place: row[3],
                  Printing_Year: row[4],
                  Pages: row[5], // Now column 5 instead of 7
                  Tags: row[6]?.replace(/;/g, ' \\ ') || '', // Now column 6 instead of 10
                  userScore: 0,
                  lastAccessed: undefined,
                }))
                .filter((book: HebrewBook) => book.Title && book.Title.trim() !== '')

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
