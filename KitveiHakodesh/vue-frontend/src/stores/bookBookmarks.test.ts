import { describe, it, expect, vi, beforeEach } from 'vitest'

// A tiny in-memory user_bookmarks table behind the two user-settings calls.
type Row = Record<string, unknown>
const table: Row[] = []
const calls: string[] = []

vi.mock('@/webview-host/userSettingsDb', () => ({
  queryUserSettings: vi.fn(async (_sql: string, params: unknown[]) =>
    table.filter((r) => r.owner === params[0]).map(({ owner: _o, ...rest }) => ({ ...rest }))),
  executeUserSettings: vi.fn(async (sql: string, p: unknown[]) => {
    if (sql.includes('DELETE')) {
      calls.push('delete')
      const i = table.findIndex((r) => r.owner === p[0] && r.bookmarkId === p[1])
      if (i >= 0) table.splice(i, 1)
      return 0
    }
    calls.push('upsert')
    const cols = ['owner', 'bookmarkId', 'lineId', 'lineIndex', 'letterOffset', 'selectionLetterCount', 'label', 'tocPath', 'snippet', 'pdfTop']
    const row: Row = Object.fromEntries(cols.map((c, i) => [c, p[i]]))
    const i = table.findIndex((r) => r.owner === p[0] && r.bookmarkId === p[1])
    if (i >= 0) table[i] = row
    else table.push(row)
    return table.length
  }),
}))

const store = await import('./bookBookmarks')

describe('bookBookmarks (user_settings.db)', () => {
  beforeEach(() => { table.length = 0; calls.length = 0 })

  it('saves, reloads, edits only changed rows, deletes, and keys PDFs by path', async () => {
    const a = { id: 2, lineId: 10, lineIndex: 5, label: 'A', letterOffset: 3 }
    const b = { id: 1, lineId: 11, lineIndex: 9, label: 'B', snippet: 's' }
    const saved = await store.updateBookBookmarks(42, () => [b, a])
    expect(saved.map((x) => x.id)).toEqual([2, 1])
    expect(store.loadedBookBookmarks(42)).toEqual(saved)
    expect(table.every((r) => r.owner === '42')).toBe(true)

    calls.length = 0
    await store.updateBookBookmarks(42, (list) => list.map((x) => (x.id === 1 ? { ...x, label: 'B2' } : x)))
    expect(calls).toEqual(['upsert'])
    expect(table.find((r) => r.bookmarkId === 1)?.label).toBe('B2')

    await store.updateBookBookmarks(42, (list) => list.filter((x) => x.id !== 2))
    expect(table.map((r) => r.bookmarkId)).toEqual([1])

    const pdf = store.pdfBookmarksKey('C:/X/a.pdf')
    await store.updateBookBookmarks(pdf, () => [{ id: 7, lineId: 3, lineIndex: 2, label: 'p', pdfTop: 500 }])
    expect(table.find((r) => r.owner === 'pdf:c:/x/a.pdf')?.pdfTop).toBe(500)
  })

  it('loads a stored list once, dropping null columns', async () => {
    table.push({ owner: '9', bookmarkId: 5, lineId: 1, lineIndex: 1, letterOffset: null, selectionLetterCount: null, label: 'L', tocPath: null, snippet: null, pdfTop: null })
    await store.loadBookBookmarks(9)
    expect(store.loadedBookBookmarks(9)).toEqual([{ id: 5, lineId: 1, lineIndex: 1, label: 'L' }])
  })
})
