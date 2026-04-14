import { defineStore } from 'pinia'
import { ref, computed, toRaw } from 'vue'
import { lsGet, lsSet, idbDeleteWorkspaceData, KEYS } from '@/utils/persistence'
import type { Workspace, WorkspaceList } from '@/utils/persistence'

export type { Workspace } from '@/utils/persistence'

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
    await idbDeleteWorkspaceData(id)
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
