import { defineStore } from 'pinia'
import { useTabStore } from './tabStore'
import { onWebviewEvent } from '@/webview-host/seforimDb'

/**
 * Receives "search from the VSTO host" pushes and routes them to a page.
 *
 * The Word ribbon's right-click context menu ("העתק לחיפוש בכתבי הקודש" /
 * "חיפוש ספר בכתבי הקודש") calls AppViewer.SearchFromHost on the C# side, which
 * strips the selection to searchable words and pushes:
 *
 *   { event: 'hostSearch', target: 'fts' | 'catalog', text: '<cleaned selection>' }
 *
 * fts     → open a full-text-search tab seeded with the text (FullTextSearchPage
 *           auto-runs the search from tab.searchQuery on mount).
 * catalog → open the book-catalog singleton seeded with the text via
 *           tab.catalogQuery (BookCatalogPage reads it on mount and searches
 *           reactively).
 *
 * The listener is global and lives for the app's lifetime, so this store is
 * instantiated once at startup (main.ts) — before 'appReady' is posted, so any
 * request queued C#-side is delivered.
 */
export const useHostSearchStore = defineStore('hostSearch', () => {
  const tabStore = useTabStore()

  onWebviewEvent((msg) => {
    if (msg.event !== 'hostSearch') return
    const text = (msg.text as string | undefined)?.trim()
    if (!text) return
    const target = msg.target as string | undefined

    if (target === 'catalog') {
      // '/books' is a singleton — reuse the existing catalog tab if present,
      // otherwise open one. Either way stamp catalogQuery so the page seeds it.
      const existing = tabStore.tabs.find((t) => t.route === '/books')
      if (existing) {
        tabStore.updateTab(existing.id, { catalogQuery: text })
        tabStore.switchTab(existing.id)
      } else {
        tabStore.openTab({ route: '/books', title: 'ספרים', catalogQuery: text })
      }
      return
    }

    // Default: full-text search. '@' is a book-filter separator in the FTS query
    // syntax, so drop it from a raw selection to avoid splitting the term.
    const ftsText = text.replace(/@/g, ' ').replace(/\s+/g, ' ').trim()
    if (!ftsText) return
    tabStore.openTab({ route: '/search', title: 'חיפוש: ' + ftsText, searchQuery: ftsText })
  })

  return {}
})
