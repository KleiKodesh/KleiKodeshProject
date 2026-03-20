/**
 * IndexedDB persistence for per-tab and per-tab+book state.
 *
 * Two object stores:
 *   tab-state  — key: tabId          — value: TabState
 *   book-state — key: "tabId:bookId" — value: BookState
 *
 * Lifecycle:
 *   - Tab closed        → deleteTab(tabId)   clears tab-state + all book-state for that tab
 *   - Navigate from book → deleteBook(tabId, bookId) clears that book-state entry
 */

const DB_NAME = 'app-tab-state'
const TAB_STORE = 'tab-state'
const BOOK_STORE = 'book-state'

export interface TabState {
  bottomVisible: boolean
  tocVisible: boolean
}

export interface BookState {
  scrollIndex: number
  scrollOffset: number
  selectedLineId?: number | null
}

let db: IDBDatabase | null = null

function open(): Promise<IDBDatabase> {
  if (db) return Promise.resolve(db)
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, 1)
    req.onupgradeneeded = () => {
      req.result.createObjectStore(TAB_STORE)
      req.result.createObjectStore(BOOK_STORE)
    }
    req.onsuccess = () => { db = req.result; resolve(db) }
    req.onerror = () => reject(req.error)
  })
}

function bookKey(tabId: string, bookId: number) {
  return `${tabId}:${bookId}`
}

// ── Tab state ────────────────────────────────────────────────────────────────

export async function getTabState(tabId: string): Promise<TabState | null> {
  const store = (await open()).transaction(TAB_STORE).objectStore(TAB_STORE)
  return new Promise((resolve, reject) => {
    const req = store.get(tabId)
    req.onsuccess = () => resolve(req.result ?? null)
    req.onerror = () => reject(req.error)
  })
}

export async function setTabState(tabId: string, state: TabState): Promise<void> {
  const store = (await open()).transaction(TAB_STORE, 'readwrite').objectStore(TAB_STORE)
  return new Promise((resolve, reject) => {
    const req = store.put(state, tabId)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })
}

// ── Book state ───────────────────────────────────────────────────────────────

export async function getBookState(tabId: string, bookId: number): Promise<BookState | null> {
  const store = (await open()).transaction(BOOK_STORE).objectStore(BOOK_STORE)
  return new Promise((resolve, reject) => {
    const req = store.get(bookKey(tabId, bookId))
    req.onsuccess = () => resolve(req.result ?? null)
    req.onerror = () => reject(req.error)
  })
}

export async function setBookState(tabId: string, bookId: number, state: BookState): Promise<void> {
  const store = (await open()).transaction(BOOK_STORE, 'readwrite').objectStore(BOOK_STORE)
  return new Promise((resolve, reject) => {
    const req = store.put(state, bookKey(tabId, bookId))
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })
}

export async function deleteBook(tabId: string, bookId: number): Promise<void> {
  const store = (await open()).transaction(BOOK_STORE, 'readwrite').objectStore(BOOK_STORE)
  return new Promise((resolve, reject) => {
    const req = store.delete(bookKey(tabId, bookId))
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })
}

// ── Tab close ────────────────────────────────────────────────────────────────

/** Remove all state for a tab — call on tab close. */
export async function deleteTab(tabId: string): Promise<void> {
  const idb = await open()
  const prefix = `${tabId}:`

  await new Promise<void>((resolve, reject) => {
    const req = idb.transaction(TAB_STORE, 'readwrite').objectStore(TAB_STORE).delete(tabId)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })

  await new Promise<void>((resolve, reject) => {
    const req = idb.transaction(BOOK_STORE, 'readwrite').objectStore(BOOK_STORE).openCursor()
    req.onsuccess = () => {
      const cursor = req.result
      if (!cursor) { resolve(); return }
      if ((cursor.key as string).startsWith(prefix)) cursor.delete()
      cursor.continue()
    }
    req.onerror = () => reject(req.error)
  })
}
