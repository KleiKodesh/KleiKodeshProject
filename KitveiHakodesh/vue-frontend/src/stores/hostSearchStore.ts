import { defineStore } from 'pinia'
import { useTabStore } from './tabStore'
import { useBooksDataStore } from './booksDataStore'
import { onWebviewEvent } from '@/webview-host/seforimDb'
import { getAllTocEntries, getBookIdByExactTitle, getLineIndexFromLineId } from '@/webview-host/seforimApi'
import type { ParsedBookLink } from '@/utils/appDeepLink'
import { t } from '@/i18n'

/**
 * Receives "navigate from the VSTO host" pushes and routes them to a page.
 *
 * The Word ribbon's right-click context menu calls into the C# AppViewer, which
 * pushes one of two events:
 *
 * 1. hostSearch — "העתק לחיפוש בכתבי הקודש" / "חיפוש ספר בכתבי הקודש":
 *      { event: 'hostSearch', target: 'fts' | 'catalog', text: '<cleaned selection>' }
 *    fts     → open a full-text-search tab seeded with the text (FullTextSearchPage
 *              auto-runs the search from tab.searchQuery on mount).
 *    catalog → open the book-catalog singleton seeded with the text via
 *              tab.catalogQuery (BookCatalogPage reads it on mount and searches).
 *
 * 2. hostOpenBook — "פתח קישור בכתבי הקודש": an otzaria://, kitveihakodeshapp:// or zayit://
 *    deep link found in the selection's hyperlinks, parsed C#-side into:
 *      { event: 'hostOpenBook', scheme: 'otzaria' | 'kitveihakodeshapp' | 'zayit', bookId,
 *        index, lineId, mark, markText }
 *    Otzaria and kitveihakodeshapp (this app's own links, see @/utils/appDeepLink) both
 *    carry a positional line `index` used directly; Zayit carries a DB `lineId` we
 *    convert to a positional index via getLineIndexFromLineId. Branch on which of
 *    `index`/`lineId` is present, NOT on `scheme` — the two indexed schemes are
 *    handled identically and `scheme` is informational.
 *
 * The listener is global and lives for the app's lifetime, so this store is
 * instantiated once at startup (main.ts) — before 'appReady' is posted, so any
 * request queued C#-side is delivered.
 */
