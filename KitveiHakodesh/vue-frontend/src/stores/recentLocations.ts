import { dbGet, dbSet } from '@/utils/persistence'
import { locationKey, type NavLocation } from './navLocation'

/**
 * The `app-recent-tabs` slice — locations the reader has visited, persisted per
 * workspace. The browser equivalent of History merged with Recently-closed.
 *
 * Deliberately NOT tied to tabs. An entry is a self-describing location
 * (see navLocation.ts), so it outlives the tab that produced it, two entries can
 * hold different places in the same book, and removing one has no effect on any
 * open tab. Selecting one navigates the CURRENT tab, exactly as an address bar
 * does — it is the address bar's dropdown, not a tab switcher.
 *
 * Eviction is LRU on `recentStamp`, and that is the only way an entry leaves
 * (other than the explicit remove-from-list action).
 */

const RECENT_DB = 'app-recent-tabs'

/** LRU bound. Deduped by document, so this is 50 distinct documents, not 50 visits. */
export const RECENT_LOCATIONS_MAX = 50

interface PersistedRecentLocations {
  /** Kept as `tabs` for compatibility with lists written by earlier builds. */
  tabs: NavLocation[]
}

function key(wsId: string): string {
  return `recent:${wsId}`
}

export function loadRecentLocations(wsId: string): Promise<NavLocation[]> {
  return dbGet<PersistedRecentLocations>(RECENT_DB, key(wsId))
    .then((v) => (v?.tabs ?? []).filter(isWellFormed))
    .catch(() => [])
}

/**
 * Entries written by earlier builds were tab snapshots, not locations — they have
 * no recentStamp or route. Drop them rather than migrate: recents rebuilds itself
 * from ordinary use within minutes.
 */
function isWellFormed(loc: NavLocation): boolean {
  return !!loc && typeof loc.route === 'string' && typeof loc.recentStamp === 'number'
}

export function saveRecentLocations(wsId: string, locations: NavLocation[]): Promise<void> {
  // IndexedDB structured-clones what it stores and cannot clone a Vue reactive
  // proxy, so deep-copy to plain objects here rather than at each call site.
  return dbSet<PersistedRecentLocations>(RECENT_DB, key(wsId), {
    tabs: JSON.parse(JSON.stringify(locations)) as NavLocation[],
  })
}

/**
 * Records a visit: replaces any existing entry for the same DOCUMENT (so
 * revisiting a book updates its position and bumps it to the front rather than
 * stacking duplicates), then trims the oldest away.
 */
export function recordVisit(locations: NavLocation[], visit: NavLocation): NavLocation[] {
  const docKey = locationKey(visit)
  const withoutDuplicate = locations.filter((l) => locationKey(l) !== docKey)
  const next = [visit, ...withoutDuplicate]
  if (next.length <= RECENT_LOCATIONS_MAX) return next
  // Oldest stamps fall off the end.
  return [...next].sort((a, b) => b.recentStamp - a.recentStamp).slice(0, RECENT_LOCATIONS_MAX)
}

/** Most-recent-first, for display. */
export function sortedByRecency(locations: NavLocation[]): NavLocation[] {
  return [...locations].sort((a, b) => b.recentStamp - a.recentStamp)
}
