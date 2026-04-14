/**
 * HebrewBooks download history store.
 * Sole owner of app-hb-history IDB access — no other file may import from idbPersistence for this DB.
 */
import { defineStore } from 'pinia'
import { idbHbGetHistory, idbHbTrackAccess } from '@/utils/persistence'
import type { HbHistoryEntry } from '@/utils/persistence'
import type { HebrewBook } from '@/components/hebrew-books/hebrewBooksCatalog'

export const useHebrewBooksHistoryStore = defineStore('hebrewBooksHistory', () => {
  function getHistory(): Promise<HebrewBook[]> {
    return idbHbGetHistory() as Promise<HebrewBook[]>
  }

  function trackAccess(book: HebrewBook): Promise<void> {
    const entry: HbHistoryEntry = { ...book, lastAccessed: Date.now() }
    return idbHbTrackAccess(entry)
  }

  return { getHistory, trackAccess }
})
