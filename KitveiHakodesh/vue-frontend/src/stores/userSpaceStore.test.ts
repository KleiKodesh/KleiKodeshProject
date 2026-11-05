import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import appResetSource from '@/features/settings/appResetState.ts?raw'
import dbSwitchSource from '@/webview-host/dbSwitchCleanup.ts?raw'

// The default Torat Emet folders: added once, remembered in the roots' own database, back after a reset.
const db = vi.hoisted(() => ({ data: new Map<string, unknown>(), found: [] as string[], covered: true, throws: false }))
vi.mock('@/utils/persistence', () => ({
  dbGet: async (_db: string, key: string) => db.data.get(key) ?? null,
  dbSet: async (_db: string, key: string, value: unknown) => { db.data.set(key, value) },
}))
vi.mock('@/webview-host/bridge', () => ({
  claimDefaultUserSpaceFolders: async () => db.found,
  checkUserSpaceFolder: async () => {
    if (db.throws) throw new Error('service down')
    return { covered: db.covered, reason: db.covered ? '' : 'notIndexedDrive' }
  },
}))

import { useUserSpaceStore } from './userSpaceStore'

const BOOKS = 'C:\\Docs\\ToratEmetInstall\\Books'
const MY = 'C:\\Docs\\ToratEmetUserData\\MyBooks'
const fresh = () => { setActivePinia(createPinia()); return useUserSpaceStore() }
const paths = (store: ReturnType<typeof useUserSpaceStore>) => store.roots.map((r) => (r as { path: string }).path)

describe('default Torat Emet folders', () => {
  beforeEach(() => { db.data.clear(); db.found = [BOOKS, MY]; db.covered = true; db.throws = false })

  it('adds both once, then not again after a restart', async () => {
    let store = fresh()
    await store.claimDefaultFolders()
    expect(paths(store)).toEqual([BOOKS, MY])
    store = fresh()
    await store.claimDefaultFolders()
    expect(store.roots.length).toBe(2)
  })

  it('a folder removed by hand does not come back', async () => {
    let store = fresh()
    await store.claimDefaultFolders()
    store.remove(`computer:${MY.toLowerCase()}`)
    store = fresh()
    await store.claimDefaultFolders()
    expect(paths(store)).toEqual([BOOKS])
  })

  it('a reset (the roots database wiped) brings them back', async () => {
    let store = fresh()
    await store.claimDefaultFolders()
    store.remove(`computer:${MY.toLowerCase()}`)
    db.data.clear()   // resetEverything / the pending-reset recovery drop the whole app-user-space database
    store = fresh()
    await store.claimDefaultFolders()
    expect(store.roots.length).toBe(2)
  })

  it('a folder added by hand is not added twice', async () => {
    const store = fresh()
    await store.ensureLoaded()
    store.addComputerFolder(BOOKS)
    await store.claimDefaultFolders()
    expect(paths(store).filter((p) => p === BOOKS).length).toBe(1)
  })

  it('a folder file search does not cover is rejected, not remembered, and checked again next time', async () => {
    db.covered = false
    let store = fresh()
    const rejected = await store.claimDefaultFolders()
    expect(rejected.length).toBe(2)
    expect(store.roots.length).toBe(0)
    db.covered = true
    store = fresh()
    await store.claimDefaultFolders()
    expect(store.roots.length).toBe(2)
  })
})

// The reset paths and what each clears (the database holds the roots AND the memory of the defaults):
//  - resetEverything (settings reset) and checkAndExecPendingReset (crash recovery) drop 'app-user-space' -> defaults return;
//  - the dev reset runs the same frontend wipe;
//  - the library switch (dbSwitchCleanup) keeps local-file data, so roots and the memory both stay.
describe('robustness', () => {
  beforeEach(() => { db.data.clear(); db.found = [BOOKS, MY]; db.covered = true; db.throws = false })

  it('a failing coverage check adds nothing, throws nothing, and is retried next time', async () => {
    db.throws = true
    let store = fresh()
    await expect(store.claimDefaultFolders()).resolves.toEqual([])
    expect(store.roots.length).toBe(0)
    db.throws = false
    store = fresh()
    await store.claimDefaultFolders()
    expect(store.roots.length).toBe(2)
  })

  it('two claims at once add each folder once and remember it once', async () => {
    const store = fresh()
    await Promise.all([store.claimDefaultFolders(), store.claimDefaultFolders()])
    expect(store.roots.length).toBe(2)
    expect((db.data.get('defaultsAdded') as string[]).length).toBe(2)
  })
})

describe('reset paths', () => {
  it('the full reset drops the roots database', () => {
    expect(appResetSource).toContain("'app-user-space'")
  })
  it('a library switch keeps it', () => {
    expect(dbSwitchSource).not.toContain('app-user-space')
  })
})
