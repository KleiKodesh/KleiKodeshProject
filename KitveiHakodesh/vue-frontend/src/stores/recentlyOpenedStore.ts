/**
 * Recently opened documents store.
 * Tracks the last 16 documents the user opened across /book-view, /pdf-view,
 * /html-view, and /txt-view. Opening an already-tracked document bumps it to
 * the front (LRU with bump). The list is loaded lazily on first access — no
 * boot-time IDB cost.
 *
 * Only this store may access the `app-recently-opened` IDB database.
 */
import { defineStore } from 'pinia'
import type { TabRoute } from '@/stores/tabStore'

// ── Types ─────────────────────────────────────────────────────────────────────

export type RecentlyOpenedRoute = '/book-view' | '/pdf-view' | '/html-view' | '/txt-view'

export interface RecentlyOpenedEntry {
  /** Stable unique key used for LRU bump matching. */
  key: string
  route: RecentlyOpenedRoute
  title: string
  /** Present for /book-view entries. */
  bookId?: number
  /** Present for /pdf-view, /html-view, /txt-view entries that are local files. */
  localFilePath?: string
  /** Present for HebrewBooks /pdf-view entries (identified by HB book id). */
  localFileHbBookId?: string
  /** Needed for HebrewBooks restore — the title used as cache filename. */
  localFileHbBookTitle?: string
  /** Present when localFilePath is empty (dev mode blob URL files — identified by filename). */
  localFileName?: string
  /** Unix timestamp of last access (ms). */
  lastAccessedAt: number
  /** Pinned entries sort ahead of the rest and are never evicted by the cap. */
  pinned?: boolean
}

const RECENTLY_OPENED_DB = 'app-recently-opened'
const RECENTLY_OPENED_STORE = 'data'
const RECENTLY_OPENED_MAX = 20

// ── IDB setup ─────────────────────────────────────────────────────────────────

let _db: IDBDatabase | null = null

function openDb(): Promise<IDBDatabase> {
  if (_db) return Promise.resolve(_db)
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(RECENTLY_OPENED_DB, 1)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(RECENTLY_OPENED_STORE)) {
        req.result.createObjectStore(RECENTLY_OPENED_STORE)
      }
    }
    req.onsuccess = () => {
      _db = req.result
      resolve(_db)
    }
    req.onerror = () => reject(req.error)
  })
}

const LIST_KEY = 'list'

async function idbLoadList(): Promise<RecentlyOpenedEntry[]> {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const req = db.transaction(RECENTLY_OPENED_STORE).objectStore(RECENTLY_OPENED_STORE).get(LIST_KEY)
    req.onsuccess = () => resolve((req.result as RecentlyOpenedEntry[] | undefined) ?? [])
    req.onerror = () => reject(req.error)
  })
}

async function idbSaveList(list: RecentlyOpenedEntry[]): Promise<void> {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const req = db
      .transaction(RECENTLY_OPENED_STORE, 'readwrite')
      .objectStore(RECENTLY_OPENED_STORE)
      .put(list, LIST_KEY)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })
}

export function dropRecentlyOpenedDb(): Promise<void> {
  _db?.close()
  _db = null
  return new Promise((resolve, reject) => {
    const req = indexedDB.deleteDatabase(RECENTLY_OPENED_DB)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
    req.onblocked = () => resolve()
  })
}

// ── Stable key derivation ─────────────────────────────────────────────────────

/**
 * Derives a stable unique key for an entry so that re-opening the same document
 * bumps the existing entry rather than creating a duplicate.
 */
function deriveKey(
  route: RecentlyOpenedRoute,
  bookId?: number,
  localFilePath?: string,
  localFileHbBookId?: string,
  localFileName?: string,
): string {
  if (route === '/book-view' && bookId !== undefined) return `book:${bookId}`
  if (localFileHbBookId) return `hb:${localFileHbBookId}`
  if (localFilePath) return `file:${localFilePath}`
  if (localFileName) return `filename:${localFileName}`
  // Fallback — should not normally be reached for well-formed entries
  return `${route}:${Date.now()}`
}

/** Strips the file extension from a title for file-route entries. */
function stripFileExtension(title: string): string {
  const dot = title.lastIndexOf('.')
  if (dot > 0) return title.slice(0, dot)
  return title
}

// ── Ordering & cap ──────────────────────────────────────────────────────────────

/** Returns a new list with pinned entries first, each group newest-first. */
function sortPinnedFirst(list: RecentlyOpenedEntry[]): RecentlyOpenedEntry[] {
  return [...list].sort((a, b) => {
    const pinDelta = (b.pinned ? 1 : 0) - (a.pinned ? 1 : 0)
    if (pinDelta !== 0) return pinDelta
    return b.lastAccessedAt - a.lastAccessedAt
  })
}

