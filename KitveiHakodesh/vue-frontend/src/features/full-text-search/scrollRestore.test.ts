import { describe, it, expect } from 'vitest'
import { decideRestore } from './scrollRestore'

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
