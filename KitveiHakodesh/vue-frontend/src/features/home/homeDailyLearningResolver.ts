import { useBooksDataStore } from '@/stores/booksDataStore'
import { searchBookTitleIds } from '../book-catalog/bookTitleSearch'
import {
  getAllAltTocEntries,
  getAltTocStructures,
  getBookIdByExactTitle,
  getTocEntryByTextPrefix,
} from '@/webview-host/seforimApi'
import type { HomeDailyLearningTarget } from './homeDailyLearningTypes'

/** A daily-learning target resolved against the library: the book to open and, when found, the TOC entry. */
export interface HomeDailyLearningResolvedTarget {
  bookId: number
  bookTitle: string
  tocEntryId?: number
  tocLineIndex?: number
}

async function findBookByName(target: HomeDailyLearningTarget): Promise<{ id: number; title: string } | null> {
  for (const bookQuery of target.bookQueries) {
    const book = await findBookByQuery(bookQuery, target.matchTitleExactly ?? false)
    if (book) return book
  }
  return null
}

async function findBookByQuery(bookQuery: string, matchTitleExactly: boolean): Promise<{ id: number; title: string } | null> {
  const store = useBooksDataStore()
  await store.ensureLoaded()

  if (matchTitleExactly) {
    const rows = await getBookIdByExactTitle(bookQuery)
    const book = rows[0] ? store.allBooks.find((candidate) => candidate.id === rows[0]!.id) : undefined
    return book ? { id: book.id, title: book.title } : null
  }

  if (!bookQuery.trim()) return null
  const bookIds = await searchBookTitleIds(bookQuery.trim())
  const book = bookIds?.length ? store.allBooksMap.get(bookIds[0]!) : undefined
  return book ? { id: book.id, title: book.title } : null
}

/**
 * Resolve a daily-learning target BY NAME: catalog lookup for the book, then the
 * first TOC pattern that matches. Returns null when the book is not in the library
 * (the card is hidden); a missing TOC entry still opens the book at its start.
 */
export async function resolveHomeDailyLearningTarget(
  target: HomeDailyLearningTarget,
): Promise<HomeDailyLearningResolvedTarget | null> {
  const book = await findBookByName(target)
  if (!book) return null

  for (const pattern of target.tocPatterns) {
    const rows = await getTocEntryByTextPrefix(book.id, pattern)
    const tocEntry = rows[0]
    if (tocEntry) {
      return {
        bookId: book.id,
        bookTitle: book.title,
        tocEntryId: tocEntry.id,
        tocLineIndex: tocEntry.lineIndex ?? undefined,
      }
    }
  }

  const altTocLineIndex = await findAltTocLineIndex(book.id, target.tocPatterns)
  if (altTocLineIndex != null) return { bookId: book.id, bookTitle: book.title, tocLineIndex: altTocLineIndex }
  return { bookId: book.id, bookTitle: book.title }
}

/** SQL LIKE pattern (only `%` used) → anchored, case-insensitive RegExp. */
function likePatternToRegExp(pattern: string): RegExp {
  const body = pattern
    .split('%')
    .map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('.*')
  return new RegExp(`^${body}$`, 'i')
}

/**
 * Fallback for books whose heading lives only in an alt TOC (e.g. Torah parshiyot):
 * first alt-TOC entry, across all structures, matching a pattern in pattern order.
 * Alt entry ids aren't main-TOC ids, so only the line index is returned.
 */
async function findAltTocLineIndex(bookId: number, tocPatterns: string[]): Promise<number | null> {
  if (!tocPatterns.length) return null
  const structures = await getAltTocStructures(bookId)
  if (!structures.length) return null
  const entryLists = await Promise.all(structures.map((structure) => getAllAltTocEntries(structure.id)))
  const entries = entryLists.flat().filter((entry) => entry.lineIndex != null)
  for (const pattern of tocPatterns) {
    const regExp = likePatternToRegExp(pattern)
    const entry = entries.find((candidate) => regExp.test(candidate.text.trim()))
    if (entry) return entry.lineIndex
  }
  return null
}
