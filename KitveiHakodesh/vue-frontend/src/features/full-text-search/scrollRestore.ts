/**
 * Pure decision logic for FTS results scroll restore, extracted from
 * FullTextSearchResultsList.vue so it can be unit-tested without a real DOM/virtualizer
 * (jsdom has no layout, so measurement/scrollTop can't be exercised faithfully there).
 *
 * The component wires these to Vue reactivity and the TanStack virtualizer; the tricky,
 * race-prone part — "given what has loaded so far, should we restore now, keep waiting,
 * or give up?" — lives here as plain functions.
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

/**
 * Decide, on each retry tick of the offset-application loop, whether to apply, retry, or
 * give up. `measured` is whether the target row is present in the virtualizer's
 * measurementsCache yet. The loop re-issues scrollToIndex between ticks to pull the target
 * range into measurement.
 *
 * We keep retrying while the target *should* still be about to appear:
 *  - not measured yet but results are still streaming (row not loaded), OR
 *  - not measured yet but we're under the attempt budget.
 * Only give up once we've exhausted the budget AND the stream has settled — so a slow, heavy
 * reload (deep row measured late) no longer silently gives up near the top, which was the
 * remaining "works sometimes" flakiness.
 */
export function decideApplyOffset(opts: {
  measured: boolean
  attempts: number
  maxAttempts: number
  isSearching: boolean
}): 'apply' | 'retry' | 'giveup' {
  const { measured, attempts, maxAttempts, isSearching } = opts
  if (measured) return 'apply'
  if (isSearching) return 'retry' // more results / measurements still incoming
  if (attempts < maxAttempts) return 'retry'
  return 'giveup'
}

/**
 * How wide the render window must be to contain a target index (plus a page of headroom),
 * so the virtualizer actually has a row there to scroll to.
 */
export function windowNeededFor(index: number, renderPage: number): number {
  return index + renderPage
}
