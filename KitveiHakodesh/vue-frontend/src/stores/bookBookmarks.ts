import { reactive } from 'vue'
import { queryUserSettings, executeUserSettings } from '@/webview-host/userSettingsDb'
import { USER_SETTINGS_SQL } from '@/webview-host/userSettingsDb.sql'

/**
 * The app's bookmarks — the places a reader marked in a book, like
 * a ribbon left between pages.
 *
 * Stored in the user annotations database (user_settings.db, table user_bookmarks) beside
 * notes and highlights, so they follow the library. Keyed by owner alone: global, not per tab
 * and not per workspace; one SELECT loads a book's whole list.
 *
 * Every loaded list is also held in ONE reactive map shared by all readers - the
 * toolbar's dropdown and its copy in the "more" flyout - so a
 * bookmark made or deleted in one shows up in all of them at once.
 */

// The old IndexedDB home of bookmarks; started fresh, so it is just removed.
try { globalThis.indexedDB?.deleteDatabase('app-bookmarks') } catch { /* no IndexedDB */ }

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

/** The owner column: a book id as text, or the PDF key as is. */
function ownerColumn(bookId: BookmarksOwner): string {
  return String(bookId)
}

interface BookmarkRow {
  bookmarkId: number
  lineId: number
  lineIndex: number
  letterOffset: number | null
  selectionLetterCount: number | null
  label: string
  tocPath: string | null
  snippet: string | null
  pdfTop: number | null
}

function fromRow(r: BookmarkRow): BookBookmark {
  const b: BookBookmark = { id: r.bookmarkId, lineId: r.lineId, lineIndex: r.lineIndex, label: r.label }
  if (r.letterOffset != null) b.letterOffset = r.letterOffset
  if (r.selectionLetterCount != null) b.selectionLetterCount = r.selectionLetterCount
  if (r.tocPath != null) b.tocPath = r.tocPath
  if (r.snippet != null) b.snippet = r.snippet
  if (r.pdfTop != null) b.pdfTop = r.pdfTop
  return b
}

function sameBookmark(a: BookBookmark, b: BookBookmark): boolean {
  return a.lineId === b.lineId && a.lineIndex === b.lineIndex && a.letterOffset === b.letterOffset
    && a.selectionLetterCount === b.selectionLetterCount && a.label === b.label
    && a.tocPath === b.tocPath && a.snippet === b.snippet && a.pdfTop === b.pdfTop
}

function sortBookmarks(list: BookBookmark[]): BookBookmark[] {
  return list.sort((a, b) =>
    a.lineIndex - b.lineIndex || (a.letterOffset ?? 0) - (b.letterOffset ?? 0) || (b.pdfTop ?? 0) - (a.pdfTop ?? 0) || a.id - b.id)
}

async function readStored(bookId: BookmarksOwner): Promise<BookBookmark[]> {
  const rows = await queryUserSettings<BookmarkRow>(USER_SETTINGS_SQL.GET_BOOKMARKS_FOR_OWNER, [ownerColumn(bookId)])
  return sortBookmarks(rows.map(fromRow))
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
  const stored = await readStored(bookId)
  // An update may have landed while this read was in flight; its list is newer.
  if (!loadedBookmarks.has(bookId)) loadedBookmarks.set(bookId, stored)
}

// One chain per book, so two edits in flight (two panes on one book) never read the same
// list and the second write never drops the first one's change.
const pendingUpdates = new Map<BookmarksOwner, Promise<unknown>>()

/**
 * Read-modify-write of the book's list against what is STORED, never against a caller's
 * in-memory copy. Only rows that changed are written; the saved list, sorted into reading
 * order, replaces the shared one.
 */
export function updateBookBookmarks(
  bookId: BookmarksOwner,
  change: (bookmarks: BookBookmark[]) => BookBookmark[],
): Promise<BookBookmark[]> {
  const previous = pendingUpdates.get(bookId) ?? Promise.resolve()
  const next = previous.catch(() => {}).then(async () => {
    const stored = await readStored(bookId)
    const before = new Map(stored.map((b) => [b.id, { ...b }]))
    const sorted = sortBookmarks(change(stored))
    const owner = ownerColumn(bookId)
    const kept = new Set(sorted.map((b) => b.id))
    for (const id of before.keys())
      if (!kept.has(id)) await executeUserSettings(USER_SETTINGS_SQL.DELETE_BOOKMARK, [owner, id])
    for (const b of sorted) {
      const old = before.get(b.id)
      if (old && sameBookmark(old, b)) continue
      await executeUserSettings(USER_SETTINGS_SQL.UPSERT_BOOKMARK, [
        owner, b.id, b.lineId, b.lineIndex, b.letterOffset ?? null, b.selectionLetterCount ?? null,
        b.label, b.tocPath ?? null, b.snippet ?? null, b.pdfTop ?? null,
      ])
    }
    loadedBookmarks.set(bookId, sorted)
    return sorted
  })
  pendingUpdates.set(bookId, next)
  void next.finally(() => {
    if (pendingUpdates.get(bookId) === next) pendingUpdates.delete(bookId)
  }).catch(() => {})
  return next
}
