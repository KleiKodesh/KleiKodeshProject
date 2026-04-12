import { defineStore } from 'pinia'
import { ref, computed, toRaw } from 'vue'
import { idbGet, idbSet, idbDeleteWorkspaceData, KEYS } from '@/utils/idbPersistence'
import type { Workspace, WorkspaceList } from '@/utils/idbPersistence'

export type { Workspace } from '@/utils/idbPersistence'

const DEFAULT_WS_ID = 'default'
const DEFAULT_WS_NAME = 'ברירת מחדל'

function makeId(): string {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 7)
}

export const useWorkspaceStore = defineStore('workspace', () => {
  const workspaces = ref<Workspace[]>([])
  const activeId = ref<string>(DEFAULT_WS_ID)

  const activeWorkspace = computed(() => workspaces.value.find((w) => w.id === activeId.value))

  async function init() {
    const saved = await idbGet<WorkspaceList>(KEYS.SETTINGS_WORKSPACES)
    if (saved && saved.workspaces.length > 0) {
      workspaces.value = saved.workspaces
      activeId.value = saved.activeId
    } else {
      // First launch — create default workspace
      const def: Workspace = { id: DEFAULT_WS_ID, name: DEFAULT_WS_NAME, createdAt: Date.now() }
      workspaces.value = [def]
      activeId.value = DEFAULT_WS_ID
      await persist()
    }
  }

  function persist() {
    return idbSet<WorkspaceList>(KEYS.SETTINGS_WORKSPACES, {
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
    await persist()
    return ws
  }

  async function renameWorkspace(id: string, name: string) {
    const ws = workspaces.value.find((w) => w.id === id)
    if (ws) {
      ws.name = name.trim() || ws.name
      await persist()
    }
  }

  async function deleteWorkspace(id: string) {
    if (workspaces.value.length <= 1) return // can't delete last workspace
    const idx = workspaces.value.findIndex((w) => w.id === id)
    if (idx === -1) return
    workspaces.value.splice(idx, 1)
    // If deleting active, switch to first remaining
    if (activeId.value === id) {
      activeId.value = workspaces.value[0]!.id
    }
    await persist()
    // Clean up all IDB data for the deleted workspace
    await idbDeleteWorkspaceData(id)
  }

  /** Switch active workspace — caller should reload the app */
  async function switchWorkspace(id: string) {
    if (!workspaces.value.some((w) => w.id === id)) return
    activeId.value = id
    await persist()
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
