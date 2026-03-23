/**
 * IndexedDB persistence for per-tab and per-tab+book state.
 *
 * Three object stores:
 *   tab-state       — key: tabId          — value: TabState
 *   book-state      — key: "tabId:bookId" — value: BookState
 *   book-last-read  — key: bookId         — value: LastReadState
 *
 * Lifecycle:
 *   - Tab closed        → deleteTab(tabId)   clears tab-state + all book-state for that tab
 *   - Navigate from book → deleteBook(tabId, bookId) clears that book-state entry
 *   - book-last-read entries are permanent (global resume position, never auto-deleted)
 */

const DB_NAME = 'app-tab-state'
const DB_VERSION = 2
const TAB_STORE = 'tab-state'
const BOOK_STORE = 'book-state'
const LAST_READ_STORE = 'book-last-read'

export interface TabState {
  bottomVisible: boolean
  tocVisible: boolean
}

export interface BookState {
  scrollIndex: number
  scrollOffset: number
  selectedLineId?: number | null
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
}

export interface LastReadState {
  scrollIndex: number
  scrollOffset: number
  selectedLineId?: number | null
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
}

let db: IDBDatabase | null = null

function open(): Promise<IDBDatabase> {
  if (db) return Promise.resolve(db)
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION)
    req.onupgradeneeded = (e) => {
      const idb = req.result
      if (!idb.objectStoreNames.contains(TAB_STORE)) idb.createObjectStore(TAB_STORE)
      if (!idb.objectStoreNames.contains(BOOK_STORE)) idb.createObjectStore(BOOK_STORE)
      if (!idb.objectStoreNames.contains(LAST_READ_STORE)) idb.createObjectStore(LAST_READ_STORE)
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

// ── Global last-read position per book ───────────────────────────────────────

export async function getLastRead(bookId: number): Promise<LastReadState | null> {
  const store = (await open()).transaction(LAST_READ_STORE).objectStore(LAST_READ_STORE)
  return new Promise((resolve, reject) => {
    const req = store.get(bookId)
    req.onsuccess = () => resolve(req.result ?? null)
    req.onerror = () => reject(req.error)
  })
}

export async function setLastRead(bookId: number, state: LastReadState): Promise<void> {
  const store = (await open()).transaction(LAST_READ_STORE, 'readwrite').objectStore(LAST_READ_STORE)
  return new Promise((resolve, reject) => {
    const req = store.put(state, bookId)
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
