/**
 * Opened documents store.
 * Tracks the documents the user opened across /book-view, /pdf-view,
 * /html-view, and /txt-view. Opening an already-tracked document adds a visit's
 * points rather than simply moving it to the front — ranking is by popularity,
 * not recency; see stores/popularityScore for the model, shared with the
 * frequently-visited folders. The list is loaded lazily on first access — no
 * boot-time IDB cost.
 *
 * Only this store may access the `app-recently-opened` IDB database.
 */
import { defineStore } from 'pinia'
import type { TabRoute } from '@/stores/tabStore'
import {
  capByPopularity,
  pinBudgetExhausted,
  scoreAfterVisit,
  sortByPopularity,
  VISIT_POINTS,
  type PopularityScored,
} from '@/stores/popularityScore'

// ── Types ─────────────────────────────────────────────────────────────────────

export type RecentlyOpenedRoute = '/book-view' | '/pdf-view' | '/html-view' | '/txt-view'

export interface RecentlyOpenedEntry extends PopularityScored {
  /** Stable unique key; also what a visit is matched on. */
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
  /** Display name; also the only identifier when localFilePath is empty (older entries saved
   *  before local files carried a real path — those can't be re-served, only re-opened). */
  localFileName?: string
  /**
   * Unix timestamp of last access (ms). Kept under its original name because it
   * is already persisted; `lastVisitedAt` mirrors it for the shared scoring.
   */
  lastAccessedAt: number
  /** True when the /html-view entry is an Otzaria addin (manifest.json detected next to the HTML file). */
  isOtzariaAddin?: boolean
}

const RECENTLY_OPENED_DB = 'app-recently-opened'
const RECENTLY_OPENED_STORE = 'data'
const RECENTLY_OPENED_MAX = 50

/**
 * Pins may not fill the whole list. Without this, pinning every slot would leave
 * no room for a newly opened document to accumulate a score, so it could never
 * earn a tile however often it was opened.
 */
const MAX_PINNED = RECENTLY_OPENED_MAX - 4

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

/**
 * Brings a stored entry up to the current shape.
 *
 * Entries written before ranking moved to popularity carry only `lastAccessedAt`.
 * They are seeded with a single visit's points dated to that access, so an old
 * list keeps its familiar order on first read (everything scores the same, and
 * the recency tiebreak decides) and then diverges as the user opens things.
 */
function migrateEntry(entry: RecentlyOpenedEntry): RecentlyOpenedEntry {
  // Number.isFinite, not typeof: NaN is a number, and an entry carrying a NaN
  // score would early-return here forever — ranking arbitrarily (every NaN
  // comparison sorts as equal) and then vanishing the moment the list fills.
  if (
    Number.isFinite(entry.score) &&
    Number.isFinite(entry.scoredAt) &&
    Number.isFinite(entry.lastVisitedAt)
  ) {
    return entry
  }
  // A missing or unusable timestamp is dated to now rather than to the epoch:
  // treating an undatable entry as ancient would decay it to nothing and delete
  // it, which is a harsh reading of "we don't know when this was opened".
  const accessed =
    Number.isFinite(entry.lastAccessedAt) && entry.lastAccessedAt > 0
      ? entry.lastAccessedAt
      : Date.now()
  // lastAccessedAt is rewritten too, so a repaired entry can never early-return
  // above while still carrying the unusable value that sent it here.
  return {
    ...entry,
    lastAccessedAt: accessed,
    score: VISIT_POINTS,
    scoredAt: accessed,
    lastVisitedAt: accessed,
  }
}

/** Pinned entries first, then most popular — see stores/popularityScore. */
function sortPinnedFirst(list: RecentlyOpenedEntry[]): RecentlyOpenedEntry[] {
  return sortByPopularity(list, Date.now())
}

