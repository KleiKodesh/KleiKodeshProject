import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import {
  idbGet,
  idbSet,
  idbTabsGet,
  idbTabsSet,
  idbTabsDelete,
  idbTabsDeleteByPrefix,
  idbSetLastRead,
  idbGetLastRead,
  idbClearAll,
  KEYS,
} from '@/utils/idbPersistence'
import type { TabState, BookState, LastReadState } from '@/utils/idbPersistence'
import { useWorkspaceStore } from './workspaceStore'
import { disposePdfHost } from '@/host/bridge'

export type TabRoute =
  | '/'
  | '/pdf-view'
  | '/settings'
  | '/books'
  | '/book-view'
  | '/hebrewbooks'
  | '/workspaces'
  | '/search'

export interface Tab {
  id: string
  title: string
  route: TabRoute
  // PDF state
  pdfVirtualUrl?: string // in-memory only — not persisted, reconstructed on restore
  pdfFileName?: string
  pdfFilePath?: string // persisted — local file path (for local PDF / Word files)
  pdfHbBookId?: string // persisted — HebrewBooks book ID (for cache restore / re-download)
  pdfHbBookTitle?: string // persisted — HebrewBooks book title (used as cache filename)
  pdfConverting?: boolean // in-memory only — true while Word conversion is in progress
  pdfLoadingType?: 'converting' | 'downloading' // in-memory only — drives placeholder message
  // Book reader state
  bookId?: number
  openToc?: boolean
  openTocEntryId?: number
  openTocLineIndex?: number
  searchHighlightLineIndex?: number
  searchHighlightQuery?: string
  searchQuery?: string
  searchScrollIndex?: number
  tocPath?: string
}

interface PersistedTabList {
  tabs: Omit<Tab, 'pdfVirtualUrl' | 'openToc'>[]
  activeTabId: string
  nextId: number
}

const DEFAULT_TAB: Tab = { id: '1', title: 'בית', route: '/' }

