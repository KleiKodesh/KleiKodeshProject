/**
 * Persistence layer — localStorage for scalar settings, IndexedDB for large/structured data.
 *
 * localStorage (synchronous, zero async cost):
 *   All scalar app settings, theme, workspaces, UI state flags
 *
 * IndexedDB databases:
 *   app-tabs               — tabs list, tab states, book states (workspace-scoped)
 *   app-lastread           — per-book last-read positions (LRU-capped at 1000)
 *   app-hb-history         — HebrewBooks download history
 *   app-search-cache       — full-text search result cache (LRU-capped at 100 queries)
 *   app-catalog-toc-cache  — Book catalog TOC search result cache (LRU-capped at 25 queries)
 *
 * Reset: clear all localStorage keys + delete all IDB databases.
 */

const STORE = 'data'
const LASTREAD_MAX = 1000

// ── localStorage helpers (synchronous) ───────────────────────────────────────

const LS_PREFIX = 'zayit.'

export function lsGet<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(LS_PREFIX + key)
    if (raw === null) return null
    return JSON.parse(raw) as T
  } catch { return null }
}

export function lsSet<T>(key: string, value: T): void {
  try { localStorage.setItem(LS_PREFIX + key, JSON.stringify(value)) } catch {}
}

export function lsDelete(key: string): void {
  try { localStorage.removeItem(LS_PREFIX + key) } catch {}
}

/** Remove all zayit.* keys from localStorage (used during full app reset). */
export function lsClearAll(): void {
  try {
    const toRemove: string[] = []
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i)
      if (k?.startsWith(LS_PREFIX) || k === RESET_LS_KEY) toRemove.push(k)
    }
    toRemove.forEach((k) => localStorage.removeItem(k))
  } catch {}
}

/**
 * Remove only display/reading settings from localStorage.
 * Preserves structural and non-settings keys: tabs lists (tabs:*), workspaces,
 * and the one-time onboarding flag (setupDone) so the wizard never re-appears.
 * Used by settings reset so open tabs and app structure survive.
 */
export function lsClearSettingsOnly(): void {
  const PRESERVE = new Set(['workspaces', 'setupDone'])
  try {
    const toRemove: string[] = []
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i)
      if (!k?.startsWith(LS_PREFIX)) continue
      const unprefixed = k.slice(LS_PREFIX.length)
      if (unprefixed.startsWith('tabs:') || PRESERVE.has(unprefixed)) continue
      toRemove.push(k)
    }
    toRemove.forEach((k) => localStorage.removeItem(k))
  } catch {}
}

// ── Shared types ──────────────────────────────────────────────────────────────

export interface TabState {
  searchScrollIndex?: number
  searchScrollOffset?: number
  searchCheckedBookIds?: number[] // absent/null means "all checked" (default)
  searchAtFilters?: string[]      // @ tokens from the search input, e.g. ["בראשית", "בבלי"]
  searchZoom?: number             // per-tab zoom level for the search results page (50–200)
}

export interface BookState {
  scrollIndex: number
  scrollOffset: number
  selectedLineId?: number | null
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
  commentaryFilterState?: import('@/features/book-view/bookViewTypes').CommentaryTreeState
  zoom?: number
  bottomVisible?: boolean
  autoSelectTopLine?: boolean
}

export interface LastReadState {
  scrollIndex: number
  scrollOffset: number
  selectedLineId?: number | null
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
  commentaryFilterState?: import('@/features/book-view/bookViewTypes').CommentaryTreeState
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
  'app-tabs': null,
  'app-lastread': null,
  'app-search-cache': null,
  'app-dict-cache': null,
  'app-catalog-toc-cache': null,
}

function openDb(name: string): Promise<IDBDatabase> {
  if (handles[name]) return Promise.resolve(handles[name]!)
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(name, 1)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(STORE)) req.result.createObjectStore(STORE)
    }
    req.onsuccess = () => {
      handles[name] = req.result
      resolve(req.result)
    }
    req.onerror = () => reject(req.error)
  })
}

// ── Core get / set / delete ───────────────────────────────────────────────────

async function dbGet<T>(dbName: string, key: string): Promise<T | null> {
  const store = (await openDb(dbName)).transaction(STORE).objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.get(key)
    req.onsuccess = () => resolve(req.result ?? null)
    req.onerror = () => reject(req.error)
  })
}

async function dbSet<T>(dbName: string, key: string, value: T): Promise<void> {
  const store = (await openDb(dbName)).transaction(STORE, 'readwrite').objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.put(value, key)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })
}

