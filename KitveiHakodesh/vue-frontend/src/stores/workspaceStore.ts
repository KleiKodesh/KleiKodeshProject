import { defineStore } from 'pinia'
import { ref, computed, toRaw } from 'vue'
import { lsGet, lsSet, lsDelete, idbTabsDeleteByPrefix } from '@/utils/persistence'

/**
 * Disk names owned here. `tabsList` is exported because `tabStore` writes the list
 * this store's teardown deletes — it lives here rather than in `tabStore` because
 * the key is workspace-scoped, and because `tabStore` already imports this module
 * (the reverse would close a cycle).
 */
const KEYS = { SETTINGS_WORKSPACES: 'workspaces.list' } as const
export const tabsListKey = (wsId: string) => `tabs:${wsId}`

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
 * Forget everything persisted for a deleted workspace: its tab list (localStorage,
 * removed synchronously) and every per-tab and per-book state beneath it (app-tabs).
 *
 * Lives here rather than in the storage driver because the `tab:` / `book:` key
 * shapes and the fact that a workspace spans two storage systems are app knowledge,
 * not storage mechanics.
 */
function deleteWorkspaceData(wsId: string): Promise<void> {
  lsDelete(tabsListKey(wsId))
  return Promise.all([
    idbTabsDeleteByPrefix(`tab:${wsId}:`),
    idbTabsDeleteByPrefix(`book:${wsId}:`),
  ]).then(() => {})
}

const DEFAULT_WS_ID = 'default'
const DEFAULT_WS_NAME = 'ברירת מחדל'

function makeId(): string {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 7)
}

export const useWorkspaceStore = defineStore('workspace', () => {
  const workspaces = ref<Workspace[]>([])
  const activeId = ref<string>(DEFAULT_WS_ID)

  const activeWorkspace = computed(() => workspaces.value.find((w) => w.id === activeId.value))

  // Synchronous — workspaces list is in localStorage
  function init() {
    const saved = lsGet<WorkspaceList>(KEYS.SETTINGS_WORKSPACES)
    if (saved && saved.workspaces.length > 0) {
      workspaces.value = saved.workspaces
      activeId.value = saved.activeId
    } else {
      const def: Workspace = { id: DEFAULT_WS_ID, name: DEFAULT_WS_NAME, createdAt: Date.now() }
      workspaces.value = [def]
      activeId.value = DEFAULT_WS_ID
      persist()
    }
  }

  function persist() {
    lsSet<WorkspaceList>(KEYS.SETTINGS_WORKSPACES, {
      workspaces: toRaw(workspaces.value).map((w) => toRaw(w)),
      activeId: activeId.value,
    })
  }

  async function createWorkspace(name: string): Promise<Workspace> {
    const ws: Workspace = {
      id: makeId(),
      name: name.trim() || 'סביבת עבודה',
      createdAt: Date.now(),
    }
    workspaces.value.push(ws)
    persist()
    return ws
  }

  async function renameWorkspace(id: string, name: string) {
    const ws = workspaces.value.find((w) => w.id === id)
    if (ws) {
      ws.name = name.trim() || ws.name
      persist()
    }
  }

  async function deleteWorkspace(id: string) {
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
    if (!workspaces.value.some((w) => w.id === id)) return
    activeId.value = id
    persist()
  }

  return {
    workspaces,
    activeId,
    activeWorkspace,
    init,
    createWorkspace,
    renameWorkspace,
    deleteWorkspace,
    switchWorkspace,
  }
})
