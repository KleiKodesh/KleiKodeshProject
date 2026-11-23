// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Moving a tab between desktop windows (tabMove.ts): pack in the source page, unpack
 * in the target page, then the source closes its copy. The moved tab must arrive like
 * a tab restored after a restart - persisted fields only, its records under the new id.
 */

vi.mock('@/webview-host/bridge', () => ({
  hasNativeChromeTabs: true,
  navigatesDestinationsInPlace: false,
  disposeLocalFileHost: vi.fn(),
  getHbLocalFolderFromRegistry: vi.fn(async () => null),
  setHbLocalFolderInRegistry: vi.fn(async () => undefined),
}))
vi.mock('@/webview-host/seforimApi', () => ({ getWordLinkTargetsForBook: vi.fn(async () => null) }))
vi.mock('@/stores/recentlyOpenedStore', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/stores/recentlyOpenedStore')>()
  return { ...original, useRecentlyOpenedStore: () => ({ trackNavigation: vi.fn() }) }
})
const restoreTab = vi.hoisted(() => vi.fn(async () => undefined))
vi.mock('@/stores/localFileStore', () => ({ useLocalFileStore: () => ({ restoreTab }) }))
const pins = vi.hoisted(() => new Map<string, string>())
vi.mock('@/stores/searchCacheStore', () => ({
  useSearchCacheStore: () => ({
    pinnedKey: async (owner: string) => pins.get(owner) ?? null,
    pin: async (owner: string, key: string) => { pins.set(owner, key) },
    unpin: async (owner: string) => { pins.delete(owner) },
  }),
}))

const db = vi.hoisted(() => new Map<string, unknown>())
vi.mock('@/utils/persistence', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/utils/persistence')>()
  return {
    ...original,
    idbTabsGet: async (key: string) => db.get(key) ?? null,
    idbTabsSet: async (key: string, value: unknown) => { db.set(key, value) },
    idbTabsDelete: async (key: string) => { db.delete(key) },
    idbTabsDeleteByPrefix: async (prefix: string) => { for (const k of [...db.keys()]) if (k.startsWith(prefix)) db.delete(k) },
    idbTabsEntriesWithPrefix: async (prefix: string) => [...db].filter(([k]) => k.startsWith(prefix)),
    dbGet: async () => null,
    dbSet: async () => undefined,
    dbDelete: async () => undefined,
    dbCount: async () => 0,
    dbListEntries: async () => [],
  }
})

import { useTabStore } from '@/stores/tabStore'
import { useWorkspaceStore } from '@/stores/workspaceStore'
import { packTab, unpackTab, closeMovedTab, markLocalFileRestoreReady } from './tabMove'

function freshPage() {
  setActivePinia(createPinia())
  useWorkspaceStore().init()
  const store = useTabStore()
  store.init()
  return store
}

describe('moving a tab between windows', () => {
  beforeEach(() => {
    db.clear()
    pins.clear()
    restoreTab.mockClear()
    localStorage.clear()
  })

  it('a book tab keeps its place, gets a new id, and the source copy closes', async () => {
    const source = freshPage()
    const book = source.openTab({ title: 'b', route: '/book-view', bookId: 42, tocPath: 'x', openTocLineIndex: 5 })
    db.set(`book:default:${book.id}:42`, { scrollIndex: 300, scrollOffset: 12 })
    db.set(`tab:default:${book.id}`, { searchZoom: 110 })

    const moved = await packTab(book.id)
    expect(moved.entry.bookId).toBe(42)
    expect(moved.entry.tocPath).toBe('x')
    expect('openTocLineIndex' in moved.entry).toBe(false) // a pending jump never travels
    expect(moved.records.bookStates).toEqual([{ bookId: 42, state: { scrollIndex: 300, scrollOffset: 12 } }])

    // the target is another page (here: the same storage, so keys are compared by tab id)
    const target = freshPage()
    const before = target.tabs.map((t) => t.id)
    const newId = await unpackTab(JSON.parse(JSON.stringify(moved)), 0)
    expect(before).not.toContain(newId)
    expect(target.activeTabId).toBe(newId)
    expect(target.tabs[0]!.id).toBe(newId) // dropped first
    expect(db.get(`book:default:${newId}:42`)).toEqual({ scrollIndex: 300, scrollOffset: 12 })
    expect(db.get(`tab:default:${newId}`)).toEqual({ searchZoom: 110 })
  })

  it('a tab dropped without a position lands last', async () => {
    const source = freshPage()
    const tab = source.openTab({ title: 's', route: '/search', searchQuery: 'q' })
    const moved = await packTab(tab.id)
    const target = freshPage()
    const newId = await unpackTab(moved, -1)
    expect(target.tabs[target.tabs.length - 1]!.id).toBe(newId)
  })

  it('a search tab takes its pinned results along', async () => {
    const source = freshPage()
    const tab = source.openTab({ title: 's', route: '/search', searchQuery: 'q' })
    pins.set(`default:${tab.id}`, 'cache-key-q')
    const moved = await packTab(tab.id)
    expect(moved.searchCacheKey).toBe('cache-key-q')
    freshPage()
    const newId = await unpackTab(moved, -1)
    expect(pins.get(`default:${newId}`)).toBe('cache-key-q')
  })

  it('a local file tab reopens the way a restart reopens it', async () => {
    const source = freshPage()
    const tab = source.openTab({ title: 'f', route: '/pdf-view', localFilePath: 'C:\\a.pdf', localFileVirtualUrl: 'https://x/a.pdf' })
    const moved = await packTab(tab.id)
    expect('localFileVirtualUrl' in moved.entry).toBe(false)
    freshPage()
    markLocalFileRestoreReady()
    const newId = await unpackTab(moved, -1)
    expect(restoreTab).toHaveBeenCalledWith(newId)
  })

  it('closing the source copy after the hand-over leaves the target records alone', async () => {
    const source = freshPage()
    const book = source.openTab({ title: 'b', route: '/book-view', bookId: 42 })
    db.set(`book:default:${book.id}:42`, { scrollIndex: 3, scrollOffset: 0 })
    const moved = await packTab(book.id)
    const newId = await unpackTab(moved, -1)
    closeMovedTab(book.id)
    await Promise.resolve()
    expect(source.isOpenTab(book.id)).toBe(false)
    expect(db.has(`book:default:${book.id}:42`)).toBe(false)
    expect(db.get(`book:default:${newId}:42`)).toEqual({ scrollIndex: 3, scrollOffset: 0 })
  })

  it('a tab that is gone cannot be packed', async () => {
    freshPage()
    await expect(packTab('999')).rejects.toThrow()
  })
})