/**
 * This store's cap, on the shared popularity rules.
 *
 * Unlike the folders, a document that has gone quiet is kept until the cap
 * actually needs its slot: this list is one the user curates and pins, and a
 * document dropped from it has no way back.
 */
function capList(list: RecentlyOpenedEntry[], protectedKey?: string): RecentlyOpenedEntry[] {
  return capByPopularity(list, Date.now(), {
    max: RECENTLY_OPENED_MAX,
    maxPinned: MAX_PINNED,
    keyOf: (e) => e.key,
    protectedKey,
    floorBelowCap: false,
  })
}

// ── In-memory cache ───────────────────────────────────────────────────────────
// null means not yet loaded from IDB. Max 16 tiny objects — safe to keep in memory.

let _cache: RecentlyOpenedEntry[] | null = null
let _loadPromise: Promise<RecentlyOpenedEntry[]> | null = null

function ensureLoaded(): Promise<RecentlyOpenedEntry[]> {
  if (_cache !== null) return Promise.resolve(_cache)
  if (_loadPromise) return _loadPromise
  _loadPromise = idbLoadList().then((entries) => {
    _cache = entries.map(migrateEntry)
    _loadPromise = null
    return _cache
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
    isOtzariaAddin?: boolean,
  ): void {
    const key = deriveKey(route, bookId, localFilePath, localFileHbBookId, localFileName)
    const displayTitle = route === '/book-view' ? title : stripFileExtension(title)

    const now = Date.now()
    const baseEntry: RecentlyOpenedEntry = {
      key,
      route,
      title: displayTitle,
      lastAccessedAt: now,
      // Overwritten below for an entry that already exists; these are the values
      // a document opened for the first time starts from.
      score: VISIT_POINTS,
      scoredAt: now,
      lastVisitedAt: now,
      ...(bookId !== undefined ? { bookId } : {}),
      ...(localFilePath ? { localFilePath } : {}),
      ...(localFileHbBookId ? { localFileHbBookId } : {}),
      ...(localFileHbBookTitle ? { localFileHbBookTitle } : {}),
      ...(localFileName ? { localFileName } : {}),
      ...(isOtzariaAddin ? { isOtzariaAddin: true } : {}),
    }

    // The fresh entry carries the current metadata (title, path, flags) but none
    // of the history, so an existing entry's score and pin are carried across —
    // otherwise every re-open would reset the document to a single visit.
    const bump = (list: RecentlyOpenedEntry[]): RecentlyOpenedEntry[] => {
      const existing = list.find((e) => e.key === key)
      const entry: RecentlyOpenedEntry = existing
        ? {
            ...baseEntry,
            ...(existing.pinned ? { pinned: true } : {}),
            score: scoreAfterVisit(migrateEntry(existing), now),
            scoredAt: now,
            lastVisitedAt: now,
          }
        : baseEntry
      return capList([entry, ...list.filter((e) => e.key !== key)], key)
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
    const target = _cache.find((e) => e.key === key)
    // Refused rather than silently dropped by the next write — see pinBudgetExhausted.
    if (target && !target.pinned && pinBudgetExhausted(_cache, MAX_PINNED)) {
      return sortPinnedFirst(_cache)
    }
    _cache = capList(
      _cache.map((e) => (e.key === key ? { ...e, pinned: !e.pinned } : e)),
      key,
    )
    idbSaveList(_cache)
    return sortPinnedFirst(_cache)
  }

  /**
   * Removes an entry from the list and persists. Returns the updated list
   * (pinned first), or an empty list if the cache has not been loaded yet.
   */
  function removeEntry(key: string): RecentlyOpenedEntry[] {
    if (_cache === null) return []
    // Not re-capped: removing one tile must remove exactly that tile, and a
    // shrinking list can never breach the cap anyway.
    _cache = _cache.filter((e) => e.key !== key)
    idbSaveList(_cache)
    return sortPinnedFirst(_cache)
  }

  return { getList, trackNavigation, togglePin, removeEntry }
})
