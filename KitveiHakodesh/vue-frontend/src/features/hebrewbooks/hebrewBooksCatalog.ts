import { normalize } from '@/utils/normalizeText'
import { isHosted } from '@/webview-host/seforimDb'
import { hbGetAll, hbSearch } from '@/webview-host/bridge'

export interface HebrewBook {
  id: number
  title: string
  author: string
  printingPlace: string
  printingYear: string
  pages: number | null
  categories: string
  lastAccessed?: number
  _searchPath?: string // pre-normalized search string, built once at catalog load (for in-memory fallback)
}

export function getHbPdfUrl(bookId: number): string {
  return `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
}

/**
 * Load Hebrew Books catalog from the database.
 * In hosted mode: queries the C# backend via hbGetAll action.
 * In dev mode: falls back to loading the CSV file.
 */
export async function loadHbCatalog(): Promise<HebrewBook[]> {
  if (isHosted) {
    try {
      const result = await hbGetAll()
      if (result.error) {
        console.error('Hebrew Books database error:', result.error)
        return []
      }
      return (result.books ?? []) as HebrewBook[]
    } catch (e) {
      console.error('Failed to load Hebrew Books from database:', e)
      return []
    }
  }

  // Dev mode fallback: load from CSV
  const text = await fetch('/HebrewBooks.csv').then((r) => r.text())
  return text
    .split('\n')
    .map((line) => line.split(','))
    .filter((row) => row.length >= 6 && row[1]?.trim())
    .map((row) => {
      const title = row[1]?.trim() ?? ''
      const author = row[2]?.trim() ?? ''
      const categories = row[6]?.trim() ?? ''
      return {
        id: parseInt(row[0]?.trim() ?? '0', 10),
        title,
        author,
        printingPlace: row[3]?.trim() ?? '',
        printingYear: row[4]?.trim() ?? '',
        pages: row[5]?.trim() ? parseInt(row[5]?.trim() ?? '0', 10) : null,
        categories,
        _searchPath: `${normalize(title)} ${normalize(author)} ${normalize(categories)}`,
      }
    })
}

/**
 * Search Hebrew Books catalog.
 * In hosted mode: queries the C# backend via hbSearch action (database-backed).
 * In dev mode: searches in-memory from the catalog.
 */
export async function searchHbCatalog(catalog: HebrewBook[], term: string): Promise<HebrewBook[]> {
  if (isHosted) {
    try {
      const result = await hbSearch(term)
      if (result.error) {
        console.error('Hebrew Books search error:', result.error)
        return []
      }
      return (result.books ?? []) as HebrewBook[]
    } catch (e) {
      console.error('Failed to search Hebrew Books database:', e)
      return searchHbCatalogInMemory(catalog, term)
    }
  }

  // Dev mode: search in-memory
  return searchHbCatalogInMemory(catalog, term)
}

/**
 * In-memory search for development and fallback scenarios.
 */
function searchHbCatalogInMemory(catalog: HebrewBook[], term: string): HebrewBook[] {
  const words = normalize(term)
    .trim()
    .split(/\s+/)
    .filter((t) => t.length > 0)
  if (!words.length) return []
  return catalog
    .filter((b) => {
      const pathWords = (b._searchPath ?? '').split(/\s+/)
      return words.every((qw) => pathWords.some((pw) => pw === qw || pw.includes(qw)))
    })
    .sort((a, b) => a.title.localeCompare(b.title))
}
