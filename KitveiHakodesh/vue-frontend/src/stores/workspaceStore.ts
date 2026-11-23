import { defineStore } from 'pinia'
import { ref, computed, toRaw } from 'vue'
import { useSearchCacheStore } from './searchCacheStore'
import { deleteRecentLocations } from './recentLocations'
import { lsGet, lsSet, lsDelete, lsKeys, idbTabsDeleteByPrefix } from '@/utils/persistence'
import { windowId, windowScopedId } from './windowScope'
import { t } from '@/i18n'

/**
 * Disk names owned here. `tabsList` is exported because `tabStore` writes the list
 * this store's teardown deletes — it lives here rather than in `tabStore` because
 * the key is workspace-scoped, and because `tabStore` already imports this module
 * (the reverse would close a cycle).
 */
const KEYS = {
  SETTINGS_WORKSPACES: 'workspaces.list',
  // The active workspace of a window other than the first; the first window's is
  // `activeId` inside the shared list, as before there were several windows.
  windowActive: (win: string) => `workspaces.active@${win}`,
} as const
/** Takes a STORAGE id (`windowScopedId`), not a bare workspace id. */
export const tabsListKey = (storageId: string) => `tabs:${storageId}`

export interface Workspace {
  id: string
  name: string
  createdAt: number
}

export interface WorkspaceList {
  workspaces: Workspace[]
  activeId: string
}

/**
 * Forget everything persisted under one storage id (a workspace in one window, see
 * `windowScopedId`): its tab list (localStorage, removed synchronously) and every
 * per-tab and per-book state beneath it (app-tabs), its search-cache pins and recents.
 *
 * Lives here rather than in the storage driver because the `tab:` / `book:` key
 * shapes and the fact that a workspace spans two storage systems are app knowledge,
 * not storage mechanics.
 */
function deleteStorageIdData(storageId: string): Promise<void> {
  lsDelete(tabsListKey(storageId))
  return Promise.all([
    idbTabsDeleteByPrefix(`tab:${storageId}:`),
    idbTabsDeleteByPrefix(`book:${storageId}:`),
    // Its tabs' search-cache pins (owner = storageId:tab) go with them.
    useSearchCacheStore().unpinAllWithPrefix(`${storageId}:`),
    deleteRecentLocations(storageId),
  ]).then(() => {})
}

/** A deleted workspace goes from every window: the first window's data and each other window's. */
function deleteWorkspaceData(wsId: string): Promise<void> {
  const otherWindows = lsKeys()
    .filter((k) => k.startsWith(tabsListKey(`${wsId}@`)))
    .map((k) => k.slice(tabsListKey('').length))
  return Promise.all([wsId, ...otherWindows].map(deleteStorageIdData)).then(() => {})
}

const DEFAULT_WS_ID = 'default'
const DEFAULT_WS_NAME = t('stores.defaultWorkspace')

function makeId(): string {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 7)
}

export const useWorkspaceStore = defineStore('workspace', () => {
  const workspaces = ref<Workspace[]>([])
  const activeId = ref<string>(DEFAULT_WS_ID)

  const activeWorkspace = computed(() => workspaces.value.find((w) => w.id === activeId.value))
  /** The id this window's tabs, per-tab state and recents are stored under (see windowScope). */
  const storageId = computed(() => windowScopedId(activeId.value))

  // Synchronous — workspaces list is in localStorage
  function init() {
    const saved = lsGet<WorkspaceList>(KEYS.SETTINGS_WORKSPACES)
    if (saved && saved.workspaces.length > 0) {
      workspaces.value = saved.workspaces
      // A window other than the first keeps its own choice; a new one starts where the
      // first window is.
      const own = windowId ? lsGet<string>(KEYS.windowActive(windowId)) : null
      const wanted = own ?? saved.activeId
      activeId.value = saved.workspaces.some((w) => w.id === wanted) ? wanted : saved.workspaces[0]!.id
    } else {
      const def: Workspace = { id: DEFAULT_WS_ID, name: DEFAULT_WS_NAME, createdAt: Date.now() }
      workspaces.value = [def]
      activeId.value = DEFAULT_WS_ID
      persist()
    }
  }

  // The list is shared by every window: another window may have added, renamed or
  // removed a workspace since this one loaded, so each change starts from what is
  // stored, never from this window's copy alone.
  function reloadSharedList() {
    const saved = lsGet<WorkspaceList>(KEYS.SETTINGS_WORKSPACES)
    if (saved && saved.workspaces.length > 0) workspaces.value = saved.workspaces
  }

  /**
   * Whether the workspace still exists in the list every window shares. Read from storage,
   * not this window's copy: another window may have deleted it, and a write for it here
   * would bring an orphan back.
   */
  function existsInSharedList(id: string): boolean {
    const saved = lsGet<WorkspaceList>(KEYS.SETTINGS_WORKSPACES)
    return (saved?.workspaces ?? workspaces.value).some((w) => w.id === id)
  }

  function persist() {
    const stored = lsGet<WorkspaceList>(KEYS.SETTINGS_WORKSPACES)
    lsSet<WorkspaceList>(KEYS.SETTINGS_WORKSPACES, {
      workspaces: toRaw(workspaces.value).map((w) => toRaw(w)),
      // Only the first window owns the shared `activeId`.
      activeId: windowId ? (stored?.activeId ?? activeId.value) : activeId.value,
    })
    if (windowId) lsSet<string>(KEYS.windowActive(windowId), activeId.value)
  }

  /**
   * This window is closing for good while other windows stay open: forget its tabs,
   * per-tab state and recents in every workspace. The first window's are the plain
   * workspace keys, so they go too - the window is gone, not just hidden.
   */
  async function discardWindowData(): Promise<void> {
    reloadSharedList()
    if (windowId) lsDelete(KEYS.windowActive(windowId))
    await Promise.all(workspaces.value.map((w) => deleteStorageIdData(windowScopedId(w.id))))
  }

  async function createWorkspace(name: string): Promise<Workspace> {
    reloadSharedList()
    const ws: Workspace = {
      id: makeId(),
      name: name.trim() || t('stores.workspace'),
      createdAt: Date.now(),
    }
    workspaces.value.push(ws)
    persist()
    return ws
  }

  async function renameWorkspace(id: string, name: string) {
    reloadSharedList()
    const ws = workspaces.value.find((w) => w.id === id)
    if (ws) {
      ws.name = name.trim() || ws.name
      persist()
    }
  }

  async function deleteWorkspace(id: string) {
    reloadSharedList()
    if (workspaces.value.length <= 1) return
    const idx = workspaces.value.findIndex((w) => w.id === id)
    if (idx === -1) return
    workspaces.value.splice(idx, 1)
    if (activeId.value === id) {
      activeId.value = workspaces.value[0]!.id
    }
    persist()
    await deleteWorkspaceData(id)
  }

  async function switchWorkspace(id: string) {
    reloadSharedList()
    if (!workspaces.value.some((w) => w.id === id)) return
    activeId.value = id
    persist()
  }

  return {
    workspaces,
    activeId,
    activeWorkspace,
    storageId,
    existsInSharedList,
    init,
    discardWindowData,
    createWorkspace,
    renameWorkspace,
    deleteWorkspace,
    switchWorkspace,
  }
})
