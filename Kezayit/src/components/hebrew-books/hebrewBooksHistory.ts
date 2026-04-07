import { idbHbGetHistory, idbHbTrackAccess, type HbHistoryEntry } from '@/utils/idbPersistence'
import type { HebrewBook } from './hebrewBooksCatalog'

export const hebrewBooksHistory = {
  getHistory(): Promise<HebrewBook[]> {
    return idbHbGetHistory() as Promise<HebrewBook[]>
  },

  trackAccess(book: HebrewBook): Promise<void> {
    const entry: HbHistoryEntry = { ...book, lastAccessed: Date.now() }
    return idbHbTrackAccess(entry)
  },
}
