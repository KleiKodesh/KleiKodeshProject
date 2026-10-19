import { useBooksDataStore } from '@/stores/booksDataStore'

/**
 * The title to SHOW for a tab / recent / history entry. A /book-view entry with a
 * catalog book id renders the catalog's current title; the stored title is only
 * the fallback — local files, HebrewBooks, and ids the catalog doesn't have, plus
 * the moment before the catalog has loaded (it never waits for it: first paint
 * shows the stored title, then switches reactively once allBooksMap fills).
 */
export function catalogBookTitle(entry: { route: string; bookId?: number; title: string }): string {
  if (entry.route !== '/book-view' || entry.bookId == null) return entry.title
  return useBooksDataStore().allBooksMap.get(entry.bookId)?.title ?? entry.title
}
