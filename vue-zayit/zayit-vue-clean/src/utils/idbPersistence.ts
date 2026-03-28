/**
 * IndexedDB persistence — separate databases per concern.
 *
 * Databases:
 *   app-settings  — all scalar settings (one key per setting)
 *   app-tabs      — tabs list, tab states, book states (workspace-scoped)
 *   app-lastread  — per-book last-read positions (LRU-capped at 1000)
 *
 * Reset: delete all three databases.
 */

const STORE = 'data'
const LASTREAD_MAX = 1000

// ── Shared types ──────────────────────────────────────────────────────────────

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

export interface Workspace {
  id: string
  name: string
  createdAt: number
}

export interface WorkspaceList {
  workspaces: Workspace[]
  activeId: string
}

// ── DB handles ────────────────────────────────────────────────────────────────

const handles: Record<string, IDBDatabase | null> = {
  'app-settings': null,
  'app-tabs': null,
  'app-lastread': null,
}

function openDb(name: string): Promise<IDBDatabase> {
  if (handles[name]) return Promise.resolve(handles[name]!)
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(name, 1)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(STORE))
        req.result.createObjectStore(STORE)
    }
    req.onsuccess = () => { handles[name] = req.result; resolve(req.result) }
    req.onerror  = () => reject(req.error)
  })
}

// ── Core get / set / delete ───────────────────────────────────────────────────

async function dbGet<T>(dbName: string, key: string): Promise<T | null> {
  const store = (await openDb(dbName)).transaction(STORE).objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.get(key)
    req.onsuccess = () => resolve(req.result ?? null)
    req.onerror  = () => reject(req.error)
  })
}

async function dbSet<T>(dbName: string, key: string, value: T): Promise<void> {
  const store = (await openDb(dbName)).transaction(STORE, 'readwrite').objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.put(value, key)
    req.onsuccess = () => resolve()
    req.onerror  = () => reject(req.error)
  })
}

async function dbDelete(dbName: string, key: string): Promise<void> {
  const store = (await openDb(dbName)).transaction(STORE, 'readwrite').objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.delete(key)
    req.onsuccess = () => resolve()
    req.onerror  = () => reject(req.error)
  })
}

async function dbDeleteByPrefix(dbName: string, prefix: string): Promise<void> {
  const idb = await openDb(dbName)
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

function dropDb(name: string): Promise<void> {
  handles[name] = null
  return new Promise((resolve, reject) => {
    const req = indexedDB.deleteDatabase(name)
    req.onsuccess = () => resolve()
    req.onerror  = () => reject(req.error)
  })
}

// ── Settings DB ───────────────────────────────────────────────────────────────

export const KEYS = {
  SETTINGS_WORKSPACES:              'workspaces',
  SETTINGS_BOOKS_VIEW:              'books.view',
  SETTINGS_TOOLBAR:                 'bookView.toolbarVisible',
  SETTINGS_SEARCH_BAR_POS:          'bookView.searchBarPos',
  SETTINGS_ZOOM:                    'bookView.zoom',
  SETTINGS_CENSOR_DIVINE:           'censorDivineNames',
  SETTINGS_DIACRITICS:              'diacriticsState',
  SETTINGS_HEADER_FONT:             'headerFont',
  SETTINGS_TEXT_FONT:               'textFont',
  SETTINGS_FONT_SIZE:               'fontSize',
  SETTINGS_LINE_PADDING:            'linePadding',
  SETTINGS_COMMENTARY_HEADER_FONT:  'commentaryHeaderFont',
  SETTINGS_COMMENTARY_TEXT_FONT:    'commentaryTextFont',
  SETTINGS_COMMENTARY_FONT_SIZE:    'commentaryFontSize',
  SETTINGS_COMMENTARY_LINE_PADDING: 'commentaryLinePadding',
  SETTINGS_SEPARATE_COMMENTARY:     'useSeparateCommentarySettings',
  SETTINGS_APP_ZOOM:                'appZoom',
  SETTINGS_NEW_TAB_PAGE:            'newTabPage',
  SETTINGS_PDF_FILTERS:             'pdfPageFilters',
  SETTINGS_RESUME_LAST_READ:        'resumeLastRead',
  SETTINGS_THEME:                   'theme',
  SETTINGS_CUSTOM_THEMES:           'customThemes',

  // app-tabs keys
  tabsList: (wsId: string)                               => `tabs:${wsId}`,
  tab:      (wsId: string, tabId: string)                => `tab:${wsId}:${tabId}`,
  book:     (wsId: string, tabId: string, bookId: number) => `book:${wsId}:${tabId}:${bookId}`,
  tabPrefix: (wsId: string, tabId: string)               => `book:${wsId}:${tabId}:`,
  wsPrefix:  (wsId: string)                              => `tabs:${wsId}`,
} as const

export function idbGet<T>(key: string): Promise<T | null> {
  return dbGet<T>('app-settings', key)
}
export function idbSet<T>(key: string, value: T): Promise<void> {
  return dbSet('app-settings', key, value)
}
export function idbDelete(key: string): Promise<void> {
  return dbDelete('app-settings', key)
}

// ── Tabs DB ───────────────────────────────────────────────────────────────────

export function idbTabsGet<T>(key: string): Promise<T | null> {
  return dbGet<T>('app-tabs', key)
}
export function idbTabsSet<T>(key: string, value: T): Promise<void> {
  return dbSet('app-tabs', key, value)
}
export function idbTabsDelete(key: string): Promise<void> {
  return dbDelete('app-tabs', key)
}
export function idbTabsDeleteByPrefix(prefix: string): Promise<void> {
  return dbDeleteByPrefix('app-tabs', prefix)
}

export async function idbDeleteWorkspaceData(wsId: string): Promise<void> {
  await Promise.all([
    dbDeleteByPrefix('app-tabs', `tabs:${wsId}`),
    dbDeleteByPrefix('app-tabs', `tab:${wsId}:`),
    dbDeleteByPrefix('app-tabs', `book:${wsId}:`),
  ])
}

// ── LastRead DB ───────────────────────────────────────────────────────────────

export async function idbSetLastRead(bookId: number, value: LastReadState): Promise<void> {
  const key = `lastread:${bookId}`
  await dbSet('app-lastread', key, value)

  const idb = await openDb('app-lastread')
  const keys = await new Promise<string[]>((resolve, reject) => {
    const acc: string[] = []
    const req = idb.transaction(STORE).objectStore(STORE).openKeyCursor()
    req.onsuccess = () => {
      const cursor = req.result
      if (!cursor) { resolve(acc); return }
      acc.push(cursor.key as string)
      cursor.continue()
    }
    req.onerror = () => reject(req.error)
  })

  if (keys.length <= LASTREAD_MAX) return
  const toDelete = keys.slice(0, keys.length - LASTREAD_MAX)
  await Promise.all(toDelete.map(k => dbDelete('app-lastread', k)))
}

export function idbGetLastRead(bookId: number): Promise<LastReadState | null> {
  return dbGet<LastReadState>('app-lastread', `lastread:${bookId}`)
}

// ── Reset all ─────────────────────────────────────────────────────────────────

export async function idbClearAll(): Promise<void> {
  await Promise.all([
    dropDb('app-settings'),
    dropDb('app-tabs'),
    dropDb('app-lastread'),
  ])
}

export async function idbClearSettings(): Promise<void> {
  await dropDb('app-settings')
}
