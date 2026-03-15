import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export type TabRoute = '/' | '/pdf-view' | '/settings' | '/books' | '/book-view'

export interface Tab {
  id: string
  title: string
  route: TabRoute
  pdfBlobUrl?: string
  pdfFileName?: string
}

let nextId = 1

export const useTabStore = defineStore('tabs', () => {
  const tabs = ref<Tab[]>([{ id: '1', title: 'בית', route: '/' }])
  const activeTabId = ref('1')

  const activeTab = computed((): Tab => tabs.value.find(t => t.id === activeTabId.value) ?? tabs.value[0]!)

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

  return { tabs, activeTabId, activeTab, openTab, switchTab, closeTab, updateActiveTab, openNewHomeTab }
})
