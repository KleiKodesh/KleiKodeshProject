import { hbSearch } from '@/webview-host/bridge'

export interface HebrewBook {
  id: number
  title: string
  author: string
  printingPlace: string
  printingYear: string
  pages: number | null
  categories: string
  /** True when {localFolder}/{id}.pdf exists on disk — stamped by C# during search. */
  hasLocalFile?: boolean
  /** Set by the history store — most-recent access timestamp. */
  lastAccessed?: number
}

export function getHbPdfUrl(bookId: number): string {
  return `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
}

/**
 * Search the Hebrew Books catalog via the C# SQLite backend.
 * If localFolder is provided, C# stamps hasLocalFile on each result with no extra round-trip.
 * Returns up to 200 results sorted by title.
 */
export async function searchHbCatalog(term: string, localFolder?: string, limit?: number): Promise<HebrewBook[]> {
  try {
    const result = await hbSearch(term, localFolder, limit)
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
