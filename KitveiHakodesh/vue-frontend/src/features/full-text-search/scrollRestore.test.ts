import { describe, it, expect } from 'vitest'
import { decideRestore, decideApplyOffset, windowNeededFor } from './scrollRestore'

describe('decideRestore — when the restore watcher acts', () => {
  it('WAITS when the target is not provided yet but the stream is still running (reload race)', () => {
    // Reload: child mounts and runs its immediate watch BEFORE the parent sets
    // initialScrollIndex in its async onMounted. Target is undefined here — must not "done".
    expect(decideRestore({ target: undefined, loaded: 0, isSearching: true })).toEqual({ action: 'wait' })
    expect(decideRestore({ target: undefined, loaded: 200, isSearching: true })).toEqual({ action: 'wait' })
  })

  it('WAITS when target is undefined and results have not settled (still empty, not searching)', () => {
    // No results and not searching yet — the search may not have kicked off; keep waiting
    // for either results or a target rather than declaring done prematurely.
    expect(decideRestore({ target: undefined, loaded: 0, isSearching: false })).toEqual({ action: 'wait' })
  })

  it('is DONE only when there is genuinely no target and results have settled', () => {
    // Fresh search with no saved position: results arrived, stream finished, no target.
    expect(decideRestore({ target: undefined, loaded: 5000, isSearching: false })).toEqual({ action: 'done' })
  })

  it('WAITS when a target exists but no results have loaded yet', () => {
    expect(decideRestore({ target: 4200, loaded: 0, isSearching: true })).toEqual({ action: 'wait' })
    expect(decideRestore({ target: 4200, loaded: 0, isSearching: false })).toEqual({ action: 'wait' })
  })

  it('WAITS when the target is beyond what has streamed in AND the stream is still running', () => {
    // Deep target, only a prefix has arrived — the row may still stream in.
    expect(decideRestore({ target: 4200, loaded: 200, isSearching: true })).toEqual({ action: 'wait' })
    expect(decideRestore({ target: 4200, loaded: 4199, isSearching: true })).toEqual({ action: 'wait' })
  })

  it('RESTORES at the target once it has streamed in (deep index, tab-switch or completed reload)', () => {
    expect(decideRestore({ target: 4200, loaded: 4201, isSearching: true })).toEqual({ action: 'restore', index: 4200 })
    expect(decideRestore({ target: 4200, loaded: 10000, isSearching: false })).toEqual({ action: 'restore', index: 4200 })
    expect(decideRestore({ target: 0, loaded: 1, isSearching: false })).toEqual({ action: 'restore', index: 0 })
  })

  it('CLAMPS to the last row when the target is unreachable and the stream has finished', () => {
    // Saved against a longer set (e.g. before a filter/sort shortened it). Land on last row.
    expect(decideRestore({ target: 4200, loaded: 300, isSearching: false })).toEqual({ action: 'restore', index: 299 })
    expect(decideRestore({ target: 4200, loaded: 4200, isSearching: false })).toEqual({ action: 'restore', index: 4199 })
  })

  it('handles the exact boundary: target === loaded (index would be out of range)', () => {
    // target 200 with 200 loaded (indices 0..199): row 200 does not exist.
    expect(decideRestore({ target: 200, loaded: 200, isSearching: true })).toEqual({ action: 'wait' })
    expect(decideRestore({ target: 200, loaded: 200, isSearching: false })).toEqual({ action: 'restore', index: 199 })
    // one more loaded → row 200 exists.
    expect(decideRestore({ target: 200, loaded: 201, isSearching: true })).toEqual({ action: 'restore', index: 200 })
  })
})

