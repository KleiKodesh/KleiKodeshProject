import { normalize } from '@/utils/normalizeText'
import { normalizeBookPath } from '../book-catalog/bookCatalogSearchNormalizer'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { filterBooksByWords } from '../book-catalog/bookCatalogSearch'
import { getBookIdByExactTitle, getTocEntryByTextPrefix } from '@/webview-host/seforimApi'
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

  const words = normalizeBookPath(normalize(bookQuery.trim()))
    .split(/\s+/)
    .filter((word) => word.length > 0)
  if (!words.length) return null
  const book = filterBooksByWords(store.allBooks, words)[0]
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
  return { bookId: book.id, bookTitle: book.title }
}
