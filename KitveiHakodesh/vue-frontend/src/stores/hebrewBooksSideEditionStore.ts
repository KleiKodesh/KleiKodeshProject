import { defineStore } from 'pinia'
import { ref } from 'vue'

/**
 * Per split-view pane: which HebrewBooks edition the book view shows side by side in its
 * left side panel, and whether one is on screen right now.
 *
 * A store rather than local state because three places far apart in the tree need it - the
 * editions dropdown (deep in the book-view toolbar) picks, BookViewPage shows, and the app
 * title bar offers OCR while an edition is on screen.
 */
export const useHebrewBooksSideEditionStore = defineStore('hebrewBooksSideEdition', () => {
  /**
   * The edition the user picked per tab + book (key `${tabId}:${bookId}`); a missing key =
   * the book's best edition (picked automatically). Live state only: it is persisted with
   * the book view's position (BookState / LastReadState / nav locations - see
   * SideEditionPersistState) and seeded back from there by restorePickedEdition.
   */
  const pickedEditionIdByTabBook = ref<Record<string, number>>({})
  /** Bumped on every pick so the book view opens its left panel even for a repeat pick. */
  const pickSignal = ref<{ paneId: 1 | 2; count: number }>({ paneId: 1, count: 0 })
  /** The edition on screen in the left side panel right now; null = none shown. */
  const shownEditionIdByPane = ref<Record<1 | 2, number | null>>({ 1: null, 2: null })

  function getPickedEditionId(tabId: string, bookId: number | undefined): number | null {
    return pickedEditionIdByTabBook.value[`${tabId}:${bookId}`] ?? null
  }

  function setPickedEditionId(tabId: string, bookId: number | undefined, editionId: number) {
    if (bookId != null) pickedEditionIdByTabBook.value = { ...pickedEditionIdByTabBook.value, [`${tabId}:${bookId}`]: editionId }
  }

  /** The reader's pick from the editions dropdown - also opens the left column. */
  function pickEdition(paneId: 1 | 2, tabId: string, bookId: number | undefined, editionId: number) {
    setPickedEditionId(tabId, bookId, editionId)
    pickSignal.value = { paneId, count: pickSignal.value.count + 1 }
  }

  /** A saved pick coming back from session restore - opens nothing by itself. */
  function restorePickedEdition(tabId: string, bookId: number | undefined, editionId: number) {
    setPickedEditionId(tabId, bookId, editionId)
  }

  function getShownEditionId(paneId: 1 | 2): number | null {
    return shownEditionIdByPane.value[paneId]
  }

  function isEditionShown(paneId: 1 | 2): boolean {
    return shownEditionIdByPane.value[paneId] != null
  }

  function setShownEditionId(paneId: 1 | 2, editionId: number | null) {
    shownEditionIdByPane.value[paneId] = editionId
  }

  return {
    pickedEditionIdByTabBook,
    pickSignal,
    shownEditionIdByPane,
    getPickedEditionId,
    pickEdition,
    restorePickedEdition,
    getShownEditionId,
    isEditionShown,
    setShownEditionId,
  }
})
