import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useTabStore } from '@/stores/tabStore'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { restoreLocalFile, triggerHbDownload } from '@/webview-host/bridge'
import { getHbPdfUrl, type HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import { addinDisplayTitle } from '@/features/local-file-search/otzariaAddins'
import type { FileSearchResult } from './useHomeSearch'
import type { TocFsItem } from '@/features/book-catalog/useBookCatalogSearch'
import type { RecentlyOpenedEntry } from '@/stores/recentlyOpenedStore'

/**
 * Turns a selection in the home search dropdown (or a recently-opened tile) into
 * a navigation. Every handler calls `resetSearch` first — the dropdown closes and
 * the query clears, so returning to the home page shows a blank search bar.
 *
 * All four sources honour `openInNewTab` (Ctrl/⌘-click).
 */
export function useHomeSearchNavigation(resetSearch: () => void) {
  const paneNavigation = usePaneNavigation()
  const tabStore = useTabStore()
  const localFileStore = useLocalFileStore()
  const settingsStore = useSettingsStore()
  const hebrewBooksHistoryStore = useHebrewBooksHistoryStore()

  function onSelectCatalogBook(bookId: number, bookTitle: string, openInNewTab = false) {
    resetSearch()
    paneNavigation.openOrUpdateActiveTab(
      { route: '/book-view', title: bookTitle, bookId },
      openInNewTab,
    )
  }

  function onSelectCatalogToc(item: TocFsItem, openInNewTab = false) {
    resetSearch()
    paneNavigation.openOrUpdateActiveTab(
      {
        route: '/book-view',
        title: item.book.title,
        bookId: item.book.id,
        openTocEntryId: item.tocEntryId,
        openTocLineIndex: item.tocLineIndex ?? undefined,
      },
      openInNewTab,
    )
  }

  function onSelectHebrewBook(book: HebrewBook, openInNewTab = false) {
    resetSearch()
    hebrewBooksHistoryStore.trackAccess(book)
    // Download lifecycle is tab-id-driven (see useHebrewBooks.openBook) — for a
    // Ctrl/⌘-click open a fresh placeholder tab and target its id.
    const tabId = openInNewTab
      ? paneNavigation.openTab({ route: '/pdf-view', title: book.title }).id
      : paneNavigation.activeTabId
    localFileStore.startHbDownload(book.title, tabId)
    triggerHbDownload(
      String(book.id),
      book.title,
      getHbPdfUrl(book.id),
      tabId,
      settingsStore.hebrewBooksLocalFolder || undefined,
      navigator.onLine,
    ).catch(() => {})
  }

  async function onSelectFile(item: FileSearchResult, openInNewTab = false) {
    resetSearch()
    // Dev opens local files too now: restoreLocalFile authorizes the path with the service and
    // serves it through the same-origin /khs-file proxy (hosted keeps its C# path).

    const { fullPath, fileName } = item
    const extension = fileName.substring(fileName.lastIndexOf('.')).toLowerCase()
    const dotIndex = fileName.lastIndexOf('.')
    const titleWithoutExtension = dotIndex > 0 ? fileName.substring(0, dotIndex) : fileName

    const isHtmlLike = extension === '.htm' || extension === '.html'
    const route = extension === '.txt' ? '/txt-view' : isHtmlLike ? '/html-view' : '/pdf-view'

    // Capture the target tab id up front (a new tab for Ctrl/⌘-click, else the
    // current active tab) and patch it by id — restoreLocalFile awaits, and the
    // active tab may change during that await.
    const targetTabId = openInNewTab
      ? paneNavigation.openTab({ route, title: titleWithoutExtension }).id
      : paneNavigation.activeTabId

    if (extension === '.txt') {
      tabStore.updateTab(targetTabId, {
        route: '/txt-view',
        title: titleWithoutExtension,
        localFileName: fileName,
        localFilePath: fullPath,
        localFileVirtualUrl: undefined,
      })
      return
    }

    const restored = await restoreLocalFile(fullPath)
    if (!restored?.url) return
    // Route by what is actually served (dev Word docs may render to HTML via the fallback).
    const servedRoute =
      restored.kind === 'html' ? '/html-view' : restored.kind === 'pdf' ? '/pdf-view' : route

    // An Otzaria addin is presented by its addin name, and the tab is flagged so
    // HtmlViewPage activates the addin bridge — same as the file-search page.
    //
    // isOtzariaAddin is written unconditionally, never spread in only when true:
    // updateTab merges, so an omitted key leaves a previous addin's `true` on the
    // tab, and the next plain HTML file opened here would get the bridge injected
    // into it. Gated on the served route for the same reason bridge.ts and
    // localFileStore.ts gate theirs — only /html-view ever reads the flag.
    tabStore.updateTab(targetTabId, {
      route: servedRoute,
      title: item.addinName ? addinDisplayTitle(item.addinName) : titleWithoutExtension,
      localFileName: fileName,
      localFilePath: fullPath,
      localFileVirtualUrl: restored.url,
      isOtzariaAddin: !!item.addinName && servedRoute === '/html-view',
    })
  }

  function openRecentEntry(entry: RecentlyOpenedEntry, openInNewTab = false) {
    if (entry.route === '/book-view' && entry.bookId !== undefined) {
      paneNavigation.openOrUpdateActiveTab(
        { route: '/book-view', title: entry.title, bookId: entry.bookId },
        openInNewTab,
      )
      return
    }
    localFileStore.openFromHistory(entry, openInNewTab)
  }

  function openFullTextSearch(query: string) {
    paneNavigation.updateActiveTab({
      route: '/search',
      title: `חיפוש: ${query}`,
      searchQuery: query,
    })
  }

  return {
    onSelectCatalogBook,
    onSelectCatalogToc,
    onSelectHebrewBook,
    onSelectFile,
    openRecentEntry,
    openFullTextSearch,
  }
}
