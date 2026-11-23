// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

/**
 * The desktop app can show several windows; each keeps its own tabs under
 * `<workspace>@<window>` (windowScope.ts). These pin down which keys a window reads,
 * which a closing window deletes, and that deleting a workspace reaches every window.
 */

const removed = vi.hoisted(() => ({ idbPrefixes: [] as string[], pins: [] as string[], recents: [] as string[] }))

vi.mock('@/utils/persistence', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/utils/persistence')>()
  return {
    ...original,
    idbTabsDeleteByPrefix: async (prefix: string) => { removed.idbPrefixes.push(prefix) },
  }
})
vi.mock('./searchCacheStore', () => ({
  useSearchCacheStore: () => ({ unpinAllWithPrefix: async (prefix: string) => { removed.pins.push(prefix) } }),
}))
vi.mock('./recentLocations', () => ({
  deleteRecentLocations: async (id: string) => { removed.recents.push(id) },
}))

const LS = 'kitvei-hakodesh.'

// windowScope reads the host-injected id once, at import - so each window is a fresh import.
async function storeForWindow(windowId: string | undefined) {
  vi.resetModules()
  if (windowId === undefined) delete window.__webviewWindowId
  else window.__webviewWindowId = windowId
  const { useWorkspaceStore } = await import('./workspaceStore')
  setActivePinia(createPinia())
  const store = useWorkspaceStore()
  store.init()
  return store
}

function seedTwoWorkspacesInTwoWindows() {
  localStorage.setItem(LS + 'workspaces.list', JSON.stringify({
    workspaces: [{ id: 'default', name: 'a', createdAt: 1 }, { id: 'ws2', name: 'b', createdAt: 2 }],
    activeId: 'default',
  }))
  for (const key of ['tabs:default', 'tabs:default@1', 'tabs:ws2', 'tabs:ws2@1'])
    localStorage.setItem(LS + key, JSON.stringify({ tabs: [], activeTabId: '', nextId: 1 }))
}

const tabLists = () => Object.keys(localStorage).filter((k) => k.startsWith(LS + 'tabs:')).map((k) => k.slice(LS.length)).sort()

describe('workspace storage per desktop window', () => {
  beforeEach(() => {
    localStorage.clear()
    removed.idbPrefixes.length = 0
    removed.pins.length = 0
    removed.recents.length = 0
  })

  it('the first window (and every other host) uses the plain workspace id', async () => {
    seedTwoWorkspacesInTwoWindows()
    expect((await storeForWindow(undefined)).storageId).toBe('default')
    expect((await storeForWindow('')).storageId).toBe('default')
  })

  it('another window stores under workspace@window', async () => {
    seedTwoWorkspacesInTwoWindows()
    expect((await storeForWindow('1')).storageId).toBe('default@1')
  })

  it('switching workspace in another window leaves the first window its own choice', async () => {
    seedTwoWorkspacesInTwoWindows()
    const second = await storeForWindow('1')
    await second.switchWorkspace('ws2')
    expect(second.storageId).toBe('ws2@1')
    expect(JSON.parse(localStorage.getItem(LS + 'workspaces.list')!).activeId).toBe('default')
    // and it comes back on that window's next start
    expect((await storeForWindow('1')).storageId).toBe('ws2@1')
  })

  it('a window closing for good deletes only its own data, in every workspace', async () => {
    seedTwoWorkspacesInTwoWindows()
    const second = await storeForWindow('1')
    await second.discardWindowData()
    expect(tabLists()).toEqual(['tabs:default', 'tabs:ws2'])
    expect(removed.idbPrefixes.sort()).toEqual(['book:default@1:', 'book:ws2@1:', 'tab:default@1:', 'tab:ws2@1:'])
    expect(removed.pins.sort()).toEqual(['default@1:', 'ws2@1:'])
    expect(removed.recents.sort()).toEqual(['default@1', 'ws2@1'])
  })

  it('the first window closing for good deletes the plain keys and leaves other windows', async () => {
    seedTwoWorkspacesInTwoWindows()
    const first = await storeForWindow('')
    await first.discardWindowData()
    expect(tabLists()).toEqual(['tabs:default@1', 'tabs:ws2@1'])
  })

  it('deleting a workspace removes it from every window', async () => {
    seedTwoWorkspacesInTwoWindows()
    const first = await storeForWindow('')
    await first.deleteWorkspace('ws2')
    expect(tabLists()).toEqual(['tabs:default', 'tabs:default@1'])
    expect(removed.recents.sort()).toEqual(['ws2', 'ws2@1'])
  })

  it('a workspace deleted in one window is gone for a window that still has it in memory', async () => {
    seedTwoWorkspacesInTwoWindows()
    const second = await storeForWindow('1')
    const first = await storeForWindow('')
    await first.deleteWorkspace('ws2')
    expect(second.workspaces.some((w) => w.id === 'ws2')).toBe(true) // its stale copy
    expect(second.existsInSharedList('ws2')).toBe(false)
    expect(second.existsInSharedList('default')).toBe(true)
  })

  it('a workspace another window created is not lost when this window changes the list', async () => {
    seedTwoWorkspacesInTwoWindows()
    const first = await storeForWindow('')
    const second = await storeForWindow('1')
    await second.createWorkspace('from window 1')
    await first.renameWorkspace('default', 'renamed')
    const names = JSON.parse(localStorage.getItem(LS + 'workspaces.list')!).workspaces.map((w: { name: string }) => w.name)
    expect(names).toEqual(['renamed', 'b', 'from window 1'])
  })
})
