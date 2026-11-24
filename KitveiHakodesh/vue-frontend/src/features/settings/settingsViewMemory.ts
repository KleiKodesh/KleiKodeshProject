/**
 * What the settings page looked like when you left it, for THIS SESSION ONLY:
 * the search query and the scroll position.
 *
 * Both are needed to restore the view, and they must be restored TOGETHER — the
 * query decides which sections are visible, so a scroll offset means something
 * different under a different query. Restoring the offset alone would land in the
 * wrong place whenever a filter was active.
 *
 * The set of hidden sections is NOT stored: `applyFilter` recomputes it from the
 * query every time, so the query is the whole state and the hidden set is a
 * derivation of it. Storing both would be storing the same fact twice.
 *
 * Plain module variables, not a per-tab map: settings is unique per pane
 * (SINGLE_TAB_ROUTES in tabStore), so at most one settings tab exists and there is
 * nothing to key by. Closing that tab clears both, so a freshly opened settings
 * page starts clean.
 *
 * Persisted to localStorage so a settings tab restored on the next launch reopens
 * at the same query and offset. Settings VALUES live in the settings store; this is
 * only the view state.
 */
import { lsGet, lsSet } from '@/utils/persistence'

const STORAGE_KEY = 'settings-view-state'
interface SettingsViewState { scrollTop: number; searchQuery: string }

const saved = lsGet<SettingsViewState>(STORAGE_KEY)
let scrollTop = saved?.scrollTop ?? 0
let searchQuery = saved?.searchQuery ?? ''

// Coalesced: scroll fires every frame, and a localStorage write per frame is waste.
let saveTimer: ReturnType<typeof setTimeout> | undefined
function save(): void {
  clearTimeout(saveTimer)
  saveTimer = setTimeout(() => lsSet<SettingsViewState>(STORAGE_KEY, { scrollTop, searchQuery }), 200)
}

export function getSettingsScrollTop(): number {
  return scrollTop
}

export function setSettingsScrollTop(value: number): void {
  scrollTop = value
  save()
}

export function getSettingsSearchQuery(): string {
  return searchQuery
}

export function setSettingsSearchQuery(value: string): void {
  searchQuery = value
  save()
}

/** Called when a settings tab closes, so the next one opens clean. */
export function clearSettingsViewState(): void {
  scrollTop = 0
  searchQuery = ''
  save()
}
