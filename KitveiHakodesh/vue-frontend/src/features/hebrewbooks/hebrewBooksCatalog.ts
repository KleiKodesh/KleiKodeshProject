import { hbSearch } from '@/webview-host/bridge'

export interface HebrewBook {
  id: number
  title: string
  author: string
  printingPlace: string
  printingYear: string
  pages: number | null
  categories: string
  lastAccessed?: number
}

export function getHbPdfUrl(bookId: number): string {
  return `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
}

/**
 * Search the Hebrew Books catalog via the C# SQLite backend.
 * Returns up to 200 results sorted by title.
 */
export async function searchHbCatalog(term: string): Promise<HebrewBook[]> {
  try {
    const result = await hbSearch(term)
    if (result.error) {
      console.error('Hebrew Books search error:', result.error)
      return []
    }
    return (result.books ?? []) as HebrewBook[]
  } catch (e) {
    console.error('Failed to search Hebrew Books:', e)
    return []
  }
}
