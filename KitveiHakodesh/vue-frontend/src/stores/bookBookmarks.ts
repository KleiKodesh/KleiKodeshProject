import { dbGet, dbSet, dbDelete } from '@/utils/persistence'

/**
 * The `app-bookmarks` slice of persistence — the places a reader marked in a book, like
 * a ribbon left between pages.
 *
 * Keyed by book id alone: global, not per tab and not per workspace. One record per book
 * holds that book's whole list, so opening the bookmarks dropdown costs one read.
 */

const BOOKMARKS_DB = 'app-bookmarks'

export interface BookBookmark {
  /** Unique within the book; the creation time, so it also orders bookmarks made on the same line. */
  id: number
  lineId: number
  /** Fallback when the line id no longer resolves (a library update renumbered the rows). */
  lineIndex: number
  /** Characters into the line's text where the bookmark points; absent = the line's start. */
  characterOffset?: number
  /** Set when the bookmark was made from a selection: how many characters it covers. */
  selectionLength?: number
  /** The reader's name for it; defaults to the TOC path of the marked line. */
  label: string
  /** The TOC path of the marked line, kept apart from the label so a rename does not lose it. */
  tocPath?: string
  /** The bookmarked text - the selection, or the words at the point - shown under the name. */
  snippet?: string
}

function bookmarksKey(bookId: number): string {
  return `bookmarks:${bookId}`
}

/** The book's bookmarks, in reading order. */
export async function readBookBookmarks(bookId: number): Promise<BookBookmark[]> {
  return (await dbGet<BookBookmark[]>(BOOKMARKS_DB, bookmarksKey(bookId))) ?? []
}

// One chain per book, so two edits in flight (two panes on one book) never read the same
// list and the second write never drops the first one's change.
const pendingUpdates = new Map<number, Promise<unknown>>()

/**
 * Read-modify-write of the book's list against what is STORED, never against a caller's
 * in-memory copy: another pane may have changed it since that copy was read. Returns the
 * saved list, sorted into reading order; an empty list removes the record.
 */
export function updateBookBookmarks(
  bookId: number,
  change: (bookmarks: BookBookmark[]) => BookBookmark[],
): Promise<BookBookmark[]> {
  const previous = pendingUpdates.get(bookId) ?? Promise.resolve()
  const next = previous.catch(() => {}).then(async () => {
    const sorted = change(await readBookBookmarks(bookId))
      .sort((a, b) =>
        a.lineIndex - b.lineIndex || (a.characterOffset ?? 0) - (b.characterOffset ?? 0) || a.id - b.id)
    if (sorted.length === 0) await dbDelete(BOOKMARKS_DB, bookmarksKey(bookId))
    else await dbSet(BOOKMARKS_DB, bookmarksKey(bookId), sorted)
    return sorted
  })
  pendingUpdates.set(bookId, next)
  void next.finally(() => {
    if (pendingUpdates.get(bookId) === next) pendingUpdates.delete(bookId)
  }).catch(() => {})
  return next
}
