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
import { checkUserSpaceFolder, claimDefaultUserSpaceFolders, userSpaceRootTitles, type UserSpaceFolderReason } from '@/webview-host/bridge'

export type UserSpaceRoot =
  | { kind: 'catalog'; categoryId: number; title: string }
  | { kind: 'computer'; path: string; title: string }
  /** The whole catalog - every shelf the catalog page browses. */
  | { kind: 'catalogAll'; title: string }

const USER_SPACE_DB = 'app-user-space'
const ROOTS_KEY = 'roots'
/**
 * Default folders already added (lower-cased paths). Kept in the SAME database as the roots on purpose: every reset that clears
 * the roots clears this too, so the defaults come back after a reset - and a folder the user removes by hand stays removed
 * (it is still listed here) until one.
 */
const DEFAULTS_ADDED_KEY = 'defaultsAdded'

/** A root's identity: no two roots point at the same category or the same folder. */
export function userSpaceRootKey(root: UserSpaceRoot): string {
  if (root.kind === 'catalogAll') return 'catalog:all'
  return root.kind === 'catalog' ? `catalog:${root.categoryId}` : `computer:${root.path.toLowerCase()}`
}

export const useUserSpaceStore = defineStore('userSpace', () => {
  const roots = ref<UserSpaceRoot[]>([])
  const loaded = ref(false)
  let defaultsAdded: string[] = []
  /** Hebrew names Core gives the default roots, by lower-cased folder path (see rootTitle). */
  const coreTitles = ref<Record<string, string>>({})
  let loading: Promise<void> | null = null

  function ensureLoaded(): Promise<void> {
    if (loaded.value) return Promise.resolve()
    loading ??= Promise.all([
      dbGet<UserSpaceRoot[]>(USER_SPACE_DB, ROOTS_KEY),
      // Separate fallbacks: an unreadable memory of the defaults must not blank the roots (the next add would overwrite them).
      dbGet<string[]>(USER_SPACE_DB, DEFAULTS_ADDED_KEY).catch(() => null),
    ])
      .then(([stored, added]) => {
        roots.value = stored ?? []
        defaultsAdded = added ?? []
      })
      .catch(() => {
        // An unreadable list is an empty one; the next add writes a fresh copy.
        roots.value = []
      })
      .finally(() => {
        loaded.value = true
        loading = null
        void resolveRootTitles()
      })
    return loading
  }

  /** Asks Core for the names of the computer roots it has not been asked about yet. Never throws. */
  async function resolveRootTitles(): Promise<void> {
    const paths = roots.value.flatMap((r) => (r.kind === 'computer' && !(r.path.toLowerCase() in coreTitles.value) ? [r.path] : []))
    if (!paths.length) return
    let titles: string[]
    try { titles = await userSpaceRootTitles(paths) } catch { return }   // service not ready: not remembered, asked again next time
    const next = { ...coreTitles.value }
    paths.forEach((p, i) => { next[p.toLowerCase()] = titles[i] ?? '' })
    coreTitles.value = next
  }

  /**
   * The name a root shows: the Hebrew name Core gives a default root (Torat Emet), unless the stored title is no longer the
   * folder's own name (the root was renamed); every other root shows its stored title.
   */
  function rootTitle(root: UserSpaceRoot): string {
    if (root.kind !== 'computer') return root.title
    const own = coreTitles.value[root.path.toLowerCase()]
    return own && root.title === folderDisplayName(root.path) ? own : root.title
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
    if (root.kind === 'computer') void resolveRootTitles()
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
   * Adds the default computer folders (the Torat Emet books, when installed), each ONCE: the ones already added before are
   * remembered in the roots' own database (see DEFAULTS_ADDED_KEY), so a folder the user removed by hand does not come back until
   * a reset clears that database. A folder the file search does not cover is treated like a user-added one: not added (and not
   * remembered, so it is checked again next time); it is returned so a page can show the same notice. One the user added by hand
   * is not added twice. Never throws.
   */
  async function claimDefaultFolders(): Promise<{ folder: string; reason: UserSpaceFolderReason }[]> {
    await ensureLoaded()
    const rejected: { folder: string; reason: UserSpaceFolderReason }[] = []
    let changed = false
    for (const folder of await claimDefaultUserSpaceFolders()) {
      const key = folder.toLowerCase()
      if (defaultsAdded.includes(key)) continue
      let check: Awaited<ReturnType<typeof checkUserSpaceFolder>>
      try {
        check = await checkUserSpaceFolder(folder)
      } catch {
        continue   // unknown: not added, not remembered - checked again next time
      }
      if (!check.covered) {
        rejected.push({ folder, reason: check.reason })
        continue
      }
      addComputerFolder(folder)   // false = already a root: remembered all the same
      if (!defaultsAdded.includes(key)) defaultsAdded = [...defaultsAdded, key]   // a concurrent claim may have added it meanwhile
      changed = true
    }
    if (changed) dbSet(USER_SPACE_DB, DEFAULTS_ADDED_KEY, [...defaultsAdded]).catch(() => {})
    return rejected
  }

  function remove(key: string) {
    roots.value = roots.value.filter((r) => userSpaceRootKey(r) !== key)
    persist()
  }

  return { roots, loaded, ensureLoaded, rootTitle, addCatalogCategory, addWholeCatalog, addComputerFolder, claimDefaultFolders, remove }
})
