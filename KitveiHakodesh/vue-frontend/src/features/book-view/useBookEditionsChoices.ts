import { computed, onMounted } from 'vue'
import { useOnline } from '@vueuse/core'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useHebrewBooksSideEditionStore } from '@/stores/hebrewBooksSideEditionStore'
import { useUserEditionsStore } from '@/stores/userEditionsStore'
import { pickLocalFilePath } from '@/webview-host/bridge'
import { useHebrewBooksSideEditionMode } from './useHebrewBooksSideEditionMode'
import { useBookEditionOpen, userItemAsEdition } from './useBookEditionOpen'
import type { HebrewBooksEdition, UserEdition } from '@/webview-host/queries.types'
import { useI18n } from 'vue-i18n'

/**
 * What the scanned-editions list offers and what picking each thing does - ONE implementation
 * shared by the list shown in the toolbar dropdown (BookViewHebrewBooksEditionsList) and by the
 * dropdown's row in the toolbar's "more" menu, so the two never drift apart.
 *
 * Every pick opens at the outline entry for the section the reader is in now - read at click
 * time; a PDF whose bookmarks do not match opens at its start (useBookEditionOpen).
 * `onPicked` runs when something was picked (the host closes its list).
 */
export function useBookEditionsChoices(
  getEditions: () => HebrewBooksEdition[],
  getTalmudPdfPath: () => string | null,
  onPicked: () => void,
) {
  const { t } = useI18n()
  const paneNavigation = usePaneNavigation()
  const sideEditionStore = useHebrewBooksSideEditionStore()
  const userEditionsStore = useUserEditionsStore()
  const sideBySide = useHebrewBooksSideEditionMode()
  const editionOpen = useBookEditionOpen()
  const isOnline = useOnline()

  const bookId = computed(() => paneNavigation.activeTab.bookId ?? 0)
  const userEditions = computed(() => userEditionsStore.editionsOf(bookId.value))

  /** Suggestions the reader can open now: on the computer, or downloadable while online. */
  const visibleEditions = computed(() => getEditions().filter((e) => e.isOnDisk || isOnline.value))

  /** Loads the reader's own items for this book. */
  function loadUserEditions() {
    void userEditionsStore.load(bookId.value)
  }
  onMounted(loadUserEditions)

  function isShownSideBySide(edition: HebrewBooksEdition): boolean {
    return sideEditionStore.getShownEditionId() === edition.id
  }

  /** Author, place, year and page count - whichever of them the catalogue has. */
  function detailsFor(edition: HebrewBooksEdition): string {
    return [
      edition.author,
      edition.printingPlace,
      edition.printingYear,
      edition.pages ? t('bookView.hebrewBooks.pages', { n: edition.pages }) : '',
    ]
      .map((part) => (part ?? '').trim())
      .filter(Boolean)
      .join(' · ')
  }

  /** Why a user item cannot open now, or '' when it can. */
  function unavailableReason(item: UserEdition): string {
    if (item.kind === 'file') return item.fileExists ? '' : t('bookView.hebrewBooks.fileMissing')
    return item.isOnDisk || isOnline.value ? '' : t('bookView.hebrewBooks.notAvailableOffline')
  }

  function openHebrewBooksEdition(edition: HebrewBooksEdition) {
    onPicked()
    editionOpen.openHebrewBooksEdition(edition)
  }

  function openLocalPdf(filePath: string, title: string) {
    onPicked()
    editionOpen.openLocalPdf(filePath, title)
  }

  function openUserItem(item: UserEdition) {
    if (unavailableReason(item)) return
    if (item.kind === 'file' && item.filePath) openLocalPdf(item.filePath, item.title)
    else if (item.kind === 'hb' && item.hbId) openHebrewBooksEdition(userItemAsEdition(item))
  }

  function openTalmud() {
    const path = getTalmudPdfPath()
    if (path) openLocalPdf(path, paneNavigation.activeTab.title)
  }

  /** Drops an item from the list (the file itself is never touched). */
  function removeUserItem(item: UserEdition) {
    void userEditionsStore.remove(bookId.value, item.id)
  }

  function stripExtension(fileName: string): string {
    const dot = fileName.lastIndexOf('.')
    return dot > 0 ? fileName.substring(0, dot) : fileName
  }

  /** Asks for a PDF on this computer, adds it to the list and opens it. */
  async function addPdfFromComputer() {
    const picked = await pickLocalFilePath({ pdfOnly: true })
    if (!picked) return
    const title = stripExtension(picked.fileName)
    await userEditionsStore.addFile(bookId.value, picked.filePath, title)
    openLocalPdf(picked.filePath, title)
  }

  return {
    sideBySide,
    userEditions,
    visibleEditions,
    loadUserEditions,
    isShownSideBySide,
    detailsFor,
    unavailableReason,
    openHebrewBooksEdition,
    openUserItem,
    openTalmud,
    removeUserItem,
    addPdfFromComputer,
  }
}