export const useTabStore = defineStore('tabs', () => {
  const tabs = ref<Tab[]>([DEFAULT_TAB])
  const activeTabId = ref('1')
  let nextId = 1

  // ── Init (called once from main.ts before mount) ──────────────────────────

  async function init() {
    const wsStore = useWorkspaceStore()
    const wsId = wsStore.activeId
    const saved = await idbTabsGet<PersistedTabList>(KEYS.tabsList(wsId))
    if (saved && saved.tabs.length > 0) {
      tabs.value = saved.tabs
      activeTabId.value = saved.activeTabId
      nextId = saved.nextId
    }
  }

  // ── Singleton routes — never persisted across sessions ───────────────────

  const SINGLETON_ROUTES: TabRoute[] = ['/settings', '/books', '/hebrewbooks', '/workspaces']
  const SINGLETON_TITLES: Record<string, string> = {
    '/settings': 'הגדרות',
    '/books': 'ספרים',
    '/hebrewbooks': 'היברו-בוקס',
    '/workspaces': 'סביבות עבודה',
  }

  // ── Tab list persistence ──────────────────────────────────────────────────

  function persistTabs() {
    const wsId = useWorkspaceStore().activeId
    const persistable = tabs.value.filter((t) => !SINGLETON_ROUTES.includes(t.route))
    idbTabsSet<PersistedTabList>(KEYS.tabsList(wsId), {
      tabs: persistable.map(
        ({
          pdfVirtualUrl,
          pdfConverting,
          pdfLoadingType,
          openToc,
          openTocEntryId,
          openTocLineIndex,
          searchHighlightLineIndex,
          searchHighlightQuery,
          ...t
        }) => t,
      ),
      activeTabId: persistable.some((t) => t.id === activeTabId.value)
        ? activeTabId.value
        : (persistable[0]?.id ?? activeTabId.value),
      nextId,
    })
  }

  watch([tabs, activeTabId], persistTabs, { deep: true })

  const activeTab = computed(
    (): Tab => tabs.value.find((t) => t.id === activeTabId.value) ?? tabs.value[0]!,
  )

  // ── Per-tab state ─────────────────────────────────────────────────────────

  function getTabViewState(tabId: string): Promise<TabState | null> {
    const wsId = useWorkspaceStore().activeId
    return idbTabsGet<TabState>(KEYS.tab(wsId, tabId))
  }
  function setTabViewState(tabId: string, state: TabState): Promise<void> {
    const wsId = useWorkspaceStore().activeId
    return idbTabsSet(KEYS.tab(wsId, tabId), state)
  }

  // ── Per-tab+book state ────────────────────────────────────────────────────

  function getBookViewState(tabId: string, bookId: number): Promise<BookState | null> {
    const wsId = useWorkspaceStore().activeId
    return idbTabsGet<BookState>(KEYS.book(wsId, tabId, bookId))
  }
  function setBookViewState(tabId: string, bookId: number, state: BookState): Promise<void> {
    const wsId = useWorkspaceStore().activeId
    return idbTabsSet(KEYS.book(wsId, tabId, bookId), state)
  }
  function clearBookViewState(tabId: string, bookId: number): Promise<void> {
    const wsId = useWorkspaceStore().activeId
    return idbTabsDelete(KEYS.book(wsId, tabId, bookId))
  }

  // ── Global last-read per book (LRU-capped at 1000) ────────────────────────

  function getLastReadPos(bookId: number): Promise<LastReadState | null> {
    return idbGetLastRead(bookId)
  }
  function setLastReadPos(bookId: number, pos: LastReadState): Promise<void> {
    return idbSetLastRead(bookId, pos)
  }

  // ── Books view setting ────────────────────────────────────────────────────

  async function getBooksView(): Promise<'list' | 'tiles' | 'tree'> {
    return (await idbGet<'list' | 'tiles' | 'tree'>(KEYS.SETTINGS_BOOKS_VIEW)) ?? 'list'
  }
  function setBooksView(v: 'list' | 'tiles' | 'tree') {
    idbSet(KEYS.SETTINGS_BOOKS_VIEW, v)
  }

  // ── App reset ─────────────────────────────────────────────────────────────

  function resetAll(): Promise<void> {
    return idbClearAll()
  }

  // ── Tab lifecycle ─────────────────────────────────────────────────────────

  function openTab(partial: Omit<Tab, 'id'>) {
    const tab: Tab = { id: String(++nextId), ...partial }
    tabs.value.push(tab)
    activeTabId.value = tab.id
    return tab
  }

  function switchTab(id: string) {
    if (tabs.value.some((t) => t.id === id)) activeTabId.value = id
  }

  function closeAllTabs() {
    const wsId = useWorkspaceStore().activeId
    for (const tab of tabs.value) {
      if (tab.pdfFilePath) disposePdfHost(tab.pdfFilePath)
      idbTabsDelete(KEYS.tab(wsId, tab.id))
      idbTabsDeleteByPrefix(KEYS.tabPrefix(wsId, tab.id))
    }
    const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
    tabs.value = [home]
    activeTabId.value = home.id
  }

  function closeTab(id: string) {
    const idx = tabs.value.findIndex((t) => t.id === id)
    if (idx === -1) return
    const tab = tabs.value[idx]!
    if (tab.pdfFilePath) disposePdfHost(tab.pdfFilePath)
    const wsId = useWorkspaceStore().activeId
    idbTabsDelete(KEYS.tab(wsId, id))
    idbTabsDeleteByPrefix(KEYS.tabPrefix(wsId, id))
    tabs.value.splice(idx, 1)
    if (activeTabId.value === id)
      activeTabId.value = tabs.value[Math.min(idx, tabs.value.length - 1)]?.id ?? ''
    if (tabs.value.length === 0) {
      const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
      tabs.value.push(home)
      activeTabId.value = home.id
    }
  }

  function updateActiveTab(patch: Partial<Omit<Tab, 'id'>>) {
    const tab = tabs.value.find((t) => t.id === activeTabId.value)
    if (tab) Object.assign(tab, patch)
  }

  function openNewHomeTab() {
    const existing = tabs.value.find((t) => t.route === '/')
    if (existing) switchTab(existing.id)
    else openTab({ title: 'בית', route: '/' })
  }

  // Singleton pages — only one tab per route allowed; switch if exists, else replace current tab
  // These routes are never persisted across sessions — they are always stripped before saving

  function navigateToSingleton(route: TabRoute) {
    const existing = tabs.value.find((t) => t.route === route)
    if (existing) {
      switchTab(existing.id)
    } else {
      openTab({ route, title: SINGLETON_TITLES[route] ?? route })
    }
  }

  return {
    tabs,
    activeTabId,
    activeTab,
    init,
    openTab,
    switchTab,
    closeTab,
    closeAllTabs,
    updateActiveTab,
    openNewHomeTab,
    navigateToSingleton,
    getBooksView,
    setBooksView,
    getLastReadPos,
    setLastReadPos,
    getTabViewState,
    setTabViewState,
    getBookViewState,
    setBookViewState,
    clearBookViewState,
    resetAll,
  }
})
