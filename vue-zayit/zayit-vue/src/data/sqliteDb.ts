/**
 * SQLite Database Access for Development
 * 
 * Uses Vite dev server API to query database.
 * Only used in development mode.
 */

import type { Category } from '../types/BookCategoryTree'
import type { Book } from '../types/Book'
import type { TocEntry } from '../types/BookToc'
import type { Link } from '../types/Link'
import { SqlQueries } from './sqlQueries'

/**
 * Execute SQL query via Vite dev server API
 */
export async function query<T = any>(sql: string, params: any[] = []): Promise<T[]> {
  // In production, this should not be used
  if (import.meta.env.PROD) {
    throw new Error('SQLite API is only available in development mode')
  }

  try {
    const response = await fetch('/__db/query', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ query: sql, params })
    })

    const result = await response.json()

    if (!result.success) {
      throw new Error(result.error)
    }

    return result.data
  } catch (error) {
    console.error('❌ Database query failed:', error)
    throw error
  }
}

export async function getAllCategories() {
  const categories = await query<Category>(SqlQueries.getAllCategories)
  return categories
}

/**
 * Get all books from database
 */
export async function getBooks() {
  const books = await query<Book>(SqlQueries.getAllBooks)
  return books
}

/**
 * Get TOC data for a book
 * Returns flat data that matches C# format
 */
export async function getToc(bookId: number) {
  const tocEntriesFlat = await query<TocEntry>(SqlQueries.getToc(bookId))
  return { tocEntriesFlat }
}

/**
 * Get single line content by bookId and lineIndex
 */
export async function getLineContent(bookId: number, lineIndex: number): Promise<string | null> {
  const result = await query<{ content: string }>(SqlQueries.getLineContent(bookId, lineIndex))
  return result[0]?.content ?? null
}

/**
 * Get line ID by bookId and lineIndex
 */
export async function getLineId(bookId: number, lineIndex: number): Promise<number | null> {
  const result = await query<{ id: number }>(SqlQueries.getLineId(bookId, lineIndex))
  return result[0]?.id ?? null
}

/**
 * Get links for a line
 */
export async function getLinks(lineId: number): Promise<Link[]> {
  return await query<Link>(SqlQueries.getLinks(lineId))
}

/**
 * Get total line count for a book
 */
export async function getTotalLines(bookId: number): Promise<number> {
  const result = await query<{ totalLines: number }>(SqlQueries.getBookLineCount(bookId))
  return result[0]?.totalLines || 0
}

/**
 * Load a range of lines from database
 */
export interface LineLoadResult {
  lineIndex: number
  content: string
}

export async function loadLineRange(bookId: number, start: number, end: number): Promise<LineLoadResult[]> {
  return await query<LineLoadResult>(SqlQueries.getLineRange(bookId, start, end))
}

/**
 * Search lines in a book
 */
export async function searchLines(bookId: number, searchTerm: string): Promise<LineLoadResult[]> {
  return await query<LineLoadResult>(SqlQueries.searchLines(bookId, searchTerm))
}

