// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import { useFastDocViewSearch } from './useFastDocViewSearch'
import type { FastDocViewApi, FastDocViewSearchStep } from '@/webview-host/fastDocViewApi'

/**
 * A document of `blockCount` blocks whose search counts are `partial` while the index builds and `final` after it:
 * the blocks whose count differs (and `alsoProvisional`) are reported provisional by steps over the partial index.
 */
function fakeSearchApi(blockCount: number, partial: Record<number, number>, final: Record<number, number>, alsoProvisional: number[] = []) {
  const state = { complete: false, calls: [] as Array<[number, number]> }
  const provisional = new Set([...Object.keys(partial), ...Object.keys(final)].map(Number).filter((b) => partial[b] !== final[b]))
  for (const block of alsoProvisional) provisional.add(block)
  const search = async (_id: string, _query: string, _matchCase: boolean, from: number, max: number): Promise<FastDocViewSearchStep> => {
    state.calls.push([from, max])
    const end = Math.min(blockCount, from + max)
    const counts = state.complete ? final : partial
    const hitBlocks: number[] = []
    const hitCounts: number[] = []
    const provisionalBlocks: number[] = []
    for (let block = from; block < end; block++) {
      if (counts[block]) { hitBlocks.push(block); hitCounts.push(counts[block]!) }
      if (!state.complete && provisional.has(block)) provisionalBlocks.push(block)
    }
    const reachedEnd = end >= blockCount
    return { hitBlocks, hitCounts, nextBlock: reachedEnd ? -1 : end, reachedEnd, provisionalBlocks }
  }
  const api = { search, searchNotes: search } as unknown as FastDocViewApi
  return { api, state }
}

async function settle() {
  for (let i = 0; i < 20; i++) { await Promise.resolve(); await nextTick() }
}

describe('useFastDocViewSearch - provisional blocks', () => {
  afterEach(() => vi.useRealTimers())

  it('recounts blocks searched over the partial index once the index completes: no duplicates, none lost', async () => {
    vi.useFakeTimers()
    // block 5: 1 match drawn provisionally, 3 in the final rendering; block 9: a match that disappears; block 12: appears;
    // block 20: provisional but unchanged
    const { api, state } = fakeSearchApi(30, { 2: 1, 5: 1, 9: 2, 20: 1 }, { 2: 1, 5: 3, 12: 1, 20: 1 }, [20])
    const complete = ref(false)
    const scope = effectScope()
    const search = scope.run(() => useFastDocViewSearch({ api, documentId: () => 'doc', currentBlock: () => 0, isIndexComplete: () => complete.value }))!
    search.query.value = 'word'
    await vi.advanceTimersByTimeAsync(200)
    await settle()
    expect(search.matchCount.value).toBe(5)
    expect(Array.from(search.matches.value.rows)).toEqual([2, 5, 9, 20])
    // step to block 9's second match, then the index completes
    search.step('forward')
    for (let i = 0; i < 3; i++) search.step('forward')
    expect(search.currentMatchAsBlock()).toEqual({ block: 9, occurrence: 1 })
    const callsBefore = state.calls.length
    state.complete = true
    complete.value = true
    await settle()
    expect(Array.from(search.matches.value.rows)).toEqual([2, 5, 12, 20])
    expect(search.matchCount.value).toBe(6)
    // only the provisional blocks were searched again, adjacent ones in one call
    expect(state.calls.slice(callsBefore)).toEqual([[5, 1], [9, 1], [12, 1], [20, 1]])
    // the current match (block 9, gone) moved to the next match after it; stepping goes on from there
    expect(search.currentMatchAsBlock()).toEqual({ block: 12, occurrence: 0 })
    expect(search.step('forward')).toEqual({ block: 20, occurrence: 0 })
    expect(search.step('forward')).toEqual({ block: 2, occurrence: 0 })
    expect(search.step('backward')).toEqual({ block: 20, occurrence: 0 })
    scope.stop()
  })

  it('recounts at once a step that arrives after the index completed', async () => {
    vi.useFakeTimers()
    const { api, state } = fakeSearchApi(10, { 4: 1 }, { 4: 2 })
    const complete = ref(false)
    let releaseStep: (() => void) | null = null
    const slowApi = {
      ...api,
      search: async (...args: Parameters<FastDocViewApi['search']>) => {
        const answer = await api.search(...args)       // computed over the partial index...
        await new Promise<void>((resolve) => (releaseStep = resolve))
        return answer                                  // ...and delivered after the index completed
      },
    } as FastDocViewApi
    const scope = effectScope()
    const search = scope.run(() => useFastDocViewSearch({ api: slowApi, documentId: () => 'doc', currentBlock: () => 0, isIndexComplete: () => complete.value }))!
    search.query.value = 'word'
    await vi.advanceTimersByTimeAsync(200)
    state.complete = true
    complete.value = true
    await settle()
    releaseStep!()
    await settle()
    releaseStep!()   // the recount's own step
    await settle()
    expect(search.matchCount.value).toBe(2)
    expect(Array.from(search.matches.value.rows)).toEqual([4])
    scope.stop()
  })

  it('a new query drops the old run\'s provisional blocks', async () => {
    vi.useFakeTimers()
    const { api, state } = fakeSearchApi(10, { 4: 1 }, { 4: 2 })
    const complete = ref(false)
    const scope = effectScope()
    const search = scope.run(() => useFastDocViewSearch({ api, documentId: () => 'doc', currentBlock: () => 0, isIndexComplete: () => complete.value }))!
    search.query.value = 'word'
    await vi.advanceTimersByTimeAsync(200)
    await settle()
    search.clear()
    const callsBefore = state.calls.length
    state.complete = true
    complete.value = true
    await settle()
    expect(state.calls.length).toBe(callsBefore)
    expect(search.matchCount.value).toBe(0)
    scope.stop()
  })
})