export const useHostSearchStore = defineStore('hostSearch', () => {
  const tabStore = useTabStore()

  onWebviewEvent((msg) => {
    if (msg.event === 'hostSearch') {
      handleHostSearch(msg)
    } else if (msg.event === 'hostOpenBook') {
      handleHostOpenBook(msg)
    }
  })

  function handleHostSearch(msg: Record<string, unknown>) {
    const text = (msg.text as string | undefined)?.trim()
    if (!text) return
    const target = msg.target as string | undefined

    if (target === 'catalog') {
      // Deliberately reuses an open catalog tab rather than adding another: repeated
      // lookups from the host would otherwise pile up tabs the user never asked for.
      // Either way stamp catalogQuery so the page seeds it.
      const existing = tabStore.tabs.find((t) => t.route === '/books')
      if (existing) {
        tabStore.updateTab(existing.id, { catalogQuery: text })
        tabStore.switchTab(existing.id)
      } else {
        tabStore.openTab({ route: '/books', title: t('stores.tabTitle.catalog'), catalogQuery: text })
      }
      return
    }

    // Default: full-text search. '@' is a book-filter separator in the FTS query
    // syntax, so drop it from a raw selection to avoid splitting the term.
    const ftsText = text.replace(/@/g, ' ').replace(/\s+/g, ' ').trim()
    if (!ftsText) return
    tabStore.openTab({ route: '/search', title: t('stores.tabTitle.searchQuery', { query: ftsText }), searchQuery: ftsText })
  }

  function handleHostOpenBook(msg: Record<string, unknown>) {
    const numberOf = (value: unknown) => (typeof value === 'number' ? value : undefined)
    void openBookLink({
      scheme: typeof msg.scheme === 'string' ? msg.scheme : 'otzaria',
      bookId: numberOf(msg.bookId),
      title: typeof msg.title === 'string' && msg.title ? msg.title : undefined,
      headings: Array.isArray(msg.headings) ? msg.headings.filter((h): h is string => typeof h === 'string') : undefined,
      index: numberOf(msg.index),
      lineId: numberOf(msg.lineId),
      otherLibrary: msg.otherLibrary === true,
    })
  }

  /**
   * Opens a book link (a host deep link, a link clicked in a FastDocView document, one pasted into
   * a search bar) inside the app: the book by id, by title (the catalogue) or by a line's DB id, at
   * the line, the TOC heading path, or the top. False when the book is not in this library (the
   * caller tells the user); a line or heading that cannot be placed still opens the book.
   * Otzaria's &mark / &m= highlight is not honoured - the book opens scrolled to the line.
   */
  async function openBookLink(link: Omit<ParsedBookLink, 'scheme'> & { scheme: string }): Promise<boolean> {
    if (link.otherLibrary) return false
    const booksData = useBooksDataStore()
    try { await booksData.ensureLoaded() } catch { /* resolved below as far as possible */ }

    let bookId = link.bookId
    let lineIndex = link.index
    if (link.lineId != null) {
      try {
        const row = (await getLineIndexFromLineId(link.lineId))[0]
        // Trust the DB's book for the line over the one in the link.
        if (row) { lineIndex = row.lineIndex; bookId = row.bookId }
      } catch { /* open the book at its start */ }
    }
    if (bookId == null && link.title) bookId = await bookIdByTitle(link.title)
    if (bookId == null) return false
    const book = booksData.allBooksMap.get(bookId)
    // The catalogue is loaded and does not hold this id: not in this library.
    if (!book && booksData.allBooksMap.size > 0) return false

    if (lineIndex == null && link.headings?.length) lineIndex = await lineIndexOfHeadings(bookId, link.headings)
    tabStore.openTab({
      route: '/book-view',
      bookId,
      title: book?.title ?? t('stores.tabTitle.kitveiHakodesh'),
      openTocLineIndex: lineIndex,
      // Briefly flash the target line so the user sees where the link landed.
      flashOpenLine: lineIndex != null,
    })
    return true
  }

  /** A book by title: exact, then ignoring case, spacing, '_' for space and niqqud; then the DB's exact-title lookup. */
  async function bookIdByTitle(title: string): Promise<number | undefined> {
    const books = [...useBooksDataStore().allBooksMap.values()]
    const exact = books.find((book) => book.title === title)
    if (exact) return exact.id
    const wanted = normalizeTitle(title)
    const loose = books.find((book) => normalizeTitle(book.title) === wanted)
    if (loose) return loose.id
    try {
      return (await getBookIdByExactTitle(title))[0]?.id
    } catch {
      return undefined
    }
  }

  /** The line of a TOC heading path (Otzaria's book://<title>#<heading>#<sub-heading>): each heading after the one before it. */
  async function lineIndexOfHeadings(bookId: number, headings: string[]): Promise<number | undefined> {
    let entries
    try { entries = await getAllTocEntries(bookId) } catch { return undefined }
    let found: number | undefined
    let from = 0
    for (const heading of headings) {
      const wanted = normalizeTitle(heading)
      const at = entries.findIndex((entry, i) => i >= from && normalizeTitle(entry.text) === wanted)
      if (at < 0) break
      found = entries[at]!.lineIndex ?? found
      from = at + 1
    }
    return found
  }

  /** Opens a search link (otzaria://open/search?q=, zayit://search/) as a full-text search tab. */
  function openSearchLink(query: string) {
    handleHostSearch({ event: 'hostSearch', target: 'fts', text: query })
  }

  /** Searches the book catalog for a text (reuses an open catalog tab), as the Word ribbon does. */
  function openCatalogSearch(text: string) {
    handleHostSearch({ event: 'hostSearch', target: 'catalog', text })
  }

  return { openBookLink, openSearchLink, openCatalogSearch }
})

/** A title or heading compared loosely: no tags, niqqud or cantillation, '_' as space, collapsed spaces, lower case. */
function normalizeTitle(text: string): string {
  return text
    .replace(/<[^>]*>/g, '')
    .replace(/[֑-ׇ]/g, '')
    .replace(/_/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase()
}
