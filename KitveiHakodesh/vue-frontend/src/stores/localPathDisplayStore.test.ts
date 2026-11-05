import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const calls = vi.hoisted(() => ({ batches: [] as { path: string; isFolder: boolean }[][], fail: false }))
vi.mock('@/webview-host/bridge', () => ({
  displayLocalPaths: async (items: { path: string; isFolder: boolean }[]) => {
    calls.batches.push(items)
    if (calls.fail) throw new Error('down')
    return items.map((i) => (i.path.includes('Books') ? { title: 'T:' + i.path, path: 'P:' + i.path } : { title: '', path: '' }))
  },
}))
vi.mock('@/stores/booksDataStore', () => ({ useBooksDataStore: () => ({ allBooksMap: new Map() }) }))

import { useLocalPathDisplayStore } from './localPathDisplayStore'
import { catalogBookTitle } from './catalogBookTitle'

const BOOK = 'C:/x/ToratEmetInstall/Books/001_TORA/a01_Genesis.txt'
const DIR = 'C:/x/ToratEmetInstall/Books/001_TORA'

describe('local path display', () => {
  beforeEach(() => { setActivePinia(createPinia()); calls.batches.length = 0; calls.fail = false })

  it('asks once for all visible entries and only for Torat Emet paths', async () => {
    const s = useLocalPathDisplayStore()
    await s.request([{ path: BOOK, isFolder: false }, { path: DIR, isFolder: true }, { path: 'C:/Docs/a.pdf', isFolder: false }, { path: undefined, isFolder: false }])
    expect(calls.batches).toHaveLength(1)
    expect(calls.batches[0]).toHaveLength(2)
    expect(s.titleOf(BOOK, false)).toBe('T:' + BOOK)
    expect(s.pathOf(DIR, true)).toBe('P:' + DIR)
    expect(s.titleOf('C:/Docs/a.pdf', false)).toBe('')
    await s.request([{ path: BOOK, isFolder: false }])
    expect(calls.batches).toHaveLength(1)
  })

  it('asks again after a failure, shows stored values meanwhile', async () => {
    const s = useLocalPathDisplayStore()
    calls.fail = true
    await s.request([{ path: BOOK, isFolder: false }])
    expect(s.titleOf(BOOK, false)).toBe('')
    calls.fail = false
    await s.request([{ path: BOOK, isFolder: false }])
    expect(s.titleOf(BOOK, false)).toBe('T:' + BOOK)
  })

  it('catalogBookTitle shows the Hebrew title of a local file, keeps the stored one otherwise', async () => {
    const s = useLocalPathDisplayStore()
    const entry = { route: '/fast-doc-view', title: 'a01_Genesis.txt', localFilePath: BOOK }
    expect(catalogBookTitle(entry)).toBe('a01_Genesis.txt')
    await s.request([{ path: BOOK, isFolder: false }])
    expect(catalogBookTitle(entry)).toBe('T:' + BOOK)
  })
})
