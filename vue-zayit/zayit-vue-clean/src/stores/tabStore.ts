import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { persistGet, persistSet, PERSIST_KEYS } from '@/utils/persist'

export type TabRoute = '/' | '/pdf-view' | '/settings' | '/books' | '/book-view'

export interface Tab {
  id: string
  title: string
  route: TabRoute
  pdfBlobUrl?: string
  pdfFileName?: string
  bookId?: number
}

interface PersistedTabState {
  tabs: Omit<Tab, 'pdfBlobUrl'>[]
  activeTabId: string
  nextId: number
}

function loadPersistedTabs(): { tabs: Tab[]; activeTabId: string; nextId: number } {
  const saved = persistGet<PersistedTabState | null>(PERSIST_KEYS.TABS, null)
  if (saved && saved.tabs.length > 0) {
    return { tabs: saved.tabs, activeTabId: saved.activeTabId, nextId: saved.nextId }
  }
  return { tabs: [{ id: '1', title: 'בית', route: '/' }], activeTabId: '1', nextId: 1 }
}

export const useTabStore = defineStore('tabs', () => {
  const initial = loadPersistedTabs()
  let nextId = initial.nextId

  const tabs = ref<Tab[]>(initial.tabs)
  const activeTabId = ref(initial.activeTabId)
  const activeTab = computed((): Tab => tabs.value.find(t => t.id === activeTabId.value) ?? tabs.value[0]!)

  // Callbacks registered by other stores to react to tab close (avoids circular imports)
  const onCloseCallbacks: Array<(id: string) => void> = []
  function onTabClose(cb: (id: string) => void) { onCloseCallbacks.push(cb) }

  function persist() {
    persistSet<PersistedTabState>(PERSIST_KEYS.TABS, {
      tabs: tabs.value.map(({ pdfBlobUrl, ...t }) => t),
      activeTabId: activeTabId.value,
      nextId,
    })
  }

  watch([tabs, activeTabId], persist, { deep: true })

  function openTab(partial: Omit<Tab, 'id'>) {
    const tab: Tab = { id: String(++nextId), ...partial }
    tabs.value.push(tab)
    activeTabId.value = tab.id
    return tab
  }

  function switchTab(id: string) {
    if (tabs.value.some(t => t.id === id)) activeTabId.value = id
  }

  function closeTab(id: string) {
    const idx = tabs.value.findIndex(t => t.id === id)
    if (idx === -1) return
    const tab = tabs.value[idx]!
    if (tab.pdfBlobUrl) URL.revokeObjectURL(tab.pdfBlobUrl)
    onCloseCallbacks.forEach(cb => cb(id))
    tabs.value.splice(idx, 1)
    if (activeTabId.value === id) {
      activeTabId.value = tabs.value[Math.min(idx, tabs.value.length - 1)]?.id ?? ''
    }
    if (tabs.value.length === 0) {
      const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
      tabs.value.push(home)
      activeTabId.value = home.id
    }
  }

  function updateActiveTab(patch: Partial<Omit<Tab, 'id'>>) {
    const tab = tabs.value.find(t => t.id === activeTabId.value)
    if (tab) Object.assign(tab, patch)
  }

  function openNewHomeTab() {
    const existing = tabs.value.find(t => t.route === '/')
    if (existing) switchTab(existing.id)
    else openTab({ title: 'בית', route: '/' })
  }

  return { tabs, activeTabId, activeTab, openTab, switchTab, closeTab, updateActiveTab, openNewHomeTab, onTabClose }
})