async function dbDelete(dbName: string, key: string): Promise<void> {
  const store = (await openDb(dbName)).transaction(STORE, 'readwrite').objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.delete(key)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })
}

async function dbDeleteByPrefix(dbName: string, prefix: string): Promise<void> {
  const idb = await openDb(dbName)
  return new Promise((resolve, reject) => {
    const req = idb.transaction(STORE, 'readwrite').objectStore(STORE).openCursor()
    req.onsuccess = () => {
      const cursor = req.result
      if (!cursor) {
        resolve()
        return
      }
      if ((cursor.key as string).startsWith(prefix)) cursor.delete()
      cursor.continue()
    }
    req.onerror = () => reject(req.error)
  })
}

function dropDb(name: string): Promise<void> {
  handles[name]?.close()
  handles[name] = null
  return new Promise((resolve, reject) => {
    const req = indexedDB.deleteDatabase(name)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
    req.onblocked = () => resolve() // blocked means another tab holds it open; reload will finish the delete
  })
}

export const KEYS = {
  // localStorage keys (used with lsGet/lsSet)
  SETTINGS_WORKSPACES: 'workspaces',
  SETTINGS_BOOKS_VIEW: 'books.view',
  SETTINGS_TOOLBAR: 'bookView.toolbarVisible',
  SETTINGS_TOOLBAR_POSITION: 'bookView.toolbarPosition',
  SETTINGS_SEARCH_BAR_POS: 'bookView.searchBarPos',
  SETTINGS_AUTO_SELECT_TOP_LINE: 'bookView.autoSelectTopLine',
  SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY: 'defaultAutoSyncCommentary',
  SETTINGS_CENSOR_DIVINE: 'censorDivineNames',
  SETTINGS_DIACRITICS: 'diacriticsState',
  SETTINGS_HEADER_FONT: 'headerFont',
  SETTINGS_TEXT_FONT: 'textFont',
  SETTINGS_FONT_SIZE: 'fontSize',
  SETTINGS_LINE_PADDING: 'linePadding',
  SETTINGS_COMMENTARY_HEADER_FONT: 'commentaryHeaderFont',
  SETTINGS_COMMENTARY_TEXT_FONT: 'commentaryTextFont',
  SETTINGS_COMMENTARY_FONT_SIZE: 'commentaryFontSize',
  SETTINGS_COMMENTARY_LINE_PADDING: 'commentaryLinePadding',
  SETTINGS_SEPARATE_COMMENTARY: 'useSeparateCommentarySettings',
  SETTINGS_APP_ZOOM: 'appZoom',
  SETTINGS_NEW_TAB_PAGE: 'newTabPage',
  SETTINGS_PDF_FILTERS: 'pdfPageFilters',
  SETTINGS_RESUME_LAST_READ: 'resumeLastRead',
  SETTINGS_THEME: 'theme',
  SETTINGS_SETUP_DONE: 'setupDone',
  SETTINGS_ZMANIM_CITY: 'zmanim.city',
  SETTINGS_CALENDAR_VIEW: 'calendar.viewMode',
  SETTINGS_MIDOT_DISCLAIMER: 'midot.disclaimerAccepted',
  SETTINGS_DICTIONARY_ZOOM: 'dictionaryZoom',
  SETTINGS_SEARCH_CONTEXT_MARGIN: 'search.contextMargin',
  // tab list is also localStorage (small JSON, needed synchronously at boot)
  tabsList: (wsId: string) => `tabs:${wsId}`,

  // app-tabs IDB keys (per-tab and per-book state — can accumulate, stays in IDB)
  tab: (wsId: string, tabId: string) => `tab:${wsId}:${tabId}`,
  book: (wsId: string, tabId: string, bookId: number) => `book:${wsId}:${tabId}:${bookId}`,
  tabPrefix: (wsId: string, tabId: string) => `book:${wsId}:${tabId}:`,
} as const

// ── Search cache DB ───────────────────────────────────────────────────────────

export function idbGet<T>(key: string): Promise<T | null> {
  return dbGet<T>('app-search-cache', key)
}
export function idbSet<T>(key: string, value: T): Promise<void> {
  return dbSet('app-search-cache', key, value)
}
export function idbDelete(key: string): Promise<void> {
  return dbDelete('app-search-cache', key)
}

// ── Dictionary cache DB ──────────────────────────────────────────────────────

export function idbDictionaryCacheGet<T>(key: string): Promise<T | null> {
  return dbGet<T>('app-dict-cache', key)
}
export function idbDictionaryCacheSet<T>(key: string, value: T): Promise<void> {
  return dbSet('app-dict-cache', key, value)
}
export function idbDictionaryCacheDelete(key: string): Promise<void> {
  return dbDelete('app-dict-cache', key)
}

