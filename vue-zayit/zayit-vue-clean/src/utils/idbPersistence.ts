/**
 * Unified IndexedDB persistence.
 *
 * Single object store `app-state` with prefixed string keys:
 *
 *   settings:{key}                    — individual scalar settings (one key per setting)
 *   settings:workspaces               — WorkspaceList (all workspaces + active id)
 *   tabs:{wsId}:list                  — tab list + activeTabId + nextId counter (workspace-scoped)
 *   tab:{wsId}:{tabId}                — TabState (bottomVisible, workspace-scoped)
 *   book:{wsId}:{tabId}:{bookId}      — BookState (scroll, selectedLine, commentary scroll)
 *   lastread:{bookId}                 — LastReadState (global per-book resume, capped at 1000)
 */

const DB_NAME = 'app-state'
const DB_VERSION = 1
const STORE = 'app-state'

const LASTREAD_MAX = 1000

export interface TabState {
  bottomVisible: boolean
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

// ── Workspace types ───────────────────────────────────────────────────────────

export interface Workspace {
  id: string
  name: string
  createdAt: number
}

export interface WorkspaceList {
  workspaces: Workspace[]
  activeId: string
}

// ── Keys ─────────────────────────────────────────────────────────────────────

export const KEYS = {
  // workspace registry
  SETTINGS_WORKSPACES: 'settings:workspaces',

  // bookViewStore settings
  SETTINGS_BOOKS_VIEW:      'settings:books.view',
  SETTINGS_TOOLBAR:         'settings:bookView.toolbarVisible',
  SETTINGS_SEARCH_BAR_POS:  'settings:bookView.searchBarPos',
  SETTINGS_ZOOM:            'settings:bookView.zoom',

  // settingsStore — one key per setting
  SETTINGS_CENSOR_DIVINE:   'settings:censorDivineNames',
  SETTINGS_DIACRITICS:      'settings:diacriticsState',
  SETTINGS_HEADER_FONT:     'settings:headerFont',
  SETTINGS_TEXT_FONT:       'settings:textFont',
  SETTINGS_FONT_SIZE:       'settings:fontSize',
  SETTINGS_LINE_PADDING:    'settings:linePadding',
  SETTINGS_COMMENTARY_HEADER_FONT:  'settings:commentaryHeaderFont',
  SETTINGS_COMMENTARY_TEXT_FONT:    'settings:commentaryTextFont',
  SETTINGS_COMMENTARY_FONT_SIZE:    'settings:commentaryFontSize',
  SETTINGS_COMMENTARY_LINE_PADDING: 'settings:commentaryLinePadding',
  SETTINGS_SEPARATE_COMMENTARY:     'settings:useSeparateCommentarySettings',
  SETTINGS_APP_ZOOM:        'settings:appZoom',
  SETTINGS_NEW_TAB_PAGE:    'settings:newTabPage',
  SETTINGS_PDF_FILTERS:     'settings:pdfPageFilters',
  SETTINGS_RESUME_LAST_READ:'settings:resumeLastRead',

  // themeStore
  SETTINGS_THEME:           'settings:theme',
  SETTINGS_CUSTOM_THEMES:   'settings:customThemes',

  // workspace-scoped keys
  tabsList: (wsId: string)                              => `tabs:${wsId}:list`,
  tab:      (wsId: string, tabId: string)               => `tab:${wsId}:${tabId}`,
  book:     (wsId: string, tabId: string, bookId: number) => `book:${wsId}:${tabId}:${bookId}`,
  lastread: (bookId: number)                            => `lastread:${bookId}`,

  // prefix helpers for bulk delete
  wsPrefix:  (wsId: string) => `tabs:${wsId}:`,
  tabPrefix: (wsId: string, tabId: string) => `book:${wsId}:${tabId}:`,
} as const

// ── DB open ───────────────────────────────────────────────────────────────────

let db: IDBDatabase | null = null

function open(): Promise<IDBDatabase> {
  if (db) return Promise.resolve(db)
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(STORE))
        req.result.createObjectStore(STORE)
    }
    req.onsuccess = () => { db = req.result; resolve(db) }
    req.onerror  = () => reject(req.error)
  })
}

// ── Core get / set / delete ───────────────────────────────────────────────────

export async function idbGet<T>(key: string): Promise<T | null> {
  const store = (await open()).transaction(STORE).objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.get(key)
    req.onsuccess = () => resolve(req.result ?? null)
    req.onerror  = () => reject(req.error)
  })
}

export async function idbSet<T>(key: string, value: T): Promise<void> {
  const store = (await open()).transaction(STORE, 'readwrite').objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.put(value, key)
    req.onsuccess = () => resolve()
    req.onerror  = () => reject(req.error)
  })
}

export async function idbDelete(key: string): Promise<void> {
  const store = (await open()).transaction(STORE, 'readwrite').objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.delete(key)
    req.onsuccess = () => resolve()
    req.onerror  = () => reject(req.error)
  })
}

// ── Clear all (app reset) ─────────────────────────────────────────────────────

export async function idbClearAll(): Promise<void> {
  const store = (await open()).transaction(STORE, 'readwrite').objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.clear()
    req.onsuccess = () => resolve()
    req.onerror  = () => reject(req.error)
  })
}

// ── Prefix delete (for tab/workspace cleanup) ─────────────────────────────────

export async function idbDeleteByPrefix(prefix: string): Promise<void> {
  const idb = await open()
  return new Promise((resolve, reject) => {
    const req = idb.transaction(STORE, 'readwrite').objectStore(STORE).openCursor()
    req.onsuccess = () => {
      const cursor = req.result
      if (!cursor) { resolve(); return }
      if ((cursor.key as string).startsWith(prefix)) cursor.delete()
      cursor.continue()
    }
    req.onerror = () => reject(req.error)
  })
}

/**
 * Delete all data for a workspace: tabs list, all tab states, all book states.
 * Does NOT touch settings: or lastread: keys.
 */
export async function idbDeleteWorkspaceData(wsId: string): Promise<void> {
  await Promise.all([
    idbDeleteByPrefix(`tabs:${wsId}:`),
    idbDeleteByPrefix(`tab:${wsId}:`),
    idbDeleteByPrefix(`book:${wsId}:`),
  ])
}

// ── lastread LRU cap ──────────────────────────────────────────────────────────
// After writing a lastread entry, if total count exceeds LASTREAD_MAX, delete
// the oldest entries (by key order, which is insertion order for string keys).

export async function idbSetLastRead(bookId: number, value: LastReadState): Promise<void> {
  await idbSet(KEYS.lastread(bookId), value)

  const idb = await open()
  // Collect all lastread keys
  const keys = await new Promise<string[]>((resolve, reject) => {
    const keys: string[] = []
    const req = idb.transaction(STORE).objectStore(STORE).openKeyCursor()
    req.onsuccess = () => {
      const cursor = req.result
      if (!cursor) { resolve(keys); return }
      if ((cursor.key as string).startsWith('lastread:')) keys.push(cursor.key as string)
      cursor.continue()
    }
    req.onerror = () => reject(req.error)
  })

  if (keys.length <= LASTREAD_MAX) return

  // Delete oldest (first in key order) until under cap
  const toDelete = keys.slice(0, keys.length - LASTREAD_MAX)
  await Promise.all(toDelete.map(k => idbDelete(k)))
}
