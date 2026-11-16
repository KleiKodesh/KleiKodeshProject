import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useHebrewBooks } from '@/features/hebrewbooks/useHebrewBooks'
import { useHebrewBooksSideEditionStore } from '@/stores/hebrewBooksSideEditionStore'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useHebrewBooksSideEditionMode } from './useHebrewBooksSideEditionMode'
import type { HebrewBooksEdition, UserEdition } from '@/webview-host/queries.types'

/** A HebrewBooks user item as an edition, so it opens (and shows side by side) like one. */
export function userItemAsEdition(item: UserEdition): HebrewBooksEdition {
  return {
    id: Number(item.hbId),
    title: item.title,
    author: '',
    printingPlace: '',
    printingYear: '',
    pages: null,
    isBest: false,
    isCertain: true,
    isOnDisk: item.isOnDisk,
  } as HebrewBooksEdition
}

/**
 * Opening a scanned edition from the book view, at the section the reader is in now (read at
 * call time). A HebrewBooks edition follows the side-panel toggle; a PDF on this computer
 * always opens in a new tab. A PDF whose bookmarks do not match opens at its start.
 */
export function useBookEditionOpen(paneId: 1 | 2) {
  const paneNavigation = usePaneNavigation()
  const { openEdition } = useHebrewBooks()
  const sideEditionStore = useHebrewBooksSideEditionStore()
  const localFileStore = useLocalFileStore()
  const { opensSideBySide } = useHebrewBooksSideEditionMode(paneId)

  function openHebrewBooksEdition(edition: HebrewBooksEdition) {
    if (opensSideBySide.value) sideEditionStore.pickEdition(paneId, paneNavigation.activeTab.id, paneNavigation.activeTab.bookId, edition.id)
    else openEdition(edition, 'newTab', paneNavigation.activeTab.tocPath)
  }

  function openLocalPdf(filePath: string, title: string) {
    const tabId = paneNavigation.openTab({
      route: '/pdf-view',
      title,
      localFileName: title,
      localFilePath: filePath,
      pdfInitialOutlineTarget: paneNavigation.activeTab.tocPath || undefined,
    }).id
    void localFileStore.restoreTab(tabId)
  }

  return { openHebrewBooksEdition, openLocalPdf }
}
