import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { persistGet, persistSet, PERSIST_KEYS } from '@/utils/persist'
import { getTabState, setTabState, getBookState, setBookState, deleteBook, deleteTab } from '@/utils/tabDb'
import type { TabState, BookState } from '@/utils/tabDb'

export type TabRoute = '/' | '/pdf-view' | '/settings' | '/books' | '/book-view' | '/hebrewbooks'

export interface Tab {
  id: string
  title: string
  route: TabRoute
  pdfBlobUrl?: string
  pdfFileName?: string
  bookId?: number
  openToc?: boolean
  openTocEntryId?: number
  tocPath?: string
}

interface PersistedTabList {
  tabs: Omit<Tab, 'pdfBlobUrl' | 'openToc'>[]
  activeTabId: string
  nextId: number
}

function loadPersistedTabs(): { tabs: Tab[]; activeTabId: string; nextId: number } {
  const saved = persistGet<PersistedTabList | null>(PERSIST_KEYS.TABS, null)
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

  // ── Tab list persistence (localStorage) ─────────────────────────────────────

  function persistTabs() {
    persistSet<PersistedTabList>(PERSIST_KEYS.TABS, {
      tabs: tabs.value.map(({ pdfBlobUrl, openToc, openTocEntryId, ...t }) => t),
      activeTabId: activeTabId.value,
      nextId,
    })
  }

  watch([tabs, activeTabId], persistTabs, { deep: true })

  // ── Books view preference (localStorage) ────────────────────────────────────

  function getBooksView(): 'list' | 'tiles' | 'tree' {
    return persistGet<'list' | 'tiles' | 'tree'>(PERSIST_KEYS.BOOKS_VIEW, 'list')
  }

  function setBooksView(view: 'list' | 'tiles' | 'tree') {
    persistSet(PERSIST_KEYS.BOOKS_VIEW, view)
  }

  // ── Global book-view prefs (localStorage) ────────────────────────────────────

  function getToolbarVisible(): boolean {
    return persistGet(PERSIST_KEYS.BOOK_VIEW_TOOLBAR, true)
  }

  function setToolbarVisible(val: boolean) {
    persistSet(PERSIST_KEYS.BOOK_VIEW_TOOLBAR, val)
  }

  function getSearchBarPos(): { x: number; y: number } | null {
    return persistGet(PERSIST_KEYS.BOOK_VIEW_SEARCH_BAR_POS, null)
  }

  function setSearchBarPos(pos: { x: number; y: number }) {
    persistSet(PERSIST_KEYS.BOOK_VIEW_SEARCH_BAR_POS, pos)
  }

  // ── Per-tab state (IndexedDB) ────────────────────────────────────────────────

  function getTabViewState(tabId: string): Promise<TabState | null> {
    return getTabState(tabId)
  }

  function setTabViewState(tabId: string, state: TabState): Promise<void> {
    return setTabState(tabId, state)
  }

  // ── Per-tab+book state (IndexedDB) ───────────────────────────────────────────

  function getBookViewState(tabId: string, bookId: number): Promise<BookState | null> {
    return getBookState(tabId, bookId)
  }

  function setBookViewState(tabId: string, bookId: number, state: BookState): Promise<void> {
    return setBookState(tabId, bookId, state)
  }

  function clearBookViewState(tabId: string, bookId: number): Promise<void> {
    return deleteBook(tabId, bookId)
  }

  // ── Tab lifecycle ────────────────────────────────────────────────────────────

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
    deleteTab(id) // wipe all IDB state for this tab
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

  return {
    tabs, activeTabId, activeTab,
    openTab, switchTab, closeTab, updateActiveTab, openNewHomeTab,
    getBooksView, setBooksView,
    getToolbarVisible, setToolbarVisible,
    getSearchBarPos, setSearchBarPos,
    getTabViewState, setTabViewState,
    getBookViewState, setBookViewState, clearBookViewState,
  }
})
