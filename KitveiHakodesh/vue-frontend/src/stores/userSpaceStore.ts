/**
 * UserSpace's root folders - the ONLY thing it stores. Each root is a catalog
 * category or a folder on the computer; everything under a root is read live when the
 * user opens it (the catalog from booksDataStore, the computer from the service), so a
 * root never goes stale and the stored list stays a handful of tiny records.
 *
 * Only this store may access the `app-user-space` IDB database.
 */
import { defineStore } from 'pinia'
import { ref } from 'vue'
import { dbGet, dbSet } from '@/utils/persistence'
import { folderDisplayName } from '@/utils/filePath'
import { claimDefaultUserSpaceFolders } from '@/webview-host/bridge'

export type UserSpaceRoot =
  | { kind: 'catalog'; categoryId: number; title: string }
  | { kind: 'computer'; path: string; title: string }
  /** The whole catalog - every shelf the catalog page browses. */
  | { kind: 'catalogAll'; title: string }

const USER_SPACE_DB = 'app-user-space'
const ROOTS_KEY = 'roots'

/** A root's identity: no two roots point at the same category or the same folder. */
export function userSpaceRootKey(root: UserSpaceRoot): string {
  if (root.kind === 'catalogAll') return 'catalog:all'
  return root.kind === 'catalog' ? `catalog:${root.categoryId}` : `computer:${root.path.toLowerCase()}`
}

export const useUserSpaceStore = defineStore('userSpace', () => {
  const roots = ref<UserSpaceRoot[]>([])
  const loaded = ref(false)
  let loading: Promise<void> | null = null

  function ensureLoaded(): Promise<void> {
    if (loaded.value) return Promise.resolve()
    loading ??= dbGet<UserSpaceRoot[]>(USER_SPACE_DB, ROOTS_KEY)
      .then((stored) => {
        roots.value = stored ?? []
      })
      .catch(() => {
        // An unreadable list is an empty one; the next add writes a fresh copy.
        roots.value = []
      })
      .finally(() => {
        loaded.value = true
        loading = null
      })
    return loading
  }

  function persist() {
    dbSet(USER_SPACE_DB, ROOTS_KEY, roots.value.map((r) => ({ ...r }))).catch(() => {})
  }

  /** Adds a root at the end. False when it is already there. */
  function add(root: UserSpaceRoot): boolean {
    const key = userSpaceRootKey(root)
    if (roots.value.some((r) => userSpaceRootKey(r) === key)) return false
    roots.value = [...roots.value, root]
    persist()
    return true
  }

  function addCatalogCategory(categoryId: number, title: string): boolean {
    return add({ kind: 'catalog', categoryId, title })
  }

  function addWholeCatalog(title: string): boolean {
    return add({ kind: 'catalogAll', title })
  }

  function addComputerFolder(path: string): boolean {
    return add({ kind: 'computer', path, title: folderDisplayName(path) })
  }

  /**
   * Adds the default computer folders (the Torat Emet books, when installed). The service hands each folder out ONCE, so one the
   * user removed on purpose does not come back; one added by hand is not added twice (`add` ignores a duplicate). Never throws.
   */
  async function claimDefaultFolders(): Promise<void> {
    await ensureLoaded()
    const folders = await claimDefaultUserSpaceFolders()
    for (const folder of folders) addComputerFolder(folder)
  }

  function remove(key: string) {
    roots.value = roots.value.filter((r) => userSpaceRootKey(r) !== key)
    persist()
  }

  return { roots, loaded, ensureLoaded, addCatalogCategory, addWholeCatalog, addComputerFolder, claimDefaultFolders, remove }
})
