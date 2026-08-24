/**
 * Frequently visited folders store.
 *
 * Tracks which folders the user opens files from, ranked by popularity rather
 * than recency. The home page shows the top few as tiles that reopen the file
 * dialog in that folder.
 *
 * Ranked by popularity rather than recency — see stores/popularityScore for the
 * scoring model shared with the recently-opened files.
 *
 * Only this store may access the `app-frequent-folders` IDB database.
 */
import { defineStore } from 'pinia'
import { dbGet, dbSet } from '@/utils/persistence'
import { folderDisplayName } from '@/utils/filePath'
import {
  capByPopularity,
  decayEntry,
  pinBudgetExhausted,
  scoreAfterVisit,
  sortByPopularity,
  VISIT_POINTS,
  type PopularityScored,
} from '@/stores/popularityScore'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface FrequentFolderEntry extends PopularityScored {
  /** Absolute folder path. Also the entry's identity. */
  path: string
  /** Display name — the folder's own last segment. */
  name: string
}

const FREQUENT_FOLDERS_DB = 'app-frequent-folders'
const LIST_KEY = 'list'

const FREQUENT_FOLDERS_MAX = 30

/**
 * Pins may not fill the whole list. Without this, pinning every slot would leave
 * no room for a newly visited folder to accumulate a score, so it could never
 * earn a tile however often it was opened.
 */
const MAX_PINNED = FREQUENT_FOLDERS_MAX - 4

/** This store's cap, on the shared popularity rules. */
function capList(
  list: FrequentFolderEntry[],
  now: number,
  protectedPath?: string,
): FrequentFolderEntry[] {
  return capByPopularity(list, now, {
    max: FREQUENT_FOLDERS_MAX,
    maxPinned: MAX_PINNED,
    keyOf: (e) => e.path,
    protectedKey: protectedPath,
  })
}

// ── In-memory cache ───────────────────────────────────────────────────────────
// null means not yet loaded from IDB. At most FREQUENT_FOLDERS_MAX tiny objects.

let _cache: FrequentFolderEntry[] | null = null
let _loadPromise: Promise<FrequentFolderEntry[]> | null = null

function ensureLoaded(): Promise<FrequentFolderEntry[]> {
  if (_cache !== null) return Promise.resolve(_cache)
  if (_loadPromise) return _loadPromise
  _loadPromise = dbGet<FrequentFolderEntry[]>(FREQUENT_FOLDERS_DB, LIST_KEY)
    .then((entries) => {
      _cache = entries ?? []
      _loadPromise = null
      return _cache
    })
    .catch(() => {
      // A failed read must not wedge the loader — an empty list is a valid state
      // and the next visit will write a fresh one.
      _cache = []
      _loadPromise = null
      return _cache
    })
  return _loadPromise
}

function save(list: FrequentFolderEntry[]): void {
  dbSet(FREQUENT_FOLDERS_DB, LIST_KEY, list).catch(() => {
    // Losing a visit count is not worth surfacing to the user.
  })
}

/**
 * Forgets the in-memory list. The database itself is driver-owned, so a reset
 * drops it with `dropDatabase`; this clears the cache that would otherwise be
 * written straight back out on the next visit.
 */
export function resetFrequentFoldersCache(): void {
  _cache = null
  _loadPromise = null
}

// ── Store ─────────────────────────────────────────────────────────────────────

export const useFrequentFoldersStore = defineStore('frequentFolders', () => {
  /** The retained folders, most popular first. One IDB read on first call. */
  function getList(): Promise<FrequentFolderEntry[]> {
    return ensureLoaded().then((list) => sortByPopularity(list, Date.now()))
  }

  /**
   * Record that a file was opened from `folderPath`, adding a visit's points.
   *
   * Fire-and-forget: the in-memory cache updates synchronously so an immediate
   * re-read is correct, and the IDB write is not awaited.
   */
  function trackFolderVisit(folderPath: string): void {
    if (!folderPath) return
    const now = Date.now()

    const bump = (list: FrequentFolderEntry[]): FrequentFolderEntry[] => {
      const existing = list.find((e) => e.path === folderPath)
      const entry: FrequentFolderEntry = existing
        ? {
            ...decayEntry(existing, now),
            // Recomputed rather than carried forward: the stored name is only as
            // fresh as the visit that created it.
            name: folderDisplayName(folderPath),
            score: scoreAfterVisit(existing, now),
            lastVisitedAt: now,
          }
        : {
            path: folderPath,
            name: folderDisplayName(folderPath),
            score: VISIT_POINTS,
            scoredAt: now,
            lastVisitedAt: now,
          }
      return capList([entry, ...list.filter((e) => e.path !== folderPath)], now, folderPath)
    }

    if (_cache !== null) _cache = bump(_cache)

    ensureLoaded().then((list) => {
      const updated = bump(list)
      _cache = updated
      save(updated)
    })
  }

  /**
   * Toggles the pinned state of a folder and persists. Returns the updated list
   * (most popular first), or an empty list if the cache has not loaded yet.
   *
   * Pinning is refused once MAX_PINNED is reached — silently accepting it would
   * let the cap drop the pin again on the next write, which reads as the pin not
   * having stuck.
   */
  function togglePin(path: string): FrequentFolderEntry[] {
    if (_cache === null) return []
    const now = Date.now()
    const target = _cache.find((e) => e.path === path)
    if (target && !target.pinned && pinBudgetExhausted(_cache, MAX_PINNED)) {
      return sortByPopularity(_cache, now)
    }
    _cache = capList(
      _cache.map((e) => (e.path === path ? { ...e, pinned: !e.pinned } : e)),
      now,
      // A pin toggle must not evict the folder being toggled.
      path,
    )
    save(_cache)
    return sortByPopularity(_cache, now)
  }

  /**
   * Removes a folder and persists. Returns the updated list (most popular
   * first), or an empty list if the cache has not loaded yet.
   *
   * The folder can reappear if the user opens files from it again — this clears
   * the accumulated score, it does not blacklist the path.
   */
  function removeEntry(path: string): FrequentFolderEntry[] {
    if (_cache === null) return []
    const now = Date.now()
    _cache = capList(
      _cache.filter((e) => e.path !== path),
      now,
    )
    save(_cache)
    return sortByPopularity(_cache, now)
  }

  return { getList, trackFolderVisit, togglePin, removeEntry }
})
