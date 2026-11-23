// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'

/**
 * A tab moved to another desktop window carries its per-tab records (tabMove.ts):
 * read under the source window's storage id, written under the target's. These pin
 * down the keys on both sides.
 */

const db = vi.hoisted(() => new Map<string, unknown>())
const scope = vi.hoisted(() => ({ storageId: 'default' }))

vi.mock('@/utils/persistence', () => ({
  idbTabsGet: async (key: string) => db.get(key) ?? null,
  idbTabsSet: async (key: string, value: unknown) => { db.set(key, value) },
  idbTabsDelete: async (key: string) => { db.delete(key) },
  idbTabsDeleteByPrefix: async (prefix: string) => { for (const k of [...db.keys()]) if (k.startsWith(prefix)) db.delete(k) },
  idbTabsEntriesWithPrefix: async (prefix: string) => [...db].filter(([k]) => k.startsWith(prefix)),
}))
vi.mock('./workspaceStore', () => ({ useWorkspaceStore: () => scope }))
vi.mock('./searchCacheStore', () => ({ useSearchCacheStore: () => ({ unpin: async () => {} }) }))

import {
  readAllStateForTab,
  writeAllStateForTab,
  peekBookViewState,
  peekTabViewState,
  setBookViewState,
} from './tabStatePersistence'

describe('per-tab records travelling with a moved tab', () => {
  beforeEach(() => {
    db.clear()
    scope.storageId = 'default'
  })

  it('reads the tab record and every book record of that tab only', async () => {
    db.set('tab:default:3', { searchZoom: 120 })
    db.set('book:default:3:42', { scrollIndex: 10, scrollOffset: 5 })
    db.set('book:default:3:7', { scrollIndex: 1, scrollOffset: 0 })
    db.set('book:default:33:42', { scrollIndex: 99, scrollOffset: 0 }) // tab 33, not 3
    db.set('book:default@1:3:42', { scrollIndex: 77, scrollOffset: 0 }) // another window
    const records = await readAllStateForTab('3')
    expect(records.tabState).toEqual({ searchZoom: 120 })
    expect(records.bookStates.sort((a, b) => a.bookId - b.bookId)).toEqual([
      { bookId: 7, state: { scrollIndex: 1, scrollOffset: 0 } },
      { bookId: 42, state: { scrollIndex: 10, scrollOffset: 5 } },
    ])
  })

  it('a position saved a moment ago wins over the stored one', async () => {
    db.set('book:default:4:42', { scrollIndex: 10, scrollOffset: 0 })
    void setBookViewState('4', 42, { scrollIndex: 500, scrollOffset: 3 })
    const records = await readAllStateForTab('4')
    expect(records.bookStates).toEqual([{ bookId: 42, state: { scrollIndex: 500, scrollOffset: 3 } }])
  })

  it('writes under the target window storage id with the new tab id, readable at once', async () => {
    scope.storageId = 'default@2'
    const pending = writeAllStateForTab('9', {
      tabState: { htmlViewScrollTop: 300 },
      bookStates: [{ bookId: 42, state: { scrollIndex: 10, scrollOffset: 5 } }],
    })
    // seeded synchronously: a view mounting right away finds its place
    expect(peekBookViewState('9', 42)).toEqual({ scrollIndex: 10, scrollOffset: 5 })
    expect(peekTabViewState('9')).toEqual({ htmlViewScrollTop: 300 })
    await pending
    expect([...db.keys()].sort()).toEqual(['book:default@2:9:42', 'tab:default@2:9'])
  })
})