describe('decideApplyOffset — the retry loop that waits for the row to be measured', () => {
  const MAX = 10

  it('APPLIES immediately once the target row is measured', () => {
    expect(decideApplyOffset({ measured: true, attempts: 0, maxAttempts: MAX, isSearching: false })).toBe('apply')
    expect(decideApplyOffset({ measured: true, attempts: 9, maxAttempts: MAX, isSearching: true })).toBe('apply')
  })

  it('RETRIES while not measured but the stream is still delivering (heavy reload)', () => {
    // This is the flaky case the hardening fixes: a deep row measured late must NOT give up
    // just because the fixed attempt budget elapsed while results were still streaming.
    expect(decideApplyOffset({ measured: false, attempts: 0, maxAttempts: MAX, isSearching: true })).toBe('retry')
    expect(decideApplyOffset({ measured: false, attempts: 50, maxAttempts: MAX, isSearching: true })).toBe('retry')
    expect(decideApplyOffset({ measured: false, attempts: 999, maxAttempts: MAX, isSearching: true })).toBe('retry')
  })

  it('RETRIES while not measured and under the attempt budget (stream settled)', () => {
    expect(decideApplyOffset({ measured: false, attempts: 0, maxAttempts: MAX, isSearching: false })).toBe('retry')
    expect(decideApplyOffset({ measured: false, attempts: 9, maxAttempts: MAX, isSearching: false })).toBe('retry')
  })

  it('GIVES UP only after the budget is exhausted AND the stream has settled', () => {
    expect(decideApplyOffset({ measured: false, attempts: 10, maxAttempts: MAX, isSearching: false })).toBe('giveup')
    expect(decideApplyOffset({ measured: false, attempts: 11, maxAttempts: MAX, isSearching: false })).toBe('giveup')
  })
})

describe('windowNeededFor — render window must contain the target', () => {
  it('reserves a page of headroom beyond the target index', () => {
    expect(windowNeededFor(4200, 200)).toBe(4400)
    expect(windowNeededFor(0, 200)).toBe(200)
  })
})

// ── Scenario simulations: replay a full reload/tab-switch as a sequence of ticks ──────────
// These prove the END-TO-END behaviour: feed the decision function the states it would see
// over time and assert it eventually restores to the right index (and never prematurely
// gives up or stops). This is what catches the "works sometimes" regressions.

/** Drive decideRestore over a sequence of states; return the first non-wait decision. */
function replay(states: Array<{ target?: number; loaded: number; isSearching: boolean }>) {
  for (const s of states) {
    const d = decideRestore({ target: s.target, loaded: s.loaded, isSearching: s.isSearching })
    if (d.action !== 'wait') return d
  }
  return { action: 'wait' as const }
}

describe('scenario replays', () => {
  it('RELOAD, target arrives late (async onMounted): waits through undefined, then restores', () => {
    const d = replay([
      { target: undefined, loaded: 0, isSearching: false },   // child mounted, nothing yet
      { target: undefined, loaded: 200, isSearching: true },  // first batch streaming
      { target: 4200, loaded: 200, isSearching: true },       // parent finally set the target
      { target: 4200, loaded: 2000, isSearching: true },      // more streamed, still short
      { target: 4200, loaded: 5000, isSearching: true },      // target row now present
    ])
    expect(d).toEqual({ action: 'restore', index: 4200 })
  })

  it('RELOAD, fresh re-search streams past the target: restores exactly when reached', () => {
    const d = replay([
      { target: 8000, loaded: 200, isSearching: true },
      { target: 8000, loaded: 4000, isSearching: true },
      { target: 8000, loaded: 8000, isSearching: true },   // 0..7999 — row 8000 not there yet
      { target: 8000, loaded: 8001, isSearching: true },   // now it is
    ])
    expect(d).toEqual({ action: 'restore', index: 8000 })
  })

  it('TAB SWITCH, complete cache in one shot with target set synchronously: restores first tick', () => {
    const d = replay([
      { target: 3000, loaded: 12000, isSearching: false },
    ])
    expect(d).toEqual({ action: 'restore', index: 3000 })
  })

  it('RELOAD, saved position beyond a now-shorter set: clamps to last row after stream ends', () => {
    const d = replay([
      { target: 9000, loaded: 200, isSearching: true },
      { target: 9000, loaded: 1500, isSearching: true },
      { target: 9000, loaded: 1500, isSearching: false },  // stream ended, only 1500 results
    ])
    expect(d).toEqual({ action: 'restore', index: 1499 })
  })

  it('FRESH SEARCH, no saved position: never restores, ends "done" once settled', () => {
    const d = replay([
      { target: undefined, loaded: 0, isSearching: true },
      { target: undefined, loaded: 5000, isSearching: true },
      { target: undefined, loaded: 5000, isSearching: false },
    ])
    expect(d).toEqual({ action: 'done' })
  })
})
