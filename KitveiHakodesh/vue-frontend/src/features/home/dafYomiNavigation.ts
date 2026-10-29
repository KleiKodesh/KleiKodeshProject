import { useBooksDataStore } from '@/stores/booksDataStore'
import { searchBookTitleIds } from '../book-catalog/bookTitleSearch'
import { getTocEntryByTextPrefix } from '@/webview-host/seforimApi'
import type { PaneNavigation } from '@/composables/usePaneNavigation'

// ─── Daf string parsing ───────────────────────────────────────────────────────

/**
 * The daf yomi string from hebcal looks like "חולין דף יד" (amud alef) or
 * "חולין דף יד:" (amud bet). The format is always "<tractate> דף <number>[punctuation]".
 *
 * Returns null when the string does not match the expected shape.
 */
export function parseDafYomiString(dafYomi: string): { tractate: string; dafPrefix: string } | null {
  // Find the last occurrence of "דף" — everything before it is the tractate,
  // everything after it is the daf number (possibly with trailing . or :)
  const dafIndex = dafYomi.lastIndexOf('דף')
  if (dafIndex === -1) return null

  const tractate = dafYomi.slice(0, dafIndex).trim()
  const afterDaf = dafYomi.slice(dafIndex + 2).trim()
  // Strip trailing punctuation (. : ׃) from the daf number
  const dafNumber = afterDaf.replace(/[.:׃]+$/, '').trim()

  if (!tractate || !dafNumber) return null

  // TOC entries in Bavli are "דף יד עמוד א" — match by "דף <number>" prefix
  const dafPrefix = `דף ${dafNumber}`
  return { tractate, dafPrefix }
}

// ─── Navigation ───────────────────────────────────────────────────────────────

/**
 * Navigate to the daf yomi entry in book-view.
 *
 * Finds the Bavli tractate book through the catalog title search, then queries
 * the TOC directly with a LIKE prefix — no full TOC load or tree scoring needed
 * because the daf structure is always "דף X עמוד Y".
 */
export async function navigateToDafYomi(dafYomi: string, paneNavigation: PaneNavigation): Promise<void> {
  const store = useBooksDataStore()

  await store.ensureLoaded()

  const parsed = parseDafYomiString(dafYomi)
  if (!parsed) return

  const { tractate, dafPrefix } = parsed

  // Find the Bavli tractate — prepend בבלי to avoid matching Mishna tractates
  const bookIds = await searchBookTitleIds(`בבלי ${tractate}`)
  const book = bookIds?.length ? store.allBooksMap.get(bookIds[0]!) : undefined
  if (!book) return

  // Query the TOC directly — no need to load all entries and run the tree scorer
  const rows = await getTocEntryByTextPrefix(book.id, `${dafPrefix}%`)

  if (rows.length > 0) {
    const tocEntry = rows[0]!
    paneNavigation.updateActiveTab({
      route: '/book-view',
      title: book.title,
      bookId: book.id,
      openTocEntryId: tocEntry.id,
      openTocLineIndex: tocEntry.lineIndex ?? undefined,
    })
  } else {
    paneNavigation.updateActiveTab({ route: '/book-view', title: book.title, bookId: book.id })
  }
}
