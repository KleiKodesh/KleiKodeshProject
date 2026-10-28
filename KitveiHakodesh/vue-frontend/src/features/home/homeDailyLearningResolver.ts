import { useBooksDataStore } from '@/stores/booksDataStore'
import { searchBookTitleIds } from '../book-catalog/bookTitleSearch'
import {
  getAllAltTocEntries,
  getAltTocStructures,
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

// Same query from several cycles (daf yomi + daf weekly, mishnah + perek) shares one lookup while in flight.
const bookQueriesInFlight = new Map<string, Promise<{ id: number; title: string } | null>>()

function findBookByQuery(bookQuery: string, matchTitleExactly: boolean): Promise<{ id: number; title: string } | null> {
  const key = `${matchTitleExactly ? '=' : '~'}${bookQuery}`
  const inFlight = bookQueriesInFlight.get(key)
  if (inFlight) return inFlight
  const promise = lookUpBook(bookQuery, matchTitleExactly)
  bookQueriesInFlight.set(key, promise)
  const forget = () => bookQueriesInFlight.delete(key)
  promise.then(forget, forget)
  return promise
}

async function lookUpBook(bookQuery: string, matchTitleExactly: boolean): Promise<{ id: number; title: string } | null> {
  const store = useBooksDataStore()
  await store.ensureLoaded()

  if (matchTitleExactly) {
    // Local twin of `SELECT id FROM book WHERE title = @title` (first row = lowest id): no round-trip.
    let book: { id: number; title: string } | undefined
    for (const candidate of store.allBooks) {
      if (candidate.title === bookQuery && (!book || candidate.id < book.id)) book = candidate
    }
    return book ? { id: book.id, title: book.title } : null
  }

  if (!bookQuery.trim()) return null
  const bookIds = await searchTitleIdsOneAtATime(bookQuery.trim())
  const book = bookIds?.length ? store.allBooksMap.get(bookIds[0]!) : undefined
  return book ? { id: book.id, title: book.title } : null
}

// The catalog title search is single-flight: a newer search supersedes the running one,
// so parallel cycles would cancel each other and retry-loop. Queue them instead.
let titleSearchQueue: Promise<unknown> = Promise.resolve()

function searchTitleIdsOneAtATime(query: string): Promise<number[] | null> {
  const result = titleSearchQueue.then(() => searchBookTitleIds(query))
  titleSearchQueue = result.catch(() => null)
  return result
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

  // All patterns in parallel; the first match in pattern order still wins.
  const rowsPerPattern = await Promise.all(target.tocPatterns.map((pattern) => getTocEntryByTextPrefix(book.id, pattern)))
  for (const rows of rowsPerPattern) {
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
