import { dbGet, dbSet } from '@/utils/persistence'
import type { Tab } from './tabStore'

/**
 * The `app-recent-tabs` slice of persistence — the tab list that doesn't forget.
 *
 * This is a PARALLEL COPY of `tabStore.tabs`, holding the very same `Tab` objects
 * under the very same tab ids. Every mutation the real list sees is applied here
 * too — open appends, navigation updates in place, titles and breadcrumbs follow.
 *
 * Exactly one difference: closing a tab removes it from `tabStore.tabs` but NOT
 * from here. Entries leave this list only by LRU eviction once it outgrows
 * `RECENT_TABS_MAX`. A "closed tab" is therefore not a different kind of record —
 * it is simply a tab that is still in this list and no longer in the live one.
 *
 * Because the ids match, everything already keyed by tab id keeps working for a
 * closed entry: `tab:<ws>:<tabId>` and `book:<ws>:<tabId>:<bookId>` in
 * `tabStatePersistence` hold that entry's own reading position, so two closed
 * tabs on the same book reopen to their own places rather than collapsing onto
 * the shared per-book `lastRead`. Those records follow the same rule: they are
 * deleted when the entry is evicted from here, not when the tab is closed.
 */

const RECENT_TABS_DB = 'app-recent-tabs'

/** LRU bound. Live tabs are never evicted, so this is a floor, not a hard ceiling. */
export const RECENT_TABS_MAX = 50

/**
 * A remembered tab. The `Tab` fields are the same objects the live list holds;
 * the two extras below are bookkeeping this list owns.
 */
export interface RecentTab extends Tab {
  /** Monotonic recency stamp — highest is most recently active. Drives LRU eviction. */
  recentStamp: number
  /** True while a live tab with this id exists. Never evicted while true. */
  open?: boolean
}

interface PersistedRecentTabs {
  tabs: RecentTab[]
}

function key(wsId: string): string {
  return `recent:${wsId}`
}

export function loadRecentTabs(wsId: string): Promise<RecentTab[]> {
  return dbGet<PersistedRecentTabs>(RECENT_TABS_DB, key(wsId)).then((v) => v?.tabs ?? [])
}

export function saveRecentTabs(wsId: string, tabs: RecentTab[]): Promise<void> {
  return dbSet<PersistedRecentTabs>(RECENT_TABS_DB, key(wsId), { tabs })
}

/**
 * Trims to the cap by dropping the least-recently-used CLOSED entries, and
 * returns the trimmed list alongside the ids that were dropped so the caller can
 * tear down their per-tab state. Open tabs are never candidates: the live list is
 * the authority on those, and evicting one would orphan a visible tab.
 */
export function evictLru(tabs: RecentTab[]): { kept: RecentTab[]; evicted: string[] } {
  const closed = tabs.filter((t) => !t.open)
  const openCount = tabs.length - closed.length
  const room = Math.max(0, RECENT_TABS_MAX - openCount)
  if (closed.length <= room) return { kept: tabs, evicted: [] }

  // Oldest stamps go first.
  const doomed = new Set(
    [...closed]
      .sort((a, b) => a.recentStamp - b.recentStamp)
      .slice(0, closed.length - room)
      .map((t) => t.id),
  )
  return { kept: tabs.filter((t) => !doomed.has(t.id)), evicted: [...doomed] }
}
