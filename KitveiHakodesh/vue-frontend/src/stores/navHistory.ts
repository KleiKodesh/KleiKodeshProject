import { ref } from 'vue'
import { locationKey, type NavLocation } from './navLocation'

/**
 * Per-tab Back/Forward stacks — the third tab collection, and the only one that is
 * neither persisted nor shared.
 *
 * A browser tab's history is a list plus a cursor, not two stacks: Back moves the
 * cursor left, Forward moves it right, and navigating from anywhere other than the
 * end TRUNCATES everything after the cursor (the forward branch is discarded).
 * That single rule is what makes back-then-navigate behave the way people expect.
 *
 * In memory only, keyed by tab id, dropped when the tab closes. Nothing here
 * outlives the session — the persisted record of where the reader has been is
 * `recentLocations`, which is a different collection with a different purpose.
 */

interface TabHistory {
  /** Oldest → newest. The cursor points at the CURRENT location. */
  entries: NavLocation[]
  cursor: number
}

/** Frames kept per tab, oldest dropped first — Chrome's per-tab session history limit. */
const MAX_ENTRIES = 50

// Reactive so `canGoBack`/`canGoForward` can drive button disabled state.
const histories = ref(new Map<string, TabHistory>())

function bump() {
  // Reassign so plain-ref dependents re-evaluate: mutating a Map is not tracked.
  histories.value = new Map(histories.value)
}

function get(tabId: string): TabHistory | undefined {
  return histories.value.get(tabId)
}

/**
 * Records a navigation. Truncates any forward branch first, so going back and then
 * navigating somewhere new discards what used to be ahead — standard browser
 * behaviour, and the reason this is a cursor rather than two stacks.
 */
export function pushLocation(tabId: string, location: NavLocation): void {
  const history = get(tabId)
  if (!history) {
    histories.value.set(tabId, { entries: [location], cursor: 0 })
    bump()
    return
  }
  history.entries = [...history.entries.slice(0, history.cursor + 1), location].slice(-MAX_ENTRIES)
  history.cursor = history.entries.length - 1
  bump()
}

/**
 * Updates the CURRENT frame in place — for position changes, which are not
 * navigations. Leaving a book at a different scroll position must not create a
 * frame the reader has to press Back through.
 */
export function updateCurrentLocation(tabId: string, patch: Partial<NavLocation>): void {
  const history = get(tabId)
  if (!history) return
  const current = history.entries[history.cursor]
  if (!current) return
  history.entries[history.cursor] = { ...current, ...patch }
  bump()
}

export function canGoBack(tabId: string): boolean {
  const history = get(tabId)
  return !!history && history.cursor > 0
}

export function canGoForward(tabId: string): boolean {
  const history = get(tabId)
  return !!history && history.cursor < history.entries.length - 1
}

/** Moves the cursor and returns the location to navigate to, or null at the end. */
export function stepHistory(tabId: string, direction: -1 | 1): NavLocation | null {
  const history = get(tabId)
  if (!history) return null
  const target = history.cursor + direction
  if (target < 0 || target >= history.entries.length) return null
  history.cursor = target
  bump()
  return history.entries[target] ?? null
}

/**
 * Moves the cursor straight to an entry — the hold-to-show list path, where the
 * reader picks a frame several steps away instead of stepping one at a time.
 */
export function jumpHistory(tabId: string, index: number): NavLocation | null {
  const history = get(tabId)
  if (!history) return null
  if (index < 0 || index >= history.entries.length || index === history.cursor) return null
  history.cursor = index
  bump()
  return history.entries[index] ?? null
}

/** A tab's full history, oldest → newest — for the hold-to-show dropdown. */
export function historyEntries(tabId: string): NavLocation[] {
  return get(tabId)?.entries ?? []
}

/** The index into `historyEntries` of the frame the tab is currently on. */
export function historyCursor(tabId: string): number {
  return get(tabId)?.cursor ?? -1
}

/** The frame the tab is currently on, for capturing position before leaving it. */
export function currentLocation(tabId: string): NavLocation | null {
  const history = get(tabId)
  if (!history) return null
  return history.entries[history.cursor] ?? null
}

/**
 * Removes the current entry from the stack and steps back to the previous one.
 * Returns the location to navigate to, or null if there is no previous entry
 * (i.e. the current entry is the only one — caller should go home instead).
 *
 * This is the "close current page" operation for environments without a native
 * tab strip (VSTO, dev): Ctrl+W removes this place from history rather than
 * merely moving the cursor past it.
 */
export function popCurrentAndGoBack(tabId: string): NavLocation | null {
  const history = get(tabId)
  if (!history || history.cursor === 0) return null

  // Remove EVERY frame for this document, not just the one under the cursor.
  // pushLocation does not dedupe, so a book visited twice holds two frames; splicing
  // only the cursor frame left the older one behind, and the next Back — or the next
  // close, which steps the cursor onto it — showed the book the reader had just
  // closed. Navigating away from that resurrected frame then re-recorded it into
  // recents through applyTabPatch, so the "closed" book came back for good.
  const closedKey = locationKey(history.entries[history.cursor]!)
  const kept: NavLocation[] = []
  let cursor = -1
  for (const [index, entry] of history.entries.entries()) {
    if (locationKey(entry) === closedKey) continue
    kept.push(entry)
    // The frame to land on is the newest survivor at or before the cursor.
    if (index < history.cursor) cursor = kept.length - 1
  }

  history.entries = kept
  history.cursor = cursor
  bump()
  return cursor === -1 ? null : (history.entries[cursor] ?? null)
}

/**
 * Clears ALL history entries for a tab, leaving an empty stack.
 * Used by Ctrl+X ("close all") in environments without a native tab strip.
 * The caller is responsible for navigating home afterwards.
 */
export function clearHistory(tabId: string): void {
  const history = get(tabId)
  if (!history) return
  history.entries = []
  history.cursor = -1
  bump()
}

/** Drops a tab's history — called when the tab closes. */
export function dropHistory(tabId: string): void {
  if (!histories.value.has(tabId)) return
  histories.value.delete(tabId)
  bump()
}

/** Exposed for the reactive guards; callers should prefer canGoBack/canGoForward. */
export function historiesRef() {
  return histories
}
