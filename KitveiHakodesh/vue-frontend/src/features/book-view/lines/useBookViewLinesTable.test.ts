import { describe, it, expect, vi, beforeEach } from 'vitest'
import { effectScope, nextTick } from 'vue'

// A fake book: line i has id 1000+i and text `v<version>:<i>`. Every getLinesPaged call is logged.
const TOTAL = 10000
const calls: { offset: number; limit: number; version: number }[] = []
let gate: Promise<void> | null = null

vi.mock('@/webview-host/seforimApi', () => ({
  getBookById: vi.fn(async () => ({ totalLines: TOTAL })),
  getBookVersions: vi.fn(async () => []),
  getHebrewBooksEditions: vi.fn(async () => []),
  getLineCharCounts: vi.fn(async () => null),
  getLineIds: vi.fn(async (_id: number, start: number, end: number) =>
    Array.from({ length: end - start }, (_, i) => 1000 + start + i)),
  getLinesPaged: vi.fn(async (_id: number, limit: number, offset: number, version: number) => {
    calls.push({ offset, limit, version })
    if (gate) await gate
    const rows = []
    for (let i = offset; i < Math.min(TOTAL, offset + limit); i++) rows.push({ id: 1000 + i, lineIndex: i, content: `v${version}:${i}` })
    return rows
  }),
}))

import { useLines } from './useBookViewLinesTable'

const flush = async () => { for (let i = 0; i < 5; i++) { await Promise.resolve(); await nextTick() } }

async function open(options: Parameters<typeof useLines>[1] = {}) {
  const scope = effectScope()
  const api = scope.run(() => useLines(() => 7, options))!
  await flush()
  return { api, scope }
}

beforeEach(() => { calls.length = 0; gate = null })

describe('useLines', () => {
  it('fetches only the missing lines, at least 200 per request, toward the missing side', async () => {
    const { api } = await open()
    await api.ensureLines(1000, 1050)
    await api.ensureLines(1100, 1150) // already fetched with the first run
    await api.ensureLines(1150, 1300)
    await api.ensureLines(950, 1000) // grows backward, into the missing lines
    expect(calls).toEqual([
      { offset: 1000, limit: 200, version: 0 },
      { offset: 1200, limit: 200, version: 0 },
      { offset: 800, limit: 200, version: 0 },
    ])
    expect(api.lines.value.contentAt(1299)).toBe('v0:1299')
  })

  it('dedupes overlapping ranges in flight onto one request', async () => {
    const { api } = await open()
    let release!: () => void
    gate = new Promise((resolve) => { release = resolve })
    const first = api.ensureLines(0, 100)
    const second = api.ensureLines(50, 100)
    release()
    await Promise.all([first, second])
    expect(calls).toHaveLength(1)
  })

  it('keeps only the window + margin and retained ranges; reports evicted ids', async () => {
    const evicted: number[] = []
    const { api } = await open({ retainedRanges: () => [[10, 20]], onTextEvicted: (ids) => evicted.push(...ids) })
    await api.ensureLines(0, 100)
    await api.ensureLines(5000, 5100)
    const table = api.lines.value
    expect(table.hasText(15)).toBe(true) // retained
    expect(table.hasText(50)).toBe(false) // far from the window
    expect(table.hasText(5050)).toBe(true)
    expect(evicted).toContain(1050)
    expect(evicted).not.toContain(1015)
    expect(table.idAt(50)).toBe(1050) // ids stay
  })

  it('discards a fetch that lands after a version swap and refetches under the new one', async () => {
    const { api } = await open()
    await api.ensureLines(0, 10)
    let release!: () => void
    gate = new Promise((resolve) => { release = resolve })
    const stale = api.ensureLines(500, 510).catch(() => 'rejected')
    api.setVersion(428)
    // Old text stays shown, but is no longer loaded.
    expect(api.lines.value.contentAt(5)).toBe('v0:5')
    expect(api.lines.value.hasText(5)).toBe(false)
    release()
    expect(await stale).toBe('rejected')
    expect(api.lines.value.contentAt(505)).toBeNull()
    gate = null
    await api.ensureLines(0, 20)
    expect(api.lines.value.contentAt(5)).toBe('v428:5')
    expect(api.lines.value.contentAt(150)).toBe('v428:150')
    expect(calls.at(-1)).toEqual({ offset: 0, limit: 200, version: 428 })
  })

  it('readLines returns a range without keeping it in the table', async () => {
    const { api } = await open()
    await api.ensureLines(0, 10)
    calls.length = 0
    const rows = await api.readLines(0, TOTAL)
    expect(rows).toHaveLength(TOTAL)
    expect(rows[9]).toEqual({ id: 1009, lineIndex: 9, content: 'v0:9' })
    expect(rows[TOTAL - 1]!.content).toBe(`v0:${TOTAL - 1}`)
    expect(calls[0]!.offset).toBe(200) // loaded lines are not fetched again
    expect(api.lines.value.loadedCount).toBe(200)
  })

  it('readLines rejects when the version changes under it', async () => {
    const { api } = await open()
    let release!: () => void
    gate = new Promise((resolve) => { release = resolve })
    const reading = api.readLines(0, 100)
    api.setVersion(430)
    release()
    await expect(reading).rejects.toThrow()
  })
})
