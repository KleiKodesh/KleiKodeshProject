/**
 * Centralised persistence util — all localStorage access goes through here.
 * Call clearAll() to reset the entire app state.
 */

export const PERSIST_KEYS = {
  TABS: 'app.tabs',
  SETTINGS: 'app.settings',
  BOOKS_VIEW: 'app.books.view',
  BOOK_VIEW_TOOLBAR: 'app.bookView.toolbarVisible',
  BOOK_VIEW_SEARCH_BAR_POS: 'app.bookView.searchBarPos',
  BOOK_TAB: (tabId: string) => `app.bookTab.${tabId}`,
  BOOK_SCROLL: (tabId: string, bookId: number) => `app.bookTab.${tabId}.scroll.${bookId}`,
} as const

export function persistGet<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key)
    return raw !== null ? (JSON.parse(raw) as T) : fallback
  } catch {
    return fallback
  }
}

export function persistSet<T>(key: string, value: T): void {
  localStorage.setItem(key, JSON.stringify(value))
}

export function persistRemove(key: string): void {
  localStorage.removeItem(key)
}

/** Clears all app persistence — use for full app reset. */
export function clearAll(): void {
  const appKeys = Object.keys(localStorage).filter(k => k.startsWith('app.'))
  appKeys.forEach(k => localStorage.removeItem(k))
}
