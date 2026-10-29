// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { effectScope } from 'vue'
import { useFastDocViewDocument, FAST_DOC_VIEW_SCAN_RETRY_DELAYS_MS } from './useFastDocViewDocument'
import { FastDocViewBlockCache } from './fastDocViewBlockCache'
import {
  FAST_DOC_VIEW_SOURCE_CHANGED,
  decodeFastDocViewWeights,
  type FastDocViewApi,
  type FastDocViewBlock,
} from '@/webview-host/fastDocViewApi'

function block(index: number): FastDocViewBlock {
  return { index, kind: 'paragraph', html: `<p>${index}</p>`, notes: index === 5 ? [{ kind: 'footnote', number: 1, label: '1', html: 'n' }] : [] }
}

function fakeApi(total: number) {
  const calls: { op: string; args: unknown[] }[] = []
  let generation = 0
  let failNextBlocksWithSourceChanged = false
  const api: FastDocViewApi = {
    open: async (path) => {
      calls.push({ op: 'open', args: [path] })
      generation++
      return {
        documentId: `doc${generation}`, blockCount: total, knownBlockCount: total, isIndexComplete: true,
        isRightToLeft: true, stylesheet: '', headingCount: 2, footnoteCount: 1, endnoteCount: 0,
      }
    },
    blocks: async (documentId, first, count) => {
      calls.push({ op: 'blocks', args: [documentId, first, count] })
      if (failNextBlocksWithSourceChanged) {
        failNextBlocksWithSourceChanged = false
        throw new Error(`service error: ${FAST_DOC_VIEW_SOURCE_CHANGED}`)
      }
      const result: FastDocViewBlock[] = []
      for (let index = first; index < Math.min(first + count, total); index++) result.push(block(index))
      return result
    },
    headings: async () => [
      { blockIndex: 0, level: 1, text: 'a' },
      { blockIndex: 50, level: 2, text: 'b' },
      { blockIndex: 20, level: 1, text: 'c' },
    ],
    weights: async () => Int32Array.from({ length: total }, () => 10),
    bookmark: async () => null,
    search: async () => ({ hitBlocks: [], nextBlock: -1, reachedEnd: true }),
    sections: async () => [{ firstBlockIndex: 0, textWidthTwips: 9360 }, { firstBlockIndex: 50, textWidthTwips: 13680 }],
    scan: async (documentId, blocks) => {
      calls.push({ op: 'scan', args: [documentId, blocks] })
      return { knownBlockCount: total, isIndexComplete: true, footnoteCount: 1, endnoteCount: 0 }
    },
    close: async (documentId) => { calls.push({ op: 'close', args: [documentId] }) },
  }
  return { api, calls, failSourceChanged: () => { failNextBlocksWithSourceChanged = true } }
}

async function flush() {
  for (let i = 0; i < 5; i++) await Promise.resolve()
}

describe('FastDocViewBlockCache', () => {
  it('reports missing spans', () => {
    const cache = new FastDocViewBlockCache(100)
    cache.store([block(2), block(3), block(6)], [0, 10])
    expect(cache.missingSpans(0, 8)).toEqual([[0, 2], [4, 6], [7, 8]])
  })

  it('evicts the farthest blocks first and keeps retained ranges', () => {
    const cache = new FastDocViewBlockCache(10)
    cache.store(Array.from({ length: 10 }, (_, i) => block(i)), [0, 10])
    cache.store(Array.from({ length: 5 }, (_, i) => block(100 + i)), [100, 105], [[0, 2]])
    expect(cache.size).toBe(10)
    expect(cache.has(0) && cache.has(1)).toBe(true)
    for (let i = 100; i < 105; i++) expect(cache.has(i)).toBe(true)
    // 2..6 are the evictable blocks farthest from the focus at 100.
    expect(cache.has(2)).toBe(false)
    expect(cache.has(9)).toBe(true)
  })
})