/**
 * Trims the list to the cap without ever evicting pinned entries. All pins are
 * kept (even if that exceeds the cap); the remaining slots go to the newest
 * unpinned entries.
 */
function capList(list: RecentlyOpenedEntry[]): RecentlyOpenedEntry[] {
  if (list.length <= RECENTLY_OPENED_MAX) return list
  const pinned = list.filter((e) => e.pinned)
  const room = Math.max(0, RECENTLY_OPENED_MAX - pinned.length)
  const keptUnpinned = list
    .filter((e) => !e.pinned)
    .sort((a, b) => b.lastAccessedAt - a.lastAccessedAt)
    .slice(0, room)
  return [...pinned, ...keptUnpinned]
}

// ── In-memory cache ───────────────────────────────────────────────────────────
// null means not yet loaded from IDB. Max 16 tiny objects — safe to keep in memory.

let _cache: RecentlyOpenedEntry[] | null = null
let _loadPromise: Promise<RecentlyOpenedEntry[]> | null = null

function ensureLoaded(): Promise<RecentlyOpenedEntry[]> {
  if (_cache !== null) return Promise.resolve(_cache)
  if (_loadPromise) return _loadPromise
  _loadPromise = idbLoadList().then((entries) => {
    _cache = entries
    _loadPromise = null
    return entries
  })
  return _loadPromise
}

// ── Store ─────────────────────────────────────────────────────────────────────

export const TRACKABLE_ROUTES = new Set<TabRoute>([
  '/book-view',
  '/pdf-view',
  '/html-view',
  '/txt-view',
])

export const useRecentlyOpenedStore = defineStore('recentlyOpened', () => {
  /**
   * Returns the list, pinned entries first then newest-first.
   * Synchronous from memory after first call; one IDB read on first call.
   */
  function getList(): Promise<RecentlyOpenedEntry[]> {
    return ensureLoaded().then((list) => sortPinnedFirst(list))
  }

  /**
   * Record that a document was opened. Bumps an existing entry to the front if
   * the same document was already tracked; otherwise prepends a new entry and
   * evicts the oldest when over the cap.
   *
   * Fire-and-forget — the in-memory cache is updated synchronously; IDB write
   * is async and not awaited by callers.
   */
  function trackNavigation(
    route: RecentlyOpenedRoute,
    title: string,
    bookId?: number,
    localFilePath?: string,
    localFileHbBookId?: string,
    localFileHbBookTitle?: string,
    localFileName?: string,
  ): void {
    const key = deriveKey(route, bookId, localFilePath, localFileHbBookId, localFileName)
    const displayTitle = route === '/book-view' ? title : stripFileExtension(title)

    const baseEntry: RecentlyOpenedEntry = {
      key,
      route,
      title: displayTitle,
      lastAccessedAt: Date.now(),
      ...(bookId !== undefined ? { bookId } : {}),
      ...(localFilePath ? { localFilePath } : {}),
      ...(localFileHbBookId ? { localFileHbBookId } : {}),
      ...(localFileHbBookTitle ? { localFileHbBookTitle } : {}),
      ...(localFileName ? { localFileName } : {}),
    }

    // Re-opening a pinned document keeps it pinned (the fresh entry carries no flag).
    const bump = (list: RecentlyOpenedEntry[]): RecentlyOpenedEntry[] => {
      const entry = list.find((e) => e.key === key)?.pinned ? { ...baseEntry, pinned: true } : baseEntry
      return capList([entry, ...list.filter((e) => e.key !== key)])
    }

    if (_cache !== null) {
      _cache = bump(_cache)
    }

    // Fire-and-forget — the cache is already correct for any immediate re-reads
    ensureLoaded().then((list) => {
      const updated = bump(list)
      _cache = updated
      idbSaveList(updated)
    })
  }

  /**
   * Toggles the pinned state of an entry and persists. Returns the updated list
   * (pinned first), or an empty list if the cache has not been loaded yet.
   */
  function togglePin(key: string): RecentlyOpenedEntry[] {
    if (_cache === null) return []
    _cache = capList(_cache.map((e) => (e.key === key ? { ...e, pinned: !e.pinned } : e)))
    idbSaveList(_cache)
    return sortPinnedFirst(_cache)
  }

  /**
   * Removes an entry from the list and persists. Returns the updated list
   * (pinned first), or an empty list if the cache has not been loaded yet.
   */
  function removeEntry(key: string): RecentlyOpenedEntry[] {
    if (_cache === null) return []
    _cache = _cache.filter((e) => e.key !== key)
    idbSaveList(_cache)
    return sortPinnedFirst(_cache)
  }

  return { getList, trackNavigation, togglePin, removeEntry }
})