// ── Catalog TOC search cache DB ───────────────────────────────────────────────

export function idbCatalogTocCacheGet<T>(key: string): Promise<T | null> {
  return dbGet<T>('app-catalog-toc-cache', key)
}
export function idbCatalogTocCacheSet<T>(key: string, value: T): Promise<void> {
  return dbSet('app-catalog-toc-cache', key, value)
}
export function idbCatalogTocCacheDelete(key: string): Promise<void> {
  return dbDelete('app-catalog-toc-cache', key)
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

export function idbDeleteWorkspaceData(wsId: string): Promise<void> {
  // Tab list is in localStorage — remove it synchronously
  lsDelete(KEYS.tabsList(wsId))
  // Tab/book states are in IDB
  return Promise.all([
    dbDeleteByPrefix('app-tabs', `tab:${wsId}:`),
    dbDeleteByPrefix('app-tabs', `book:${wsId}:`),
  ]).then(() => {})
}

// ── LastRead DB ───────────────────────────────────────────────────────────────

// In-memory count of lastread entries — avoids a full DB key scan on every scroll save.
// Initialised to -1 (unknown); first write counts the real value via a cursor.
let _lastReadCount = -1

export async function idbSetLastRead(bookId: number, value: LastReadState): Promise<void> {
  const key = `lastread:${bookId}`
  const idb = await openDb('app-lastread')

  // Check if this key already exists — if so, count stays the same
  const existing = await new Promise<boolean>((resolve, reject) => {
    const req = idb.transaction(STORE).objectStore(STORE).getKey(key)
    req.onsuccess = () => resolve(req.result !== undefined)
    req.onerror = () => reject(req.error)
  })

  await dbSet('app-lastread', key, value)

  if (!existing) {
    if (_lastReadCount === -1) {
      // First write after boot — count the real number of entries once
      _lastReadCount = await new Promise<number>((resolve, reject) => {
        const req = idb.transaction(STORE).objectStore(STORE).count()
        req.onsuccess = () => resolve(req.result)
        req.onerror = () => reject(req.error)
      })
    } else {
      _lastReadCount++
    }
  }

  if (_lastReadCount <= LASTREAD_MAX) return

  // Over the cap — evict the oldest entries via a cursor (only runs when cap is exceeded)
  const toEvict = _lastReadCount - LASTREAD_MAX
  const keysToDelete = await new Promise<string[]>((resolve, reject) => {
    const acc: string[] = []
    const req = idb.transaction(STORE).objectStore(STORE).openKeyCursor()
    req.onsuccess = () => {
      const cursor = req.result
      if (!cursor || acc.length >= toEvict) {
        resolve(acc)
        return
      }
      acc.push(cursor.key as string)
      cursor.continue()
    }
    req.onerror = () => reject(req.error)
  })
  await Promise.all(keysToDelete.map((k) => dbDelete('app-lastread', k)))
  _lastReadCount -= keysToDelete.length
}

export function idbGetLastRead(bookId: number): Promise<LastReadState | null> {
  return dbGet<LastReadState>('app-lastread', `lastread:${bookId}`)
}

const RESET_LS_KEY = '__pendingReset'

export async function idbClearAll(): Promise<void> {
  // Import lazily to avoid circular dependency — dropHbHistoryDb is a plain function
  const { dropHbHistoryDb } = await import('@/stores/hebrewBooksHistoryStore')
  lsClearAll()
  await Promise.all([
    dropDb('app-tabs'),
    dropDb('app-lastread'),
    dropHbHistoryDb(),
    dropDb('app-search-cache'),
    dropDb('app-dict-cache'),
    dropDb('app-catalog-toc-cache'),
  ])
}

/** Schedule a full reset on next boot — synchronous localStorage write, zero IDB cost. */
export function idbScheduleReset(): void {
  try { localStorage.setItem(RESET_LS_KEY, '1') } catch {}
}

/**
 * Call once at boot. Synchronous localStorage check — zero cost on normal boots.
 * If the flag is set, wipes all IDB databases and reloads.
 */
export async function idbCheckAndExecReset(): Promise<void> {
  let flagSet = false
  try { flagSet = localStorage.getItem(RESET_LS_KEY) === '1' } catch {}
  if (!flagSet) return
  try { localStorage.removeItem(RESET_LS_KEY) } catch {}
  await idbClearAll()
  window.location.reload()
}
