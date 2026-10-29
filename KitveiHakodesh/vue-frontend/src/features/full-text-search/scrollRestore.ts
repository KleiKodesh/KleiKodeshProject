/**
 * Pure decision logic for FTS results scroll restore, extracted from
 * FullTextSearchResultsList.vue so it can be unit-tested without a real DOM/scroller
 * (jsdom has no layout, so measurement/scrollTop can't be exercised faithfully there).
 *
 * The component wires this to Vue reactivity and the WindowedScroller; the race-prone
 * part — "given what has loaded so far, should we restore now, keep waiting, or give
 * up?" — lives here as a plain function. Landing itself is exact (the scroller mounts
 * the target's rows before it scrolls), so there is no retry logic left to decide.
 */

export interface RestoreInputs {
  /** Saved target index into the (filtered) result set, or undefined when nothing to restore. */
  target: number | undefined
  /** Number of results currently loaded (props.results.length). */
  loaded: number
  /** Whether the search stream is still delivering results. */
  isSearching: boolean
}

export type RestoreDecision =
  | { action: 'wait' }                 // preconditions not met yet — keep the watcher armed
  | { action: 'restore'; index: number } // scroll to this (clamped) index now, then stop
  | { action: 'done' }                 // nothing to restore (no target and results settled)

/**
 * Decide what the restore watcher should do on a given tick.
 *
 * Ordering of the guards matters and encodes the bugs we fixed:
 *  - target undefined → WAIT, never "done": on reload the parent sets initialScrollIndex
 *    asynchronously AFTER the child's immediate watch runs, so an early "done" would kill
 *    the watcher before the target ever arrives (tab-switch set it synchronously and so
 *    looked fine — this is why reload was the only broken path).
 *  - no results yet → WAIT: nothing to scroll into.
 *  - target beyond what's loaded AND still streaming → WAIT: the row may still arrive.
 *  - target beyond what's loaded AND stream finished → RESTORE clamped to the last row:
 *    the set is genuinely shorter than when saved (filter/sort changed the count), so land
 *    on the last available row rather than waiting forever.
 *  - otherwise → RESTORE at the target.
 */
export function decideRestore({ target, loaded, isSearching }: RestoreInputs): RestoreDecision {
  if (target == null) {
    // No target provided yet. If the stream is done and results exist, there is genuinely
    // nothing to restore — let the watcher stop. Otherwise keep waiting for the parent to
    // supply the target (async onMounted on reload).
    return !isSearching && loaded > 0 ? { action: 'done' } : { action: 'wait' }
  }
  if (loaded === 0) return { action: 'wait' }
  if (target >= loaded && isSearching) return { action: 'wait' }
  const index = Math.min(target, loaded - 1)
  return { action: 'restore', index }
}
