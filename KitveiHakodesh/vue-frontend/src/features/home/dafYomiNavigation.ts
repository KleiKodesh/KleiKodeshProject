import { normalize } from '@/utils/normalizeText'
import { normalizeBookPath } from '../book-catalog/bookCatalogSearchNormalizer'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useTabStore } from '@/stores/tabStore'
import { filterBooksByWords } from '../book-catalog/bookCatalogSearch'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'

// ─── Daf string parsing ───────────────────────────────────────────────────────

/**
 * The daf yomi string from hebcal looks like "חולין דף יד" (amud alef) or
 * "חולין דף יד:" (amud bet). The format is always "<tractate> דף <number>[punctuation]".
 *
 * Returns null when the string does not match the expected shape.
 */
function parseDafYomiString(dafYomi: string): { tractate: string; dafPrefix: string } | null {
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
 * Finds the Bavli tractate book using the in-memory catalog index, then queries
 * the TOC directly with a LIKE prefix — no full TOC load or tree scoring needed
 * because the daf structure is always "דף X עמוד Y".
 */
export async function navigateToDafYomi(dafYomi: string): Promise<void> {
  const store = useBooksDataStore()
  const tabStore = useTabStore()

  await store.ensureLoaded()

  const parsed = parseDafYomiString(dafYomi)
  if (!parsed) return

  const { tractate, dafPrefix } = parsed

  // Find the Bavli tractate — prepend בבלי to avoid matching Mishna tractates
  const fullQuery = `בבלי ${tractate}`
  const words = normalizeBookPath(normalize(fullQuery.trim()))
    .split(/\s+/)
    .filter((word) => word.length > 0)
  if (!words.length) return

  const candidates = filterBooksByWords(store.allBooks, words)
  if (!candidates.length) return

  const book = candidates[0]!

  // Query the TOC directly — no need to load all entries and run the tree scorer
  type TocEntryRow = { id: number; lineIndex: number | null }
  const rows = await query<TocEntryRow>(SQL.GET_TOC_ENTRY_BY_TEXT_PREFIX, [
    book.id,
    `${dafPrefix}%`,
  ])

  if (rows.length > 0) {
    const tocEntry = rows[0]!
    tabStore.updateActiveTab({
      route: '/book-view',
      title: book.title,
      bookId: book.id,
      openTocEntryId: tocEntry.id,
      openTocLineIndex: tocEntry.lineIndex ?? undefined,
    })
  } else {
    // TOC entry not found — open the book at the start
    tabStore.updateActiveTab({ route: '/book-view', title: book.title, bookId: book.id })
  }
}
