import { ref, computed } from 'vue'
import { useDebounceFn } from '@vueuse/core'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { searchHbCatalog, getHbPdfUrl, type HebrewBook } from './hebrewBooksCatalog'
import { useLocalFileStore } from '@/stores/localFileStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useSettingsStore } from '@/stores/settingsStore'
import { triggerHbDownload, triggerHbSaveAs, deleteHbLocalFile, checkHbLocalFiles, revealHbLocalFile } from '@/webview-host/bridge'

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
      error.value = 'שגיאה בטעינת הספרים'
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
    trackAccess(book)
    // The whole download→convert→navigate lifecycle is driven by tab id
    // (startHbDownload / finishHbDownload / cancelHbDownload all target the tab
    // via updateTab). For a Ctrl/⌘-click we open a fresh placeholder tab and
    // hand its id to the download so the result lands there instead of here.
    const tabId = openInNewTab
      ? paneNavigation.openTab({ route: '/pdf-view', title: book.title }).id
      : paneNavigation.activeTabId
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

  function downloadBook(book: HebrewBook) {
    if (!navigator.onLine) {
      localFileStore.downloadErrorMessage = 'אין חיבור לאינטרנט'
      return
    }
    triggerHbSaveAs(String(book.id), book.title, getHbPdfUrl(book.id)).catch(() => {})
  }

  async function deleteLocalFile(book: HebrewBook) {
    const folder = settings.hebrewBooksLocalFolder
    if (!folder) return
    const result = await deleteHbLocalFile(String(book.id), folder).catch(
      () => ({ error: 'שגיאה' }) as { error: string },
    )
    if ('error' in result && result.error) {
      localFileStore.downloadErrorMessage = result.error
    } else if ('notFound' in result && result.notFound) {
      localFileStore.downloadErrorMessage = 'הקובץ לא נמצא בתיקייה'
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
    load,
    search,
    trackAccess,
    openBook,
    downloadBook,
    deleteLocalFile,
    revealInFolder,
  }
}
