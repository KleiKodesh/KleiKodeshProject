// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

// The host bridge probes window.__webviewAction at module load; a native-tabs host is
// what makes openTab add a tab (the single-tab path navigates in place instead).
vi.mock('@/webview-host/bridge', () => ({
  hasNativeChromeTabs: true,
  navigatesDestinationsInPlace: false,
  disposeLocalFileHost: vi.fn(),
  getHbLocalFolderFromRegistry: vi.fn(async () => null),
  setHbLocalFolderInRegistry: vi.fn(async () => undefined),
}))
vi.mock('@/webview-host/seforimApi', () => ({
  getWordLinkTargetsForBook: vi.fn(async () => null),
}))
// Opening a book tab also records it in the recently-opened list, which lives in its
// own IndexedDB — not present under jsdom, and not what this test is about.
vi.mock('./recentlyOpenedStore', async (importOriginal) => {
  const original = await importOriginal<typeof import('./recentlyOpenedStore')>()
  return { ...original, useRecentlyOpenedStore: () => ({ trackNavigation: vi.fn() }) }
})

const idb = vi.hoisted(() => ({ puts: [] as string[], deletes: [] as string[] }))
vi.mock('@/utils/persistence', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/utils/persistence')>()
  return {
    ...original,
    idbTabsGet: async () => null,
    idbTabsSet: async (key: string) => { idb.puts.push(key) },
    idbTabsDelete: async (key: string) => { idb.deletes.push(key) },
    idbTabsDeleteByPrefix: async (prefix: string) => { idb.deletes.push(prefix) },
    dbGet: async () => null,
    dbSet: async () => undefined,
    dbDelete: async () => undefined,
    dbCount: async () => 0,
    dbListEntries: async () => [],
  }
})

import { useTabStore } from './tabStore'

/**
 * A closing tab's view runs one last position save after the tab has left the list.
 * By then the close has deleted the tab's state, so that save must write nothing —
 * otherwise it leaves an orphan `book:` record behind in app-tabs.
 */
describe('tabStore position saves for a closed tab', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    idb.puts.length = 0
    idb.deletes.length = 0
  })

  it('writes the per-tab book state while the tab is open', async () => {
    const store = useTabStore()
    const tab = store.openTab({ route: '/book-view', title: 'book', bookId: 7 })
    await store.setBookViewState(tab.id, 7, { scrollIndex: 3, scrollOffset: 0 })
    expect(idb.puts).toEqual([`book:default:${tab.id}:7`])
  })

  it('drops the save that follows the tab close instead of resurrecting its state', async () => {
    const store = useTabStore()
    const tab = store.openTab({ route: '/book-view', title: 'book', bookId: 7 })
    await store.setBookViewState(tab.id, 7, { scrollIndex: 3, scrollOffset: 0 })
    idb.puts.length = 0

    store.closeTab(tab.id)
    expect(idb.deletes).toContain(`book:default:${tab.id}:`)

    await store.setBookViewState(tab.id, 7, { scrollIndex: 9, scrollOffset: 0 })
    await store.setTabViewState(tab.id, { searchZoom: 120 })
    expect(idb.puts).toEqual([])
  })
})
