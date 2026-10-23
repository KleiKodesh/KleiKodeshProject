import { ref, computed } from 'vue'
import { t } from '@/i18n'
import { useDebounceFn } from '@vueuse/core'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { searchHbCatalog, getHbPdfUrl, type HebrewBook } from './hebrewBooksCatalog'
import { useLocalFileStore } from '@/stores/localFileStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useSettingsStore } from '@/stores/settingsStore'
import type { HebrewBooksEdition } from '@/webview-host/queries.types'
import { triggerHbDownload, triggerHbSaveAs, deleteHbLocalFile, checkHbLocalFiles, revealHbLocalFile } from '@/webview-host/bridge'

/**
 * Where an edition opened from a text book lands in a tab. Only 'newTab' is wired; 'splitView'
 * is reserved - only `resolveEditionTabId` would change. Side by side in the book view's left
 * panel is not a tab at all: BookViewHebrewBooksSideEdition fetches the PDF itself.
 */
export type HebrewBooksEditionTarget = 'newTab' | 'splitView'

/** An edition as the HebrewBooks book record the history and download flow key on. */
export function editionAsHebrewBook(edition: HebrewBooksEdition): HebrewBook {
  const { id, title, author, printingPlace, printingYear, pages } = edition
  return { id, title, author, printingPlace, printingYear, pages, categories: '' }
}

