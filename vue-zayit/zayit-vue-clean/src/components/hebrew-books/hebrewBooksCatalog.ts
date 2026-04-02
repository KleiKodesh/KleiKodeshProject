export interface HebrewBook {
  id: string
  title: string
  author: string
  printingPlace: string
  printingYear: string
  pages: string
  _csvTags: string
  lastAccessed?: number
}

export function getHbPdfUrl(bookId: string): string {
  return `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
}

export async function loadHbCatalog(): Promise<HebrewBook[]> {
  const text = await fetch('/HebrewBooks.csv').then((r) => r.text())
  return text
    .split('\n')
    .map((line) => line.split(','))
    .filter((row) => row.length >= 6 && row[1]?.trim())
    .map((row) => ({
      id: row[0]?.trim() ?? '',
      title: row[1]?.trim() ?? '',
      author: row[2]?.trim() ?? '',
      printingPlace: row[3]?.trim() ?? '',
      printingYear: row[4]?.trim() ?? '',
      pages: row[5]?.trim() ?? '',
      _csvTags: row[6]?.trim() ?? '',
    }))
}

export function searchHbCatalog(catalog: HebrewBook[], term: string): HebrewBook[] {
  const terms = term
    .toLowerCase()
    .trim()
    .split(' ')
    .filter((t) => t.length > 1)
  if (!terms.length) return []
  return catalog
    .filter((b) =>
      terms.every(
        (t) =>
          b.title.toLowerCase().includes(t) ||
          b.author.toLowerCase().includes(t) ||
          b._csvTags.toLowerCase().includes(t),
      ),
    )
    .sort((a, b) => a.title.localeCompare(b.title))
}
