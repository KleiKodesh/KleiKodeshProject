/**
 * Frequently visited folders store.
 *
 * Tracks which folders the user opens files from, ranked by popularity rather
 * than recency. The home page shows the top few as tiles that reopen the file
 * dialog in that folder.
 *
 * Why not plain LRU: a folder opened once an hour ago would outrank the folder
 * the user works in every day, and a single detour through some other directory
 * would evict a daily habit. Scoring is instead time-decayed frequency (LFU with
 * aging): every visit adds a point, and accumulated points lose half their value
 * every HALF_LIFE_MS. Frequency is what ranks folders; age is what lets a folder
 * the user has stopped visiting fall away on its own.
 *
 * The decay is never applied by a sweep. Each entry carries the timestamp its
 * score was last computed at, and the score is decayed forward on read or on the
 * next visit — so an app left closed for a month costs nothing and still comes
 * back with correctly aged scores.
 *
 * Only this store may access the `app-frequent-folders` IDB database.
 */
import { defineStore } from 'pinia'
import { dbGet, dbSet } from '@/utils/persistence'
import { folderDisplayName } from '@/utils/filePath'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface FrequentFolderEntry {
  /** Absolute folder path. Also the entry's identity. */
  path: string
  /** Display name — the folder's own last segment. */
  name: string
  /**
   * Decayed visit points as of `scoredAt`. Not comparable across entries until
   * both are decayed to a common instant — always rank via `decayedScore`.
   */
  score: number
  /** When `score` was last brought up to date (ms). */
  scoredAt: number
  /** Unix timestamp of the most recent visit (ms), for tie-breaking. */
  lastVisitedAt: number
  /** Pinned entries sort ahead of the rest and are never evicted by the cap. */
  pinned?: boolean
}

const FREQUENT_FOLDERS_DB = 'app-frequent-folders'
const LIST_KEY = 'list'

/** Points added per visit, before any decay. */
const VISIT_POINTS = 1

/** Accumulated points lose half their value over this span. */
const HALF_LIFE_MS = 14 * 24 * 60 * 60 * 1000

/**
 * How many folders are retained. Larger than the four tiles shown so a folder
 * that drops out of view for a while can climb back without starting from zero.
 */
const FREQUENT_FOLDERS_MAX = 30

/**
 * Pins may not fill the whole list. Without this, pinning every slot would leave
 * no room for a newly visited folder to accumulate a score, so it could never
 * earn a tile however often it was opened.
 */
const MAX_PINNED = FREQUENT_FOLDERS_MAX - 4

/**
 * Entries below this decayed score are dropped as noise — a folder visited once
 * and never again decays past it in roughly six half-lives. Without a floor the
 * list fills with one-off detours that can never be evicted by the cap because
 * they keep being re-added.
 */
const MIN_SCORE = 0.02

// ── Scoring ───────────────────────────────────────────────────────────────────

/** The entry's score decayed forward to `now`. */
function decayedScore(entry: FrequentFolderEntry, now: number): number {
  const elapsed = now - entry.scoredAt
  // Clock skew (or a system clock moved back) must not inflate a score.
  if (elapsed <= 0) return entry.score
  return entry.score * Math.pow(0.5, elapsed / HALF_LIFE_MS)
}

/** Rewrites an entry with its score decayed to `now`. */
function decayEntry(entry: FrequentFolderEntry, now: number): FrequentFolderEntry {
  return { ...entry, score: decayedScore(entry, now), scoredAt: now }
}

/**
 * Ranks by decayed score, most popular first. Pins come first regardless of
 * score — a pin means the user asked for the tile, not that it earned the slot.
 * Ties fall back to recency so equal-score folders order predictably.
 */
function sortByPopularity(list: FrequentFolderEntry[], now: number): FrequentFolderEntry[] {
  return [...list].sort((a, b) => {
    const pinDelta = (b.pinned ? 1 : 0) - (a.pinned ? 1 : 0)
    if (pinDelta !== 0) return pinDelta
    const scoreDelta = decayedScore(b, now) - decayedScore(a, now)
    if (scoreDelta !== 0) return scoreDelta
    return b.lastVisitedAt - a.lastVisitedAt
  })
}

/**
 * Drops faded and surplus entries. Pinned entries survive the score floor, and
 * the remaining slots go to the highest-scoring unpinned entries.
 *
 * `protectedPath` is the folder just visited. It keeps its slot regardless of
 * score: a new folder enters at one point and would otherwise be evicted
 * immediately by long-established entries, so it could never accumulate a score
 * and the list would freeze against newcomers — which is exactly the folder the
 * user is working in today.
 */
function capList(
  list: FrequentFolderEntry[],
  now: number,
  protectedPath?: string,
): FrequentFolderEntry[] {
  const pinned = list.filter((e) => e.pinned).slice(0, MAX_PINNED)
  const room = Math.max(0, FREQUENT_FOLDERS_MAX - pinned.length)
  const unpinned = list
    .filter((e) => !e.pinned && (e.path === protectedPath || decayedScore(e, now) >= MIN_SCORE))
    .sort((a, b) => {
      // The protected entry sorts first so the slice below can never drop it.
      if (a.path === protectedPath) return -1
      if (b.path === protectedPath) return 1
      return decayedScore(b, now) - decayedScore(a, now)
    })
    .slice(0, room)
  return [...pinned, ...unpinned]
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
            score: decayedScore(existing, now) + VISIT_POINTS,
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
    if (target && !target.pinned && _cache.filter((e) => e.pinned).length >= MAX_PINNED) {
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