export function useHebrewBooks() {
  const localFileStore = useLocalFileStore()
  const history = useHebrewBooksHistoryStore()
  const settings = useSettingsStore()
  const paneNavigation = usePaneNavigation()

  const books = ref<HebrewBook[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const searchTerm = ref('')

  // IDs of books whose PDF exists in the configured local folder.
  // For search results: populated directly from the hasLocalFile flag C# stamps on each result.
  // For history items: populated via a checkHbLocalFiles round-trip (C# never sees history IDs).
  const localFileBookIds = ref(new Set<string>())

  // IDs of books whose Save As download is running; their download button spins.
  const downloadingBookIds = ref(new Set<string>())

  // History path — C# doesn't know these IDs, so we ask explicitly.
  async function refreshLocalFileIdsFromHistory(bookList: HebrewBook[]) {
    const folder = settings.hebrewBooksLocalFolder
    if (!folder || !bookList.length) {
      localFileBookIds.value = new Set()
      return
    }
    const ids = bookList.map((book) => String(book.id))
    const result = await checkHbLocalFiles(ids, folder).catch(() => ({ existingIds: [] }))
    localFileBookIds.value = new Set(result.existingIds ?? [])
  }

  // Search path — C# already stamped hasLocalFile on each result, no extra call needed.
  function applyLocalFileIdsFromSearchResults(bookList: HebrewBook[]) {
    const ids = new Set<string>()
    for (const book of bookList) {
      if (book.hasLocalFile) ids.add(String(book.id))
    }
    localFileBookIds.value = ids
  }

  async function load() {
    isLoading.value = true
    error.value = null
    try {
      books.value = await history.getHistory()
      await refreshLocalFileIdsFromHistory(books.value)
    } catch {
      error.value = t('hebrewBooks.error.loadFailed')
    } finally {
      isLoading.value = false
    }
  }

  const runSearch = useDebounceFn(async (term: string) => {
    if (!term.trim()) {
      books.value = await history.getHistory()
      await refreshLocalFileIdsFromHistory(books.value)
    } else {
      books.value = await searchHbCatalog(term, settings.hebrewBooksLocalFolder || undefined)
      applyLocalFileIdsFromSearchResults(books.value)
    }
  }, 200)

  function search(term: string) {
    searchTerm.value = term
    runSearch(term)
  }

  async function trackAccess(book: HebrewBook) {
    await history.trackAccess(book)
  }

  function openBook(book: HebrewBook, openInNewTab = false) {
    // The whole download→convert→navigate lifecycle is driven by tab id
    // (startHbDownload / finishHbDownload / cancelHbDownload all target the tab
    // via updateTab). For a Ctrl/⌘-click we open a fresh placeholder tab and
    // hand its id to the download so the result lands there instead of here.
    const tabId = openInNewTab
      ? paneNavigation.openTab({ route: '/pdf-view', title: book.title }).id
      : paneNavigation.activeTabId
    startBookDownload(book, tabId)
  }

  /**
   * Opens a scanned edition of the text book the reader is in, through the same
   * local-folder / cache / download flow as any HebrewBooks book. `outlineTarget` is the
   * book view breadcrumb; the PDF viewer jumps to the matching outline entry once loaded.
   */
  function openEdition(
    edition: HebrewBooksEdition,
    target: HebrewBooksEditionTarget,
    outlineTarget?: string,
  ) {
    const tabId = resolveEditionTabId(edition, target, outlineTarget)
    startBookDownload(editionAsHebrewBook(edition), tabId)
  }

  /** The placeholder tab an edition downloads into - the one place a target differs. */
  function resolveEditionTabId(
    edition: HebrewBooksEdition,
    _target: HebrewBooksEditionTarget,
    outlineTarget?: string,
  ): string {
    return paneNavigation.openTab({
      route: '/pdf-view',
      title: edition.title,
      pdfInitialOutlineTarget: outlineTarget || undefined,
    }).id
  }

  /** Records the access and starts the download into `tabId`, which then shows the PDF. */
  function startBookDownload(book: HebrewBook, tabId: string) {
    trackAccess(book)
    localFileStore.startHbDownload(book.title, tabId, String(book.id))
    triggerHbDownload(
      String(book.id),
      book.title,
      getHbPdfUrl(book.id),
      tabId,
      settings.hebrewBooksLocalFolder || undefined,
      navigator.onLine,
    ).catch(() => {})
  }

  async function downloadBook(book: HebrewBook) {
    if (!navigator.onLine) {
      localFileStore.downloadErrorMessage = t('hebrewBooks.error.noInternet')
      return
    }
    const id = String(book.id)
    if (downloadingBookIds.value.has(id)) return
    downloadingBookIds.value = new Set(downloadingBookIds.value).add(id)
    try {
      const result = await triggerHbSaveAs(id, book.title, getHbPdfUrl(book.id))
      if (result.notFound) localFileStore.downloadErrorMessage = t('hebrewBooks.error.notOnServer')
      else if (result.error) localFileStore.downloadErrorMessage = t('hebrewBooks.error.downloadFailed')
    } catch {
      localFileStore.downloadErrorMessage = t('hebrewBooks.error.downloadFailed')
    } finally {
      const updated = new Set(downloadingBookIds.value)
      updated.delete(id)
      downloadingBookIds.value = updated
    }
  }

  async function deleteLocalFile(book: HebrewBook, toRecycleBin = false) {
    const folder = settings.hebrewBooksLocalFolder
    if (!folder) return
    const result = await deleteHbLocalFile(String(book.id), folder, toRecycleBin).catch(
      () => ({ error: t('hebrewBooks.error.generic') }) as { error: string },
    )
    if ('error' in result && result.error) {
      localFileStore.downloadErrorMessage = result.error
    } else if ('noRecycleBin' in result && result.noRecycleBin) {
      localFileStore.downloadErrorMessage = t('hebrewBooks.error.noRecycleBin')
    } else if ('notFound' in result && result.notFound) {
      localFileStore.downloadErrorMessage = t('hebrewBooks.error.fileNotInFolder')
    } else if ('ok' in result && result.ok) {
      // Remove immediately so the button disappears without a round-trip.
      const updated = new Set(localFileBookIds.value)
      updated.delete(String(book.id))
      localFileBookIds.value = updated
    }
  }

  function revealInFolder(book: HebrewBook) {
    const folder = settings.hebrewBooksLocalFolder
    if (!folder) return
    revealHbLocalFile(String(book.id), folder).catch(() => {})
  }

  const displayedBooks = computed(() => books.value)

  return {
    displayedBooks,
    isLoading,
    error,
    searchTerm,
    localFileBookIds,
    downloadingBookIds,
    load,
    search,
    trackAccess,
    openBook,
    openEdition,
    downloadBook,
    deleteLocalFile,
    revealInFolder,
  }
}
