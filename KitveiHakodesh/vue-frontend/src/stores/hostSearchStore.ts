import { defineStore } from 'pinia'
import { useTabStore } from './tabStore'
import { useBooksDataStore } from './booksDataStore'
import { onWebviewEvent } from '@/webview-host/seforimDb'
import { getLineIndexFromLineId } from '@/webview-host/seforimApi'

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
      void handleHostOpenBook(msg)
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
        tabStore.openTab({ route: '/books', title: 'קטלוג הספרים', catalogQuery: text })
      }
      return
    }

    // Default: full-text search. '@' is a book-filter separator in the FTS query
    // syntax, so drop it from a raw selection to avoid splitting the term.
    const ftsText = text.replace(/@/g, ' ').replace(/\s+/g, ' ').trim()
    if (!ftsText) return
    tabStore.openTab({ route: '/search', title: 'חיפוש: ' + ftsText, searchQuery: ftsText })
  }

  async function handleHostOpenBook(msg: Record<string, unknown>) {
    const bookId = typeof msg.bookId === 'number' ? msg.bookId : undefined
    if (bookId == null) return

    // Resolve the positional line index. Otzaria links already carry it; Zayit
    // links carry a DB line row-id that must be converted (and only resolves on a
    // machine whose DB matches the version the link was made against).
    let lineIndex: number | undefined
    let resolvedBookId = bookId
    if (typeof msg.index === 'number') {
      lineIndex = msg.index
    } else if (typeof msg.lineId === 'number') {
      try {
        const rows = await getLineIndexFromLineId(msg.lineId)
        lineIndex = rows[0]?.lineIndex
        // Trust the DB's bookId for the resolved line over the one in the link.
        if (rows[0]?.bookId != null) resolvedBookId = rows[0].bookId
      } catch {
        /* fall through — open the book at its start below */
      }
    }

    // Resolve a human title for the tab (falls back to a generic label).
    const booksData = useBooksDataStore()
    try { await booksData.ensureLoaded() } catch { /* title falls back */ }
    const title = booksData.allBooksMap.get(resolvedBookId)?.title ?? 'כתבי הקודש'

    // Otzaria links may carry highlight params (&m=<text> / &mark). We parse them
    // C#-side so any link type is accepted, but intentionally IGNORE the highlight
    // here — just open the book scrolled to the target line. (To honour it later,
    // set searchHighlightLineIndex + searchHighlightQuery/Terms from msg.markText.)
    tabStore.openTab({
      route: '/book-view',
      bookId: resolvedBookId,
      title,
      openTocLineIndex: lineIndex,
      // Momentarily flash the target line's background so the user sees where the
      // link landed (only meaningful when we resolved a line).
      flashOpenLine: lineIndex != null,
    })
  }

  return {}
})
