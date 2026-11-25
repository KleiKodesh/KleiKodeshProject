import { reactive } from 'vue'
import { dbGet, dbSet, dbDelete } from '@/utils/persistence'

/**
 * The `app-bookmarks` slice of persistence — the places a reader marked in a book, like
 * a ribbon left between pages.
 *
 * Keyed by book id alone: global, not per tab and not per workspace. One record per book
 * holds that book's whole list, so opening the bookmarks dropdown costs one read.
 *
 * Every loaded list is also held in ONE reactive map shared by all readers - the
 * toolbar's dropdown and its copy in the "more" flyout - so a
 * bookmark made or deleted in one shows up in all of them at once.
 */

const BOOKMARKS_DB = 'app-bookmarks'

export interface BookBookmark {
  /** Unique within the book; the creation time, so it also orders bookmarks made on the same line. */
  id: number
  /** How the line is found; resolves correctly after a version switch or a renumbered library. */
  lineId: number
  /** Fallback when the line id no longer resolves. */
  lineIndex: number
  /**
   * Letters and digits in the line's text before the point - letters, not characters, so
   * the point survives the clean-text options and the divine-name censor; absent = the start.
   */
  letterOffset?: number
  /** Set when the bookmark was made from a selection: how many letters and digits it covers. */
  selectionLetterCount?: number
  /** The reader's name for it; defaults to the TOC path of the marked line. */
  label: string
  /** The TOC path of the marked line, kept apart from the label so a rename does not lose it. */
  tocPath?: string
  /** The bookmarked text - the selection, or the words at the point - shown under the name. */
  snippet?: string
  /**
   * PDF bookmarks (key `pdf:<file path>`): the point's y in PDF units on the page
   * (PDF.js's view location `top`); lineId is then the page number and lineIndex its index.
   */
  pdfTop?: number
}

/**
 * Whose list: a library book's id, or a string key for other documents - `pdf:<file path>`
 * for a PDF file (see pdfBookmarksKey).
 */
export type BookmarksOwner = number | string

/** The bookmarks key of a PDF file: its path, so the list follows the file across tabs. */
export function pdfBookmarksKey(filePath: string): string {
  return `pdf:${filePath.replace(/\\/g, '/').toLowerCase()}`
}

function bookmarksKey(bookId: BookmarksOwner): string {
  return `bookmarks:${bookId}`
}

/** Each book's list once loaded, by book id. Absent = not loaded yet. */
const loadedBookmarks = reactive(new Map<BookmarksOwner, BookBookmark[]>())

/** The book's bookmarks if loaded, in reading order - reactive; undefined until `loadBookBookmarks` lands. */
export function loadedBookBookmarks(bookId: BookmarksOwner): BookBookmark[] | undefined {
  return loadedBookmarks.get(bookId)
}

/** Loads the book's list into the shared map (once; later calls are free). */
export async function loadBookBookmarks(bookId: BookmarksOwner): Promise<void> {
  if (loadedBookmarks.has(bookId)) return
  const stored = (await dbGet<BookBookmark[]>(BOOKMARKS_DB, bookmarksKey(bookId))) ?? []
  // An update may have landed while this read was in flight; its list is newer.
  if (!loadedBookmarks.has(bookId)) loadedBookmarks.set(bookId, stored)
}

// One chain per book, so two edits in flight (two panes on one book) never read the same
// list and the second write never drops the first one's change.
const pendingUpdates = new Map<BookmarksOwner, Promise<unknown>>()

/**
 * Read-modify-write of the book's list against what is STORED, never against a caller's
 * in-memory copy. The saved list, sorted into reading order, replaces the shared one; an
 * empty list removes the record.
 */
export function updateBookBookmarks(
  bookId: BookmarksOwner,
  change: (bookmarks: BookBookmark[]) => BookBookmark[],
): Promise<BookBookmark[]> {
  const previous = pendingUpdates.get(bookId) ?? Promise.resolve()
  const next = previous.catch(() => {}).then(async () => {
    const stored = (await dbGet<BookBookmark[]>(BOOKMARKS_DB, bookmarksKey(bookId))) ?? []
    const sorted = change(stored).sort((a, b) =>
      a.lineIndex - b.lineIndex || (a.letterOffset ?? 0) - (b.letterOffset ?? 0) || (b.pdfTop ?? 0) - (a.pdfTop ?? 0) || a.id - b.id)
    if (sorted.length === 0) await dbDelete(BOOKMARKS_DB, bookmarksKey(bookId))
    else await dbSet(BOOKMARKS_DB, bookmarksKey(bookId), sorted)
    loadedBookmarks.set(bookId, sorted)
    return sorted
  })
  pendingUpdates.set(bookId, next)
  void next.finally(() => {
    if (pendingUpdates.get(bookId) === next) pendingUpdates.delete(bookId)
  }).catch(() => {})
  return next
}