describe('useFastDocViewDocument', () => {
  it('fetches only the missing blocks and stays within capacity', async () => {
    const { api, calls } = fakeApi(10_000)
    const scope = effectScope()
    const document = scope.run(() => useFastDocViewDocument({ api, cacheCapacity: 150 }))!
    await document.open('C:/a.docx')
    await document.ensureBlocks(0, 40)
    await document.ensureBlocks(20, 60)
    const blockCalls = calls.filter((call) => call.op === 'blocks').map((call) => call.args.slice(1))
    expect(blockCalls).toEqual([[0, 40], [40, 20]])
    expect(document.blockAt(30)?.html).toBe('<p>30</p>')
    await document.ensureBlocks(5000, 5250)
    expect(document.cachedBlockCount()).toBeLessThanOrEqual(250)
    await document.ensureBlocks(9000, 9040)
    expect(document.cachedBlockCount()).toBeLessThanOrEqual(150)
    expect(document.blockAt(9010)).toBeDefined()
    expect(document.blockAt(0)).toBeUndefined()
    scope.stop()
  })

  it('splits a long span into bounded requests', async () => {
    const { api, calls } = fakeApi(1000)
    const scope = effectScope()
    const document = scope.run(() => useFastDocViewDocument({ api }))!
    await document.open('x')
    await document.ensureBlocks(0, 250)
    expect(calls.filter((call) => call.op === 'blocks').map((call) => call.args[2])).toEqual([100, 100, 50])
    scope.stop()
  })

  it('builds the index in the background: the count only grows, then the outline reloads', async () => {
    const { api, calls } = fakeApi(30)
    const originalOpen = api.open
    api.open = async (path) => ({ ...(await originalOpen(path)), blockCount: 10, knownBlockCount: 10, isIndexComplete: false })
    let known = 10
    api.scan = async (documentId, blocks) => {
      calls.push({ op: 'scan', args: [documentId, blocks] })
      known = Math.min(30, known + 10)
      return { knownBlockCount: known, isIndexComplete: known === 30, footnoteCount: known, endnoteCount: 0 }
    }
    const scope = effectScope()
    const document = scope.run(() => useFastDocViewDocument({ api }))!
    await document.open('x')
    expect(document.blockCount.value).toBeLessThan(30)
    for (let attempt = 0; attempt < 20 && !document.isIndexComplete.value; attempt++) await new Promise((resolve) => setTimeout(resolve, 0))
    expect(document.isIndexComplete.value).toBe(true)
    expect(document.blockCount.value).toBe(30)
    expect(calls.filter((call) => call.op === 'scan').length).toBe(2)
    expect(document.summary.value?.footnoteCount).toBe(30)
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(document.rowWeights.value?.length).toBe(30)
    scope.stop()
  })

  it('a restored position is known before the document shows', async () => {
    const { api } = fakeApi(30)
    const originalOpen = api.open
    api.open = async (path) => ({ ...(await originalOpen(path)), blockCount: 5, knownBlockCount: 5, isIndexComplete: false })
    let known = 5
    api.scan = async () => {
      known = Math.min(30, known + 5)
      return { knownBlockCount: known, isIndexComplete: known === 30, footnoteCount: 0, endnoteCount: 0 }
    }
    const scope = effectScope()
    const document = scope.run(() => useFastDocViewDocument({ api }))!
    await document.open('x', { reachIndex: 12 })
    expect(document.blockCount.value).toBeGreaterThan(12)
    scope.stop()
  })

  it('reopens on a source-changed error and retries', async () => {
    const { api, calls, failSourceChanged } = fakeApi(100)
    const scope = effectScope()
    const document = scope.run(() => useFastDocViewDocument({ api }))!
    await document.open('x')
    const versionBefore = document.contentVersion.value
    failSourceChanged()
    await document.ensureBlocks(0, 10)
    expect(calls.filter((call) => call.op === 'open').length).toBe(2)
    expect(document.summary.value?.documentId).toBe('doc2')
    expect(document.contentVersion.value).toBe(versionBefore + 1)
    expect(document.blockAt(3)).toBeDefined()
    scope.stop()
  })

  it('a failing scan is retried with backoff, then reported - never a silent stall', async () => {
    vi.useFakeTimers()
    try {
      const { api } = fakeApi(100)
      api.open = async () => ({
        documentId: 'doc1', blockCount: 10, knownBlockCount: 10, isIndexComplete: false,
        isRightToLeft: true, stylesheet: '', headingCount: 0, footnoteCount: 0, endnoteCount: 0,
      })
      let scans = 0
      let failuresLeft = 2
      api.scan = async () => {
        scans++
        if (failuresLeft > 0) { failuresLeft--; throw new Error('transient') }
        return { knownBlockCount: 100, isIndexComplete: true, footnoteCount: 0, endnoteCount: 0 }
      }
      const scope = effectScope()
      const document = scope.run(() => useFastDocViewDocument({ api }))!
      await document.open('x')
      await vi.runAllTimersAsync()
      expect(scans).toBe(3)
      expect(document.blockCount.value).toBe(100)
      expect(document.indexBuildFailed.value).toBe(false)

      failuresLeft = Infinity
      scans = 0
      await document.open('y')
      await vi.runAllTimersAsync()
      expect(scans).toBe(FAST_DOC_VIEW_SCAN_RETRY_DELAYS_MS.length + 1)
      expect(document.indexBuildFailed.value).toBe(true)
      expect(document.blockCount.value).toBe(10)
      scope.stop()
    } finally {
      vi.useRealTimers()
    }
  })

  it('the index scan waits while a block fetch is pending', async () => {
    const { api, calls } = fakeApi(100)
    api.open = async () => ({
      documentId: 'doc1', blockCount: 50, knownBlockCount: 50, isIndexComplete: false,
      isRightToLeft: true, stylesheet: '', headingCount: 0, footnoteCount: 0, endnoteCount: 0,
    })
    let releaseBlocks: () => void = () => {}
    const blocksGate = new Promise<void>((resolve) => (releaseBlocks = resolve))
    let releaseFirstScan: () => void = () => {}
    const firstScanGate = new Promise<void>((resolve) => (releaseFirstScan = resolve))
    const fetchBlocks = api.blocks
    api.blocks = async (...args) => { await blocksGate; return fetchBlocks(...args) }
    api.scan = async (documentId, blocks) => {
      calls.push({ op: 'scan', args: [documentId, blocks] })
      const first = calls.filter((call) => call.op === 'scan').length === 1
      if (first) await firstScanGate
      return { knownBlockCount: first ? 60 : 100, isIndexComplete: !first, footnoteCount: 0, endnoteCount: 0 }
    }
    const scope = effectScope()
    const document = scope.run(() => useFastDocViewDocument({ api }))!
    await document.open('x')                      // the first scan call is now in flight
    const fetching = document.ensureBlocks(0, 10) // a block fetch starts meanwhile
    releaseFirstScan()
    await flush()
    await flush()
    expect(calls.filter((call) => call.op === 'scan').length).toBe(1)
    expect(document.blockCount.value).toBe(60)
    releaseBlocks()
    await fetching
    await flush()
    expect(calls.filter((call) => call.op === 'scan').length).toBe(2)
    expect(document.blockCount.value).toBe(100)
    scope.stop()
  })

  it('builds landmark tiers, weights and notes, and closes on dispose', async () => {
    const { api, calls } = fakeApi(100)
    const scope = effectScope()
    const document = scope.run(() => useFastDocViewDocument({ api }))!
    await document.open('x')
    await flush()
    expect(document.landmarkTiers.value).toEqual([[0, 20], [50]])
    expect(document.rowWeights.value?.length).toBe(100)
    expect([document.textWidthTwipsAt(0), document.textWidthTwipsAt(49), document.textWidthTwipsAt(50), document.textWidthTwipsAt(99)]).toEqual([9360, 9360, 13680, 13680])
    await document.ensureBlocks(0, 10)
    expect(document.notesInRange(0, 10).map((note) => note.label)).toEqual(['1'])
    scope.stop()
    await flush()
    expect(calls.some((call) => call.op === 'close' && call.args[0] === 'doc1')).toBe(true)
  })
})

describe('decodeFastDocViewWeights', () => {
  it('reads little-endian Int32 values', () => {
    expect([...decodeFastDocViewWeights(new Uint8Array([1, 0, 0, 0, 0, 1, 0, 0]))]).toEqual([1, 256])
  })
})
