import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import {
  lsGet,
  lsSet,
  idbTabsGet,
  idbTabsSet,
  idbTabsDelete,
  idbTabsDeleteByPrefix,
  idbSetLastRead,
  idbGetLastRead,
  idbClearAll,
  KEYS,
} from '@/utils/persistence'
import type { TabState, BookState, LastReadState } from '@/utils/persistence'
import { useWorkspaceStore } from './workspaceStore'
import { disposeLocalFileHost } from '@/webview-host/bridge'

export type TabRoute =
  | '/'
  | '/pdf-view'
  | '/html-view'
  | '/settings'
  | '/books'
  | '/book-view'
  | '/hebrewbooks'
  | '/workspaces'
  | '/search'
  | '/hebrew-calendar'
  | '/dictionary'
  | '/midot'
  | '/file-search'

export interface Tab {
  id: string
  title: string
  route: TabRoute
  // Local file state (PDF, HTML, Word)
  localFileVirtualUrl?: string // in-memory only — not persisted, reconstructed on restore
  localFileName?: string
  localFilePath?: string // persisted — local file path (for local PDF / Word / HTML files)
  localFileHbBookId?: string // persisted — HebrewBooks book ID (for cache restore / re-download)
  localFileHbBookTitle?: string // persisted — HebrewBooks book title (used as cache filename)
  localFileConverting?: boolean // in-memory only — true while Word conversion is in progress
  localFileLoadingType?: 'converting' | 'downloading' // in-memory only — drives placeholder message
  pdfViewerTitleBarVisible?: boolean // persisted — whether to show PDF.js viewer title bar (default true)
  // Kiwix ZIM state — removed; feature deferred to a later stage
  // Book reader state
  bookId?: number
  openToc?: boolean
  openTocEntryId?: number
  openTocLineIndex?: number
  searchHighlightLineIndex?: number
  searchHighlightQuery?: string
  searchHighlightSnippet?: string
  searchHighlightTerms?: string[]
  searchQuery?: string
  tocPath?: string
}

interface PersistedTabList {
  tabs: Omit<Tab, 'localFileVirtualUrl' | 'openToc'>[]
  activeTabId: string
  nextId: number
}

const DEFAULT_TAB: Tab = { id: '1', title: 'בית', route: '/' }

export const useTabStore = defineStore('tabs', () => {
  const tabs = ref<Tab[]>([DEFAULT_TAB])
  const activeTabId = ref('1')
  let nextId = 1

  // ── Init (called once from main.ts before mount) ──────────────────────────

  // Synchronous — tab list is in localStorage
  function init() {
    const wsStore = useWorkspaceStore()
    const wsId = wsStore.activeId
    const saved = lsGet<PersistedTabList>(KEYS.tabsList(wsId))
    if (saved && saved.tabs.length > 0) {
      tabs.value = saved.tabs
      activeTabId.value = saved.activeTabId
      nextId = saved.nextId
    }
  }

  // ── Singleton routes — never persisted across sessions ───────────────────

  const SINGLETON_ROUTES: TabRoute[] = [
    '/settings',
    '/books',
    '/hebrewbooks',
    '/workspaces',
    '/hebrew-calendar',
    '/dictionary',
    '/midot',
    '/file-search',
  ]
  const SINGLETON_TITLES: Record<string, string> = {
    '/settings': 'הגדרות',
    '/books': 'ספרים',
    '/hebrewbooks': 'היברו-בוקס',
    '/workspaces': 'סביבות עבודה',
    '/hebrew-calendar': 'לוח שנה',
    '/dictionary': 'מילון',
    '/midot': 'מידות ושיעורים',
    '/file-search': 'חיפוש קבצים',
  }

  // ── Tab list persistence ──────────────────────────────────────────────────

  function persistTabs() {
    const wsId = useWorkspaceStore().activeId
    const persistable = tabs.value.filter((t) => !SINGLETON_ROUTES.includes(t.route))
    lsSet<PersistedTabList>(KEYS.tabsList(wsId), {
      tabs: persistable.map(
        ({
          localFileVirtualUrl,
          localFileConverting,
          localFileLoadingType,
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

  // Only watch the fields that are actually persisted — avoids IDB writes on every
  // in-memory-only mutation (pdfVirtualUrl, pdfConverting, etc.)
  const _persistedSnapshot = computed(() =>
    tabs.value
      .filter((t) => !SINGLETON_ROUTES.includes(t.route))
      .map((t) => ({
        id: t.id,
        title: t.title,
        route: t.route,
        localFileName: t.localFileName,
        localFilePath: t.localFilePath,
        localFileHbBookId: t.localFileHbBookId,
        localFileHbBookTitle: t.localFileHbBookTitle,
        bookId: t.bookId,
        searchQuery: t.searchQuery,
        tocPath: t.tocPath,
      })),
  )
  watch([_persistedSnapshot, activeTabId], persistTabs)

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

  // In-memory cache: key = `${wsId}:${tabId}:${bookId}`
  const _bookStateCache = new Map<string, BookState | null>()
  // In-memory cache: key = bookId
  const _lastReadCache = new Map<number, LastReadState | null>()

  // Pending save promise — onMounted on the incoming tab awaits this before reading,
  // so the outgoing tab's async IDB write is guaranteed to have committed first.
  let pendingBookStateSave: Promise<void> | null = null

  function getBookViewState(tabId: string, bookId: number): Promise<BookState | null> {
    const wsId = useWorkspaceStore().activeId
    const cacheKey = `${wsId}:${tabId}:${bookId}`
    if (_bookStateCache.has(cacheKey)) return Promise.resolve(_bookStateCache.get(cacheKey)!)
    const read = async () => {
      const val = await idbTabsGet<BookState>(KEYS.book(wsId, tabId, bookId))
      _bookStateCache.set(cacheKey, val)
      return val
    }
    return pendingBookStateSave ? pendingBookStateSave.then(read) : read()
  }
  function setBookViewState(tabId: string, bookId: number, state: BookState): Promise<void> {
    const wsId = useWorkspaceStore().activeId
    const cacheKey = `${wsId}:${tabId}:${bookId}`
    _bookStateCache.set(cacheKey, state)
    pendingBookStateSave = idbTabsSet(KEYS.book(wsId, tabId, bookId), state)
    return pendingBookStateSave
  }
  function clearBookViewState(tabId: string, bookId: number): Promise<void> {
    const wsId = useWorkspaceStore().activeId
    const cacheKey = `${wsId}:${tabId}:${bookId}`
    _bookStateCache.delete(cacheKey)
    return idbTabsDelete(KEYS.book(wsId, tabId, bookId))
  }

  // ── Global last-read per book (LRU-capped at 1000) ────────────────────────

  let pendingLastReadSave: Promise<void> | null = null

  function getLastReadPos(bookId: number): Promise<LastReadState | null> {
    if (_lastReadCache.has(bookId)) return Promise.resolve(_lastReadCache.get(bookId)!)
    const read = async () => {
      const val = await idbGetLastRead(bookId)
      _lastReadCache.set(bookId, val)
      return val
    }
    return pendingLastReadSave ? pendingLastReadSave.then(read) : read()
  }
  function setLastReadPos(bookId: number, pos: LastReadState): Promise<void> {
    _lastReadCache.set(bookId, pos)
    // Keep in-memory cache from growing unbounded — evict oldest entry when over 200
    if (_lastReadCache.size > 200) _lastReadCache.delete(_lastReadCache.keys().next().value!)
    pendingLastReadSave = idbSetLastRead(bookId, pos)
    return pendingLastReadSave
  }

  // ── Books view setting ────────────────────────────────────────────────────

  let _booksView: 'list' | 'tiles' | 'tree' | null = null

  async function getBooksView(): Promise<'list' | 'tiles' | 'tree'> {
    if (_booksView !== null) return _booksView
    _booksView = lsGet<'list' | 'tiles' | 'tree'>(KEYS.SETTINGS_BOOKS_VIEW) ?? 'list'
    return _booksView
  }
  function setBooksView(v: 'list' | 'tiles' | 'tree') {
    _booksView = v
    lsSet(KEYS.SETTINGS_BOOKS_VIEW, v)
  }

  // ── App reset ─────────────────────────────────────────────────────────────

  async function resetAll(): Promise<void> {
    await idbClearAll()
  }

  // ── Tab lifecycle ─────────────────────────────────────────────────────────

  function openTab(partial: Omit<Tab, 'id'>) {
    const tab: Tab = { id: String(++nextId), ...partial }
    tabs.value.push(tab)
    activeTabId.value = tab.id
    return tab
  }

  function switchTab(id: string) {
    if (tabs.value.some((t) => t.id === id)) {
      activeTabId.value = id
      // Move switched tab to the front for MRU ordering
      const idx = tabs.value.findIndex((t) => t.id === id)
      if (idx > 0) {
        const tab = tabs.value[idx]!
        tabs.value.splice(idx, 1)
        tabs.value.unshift(tab)
      }
    }
  }

  function closeAllTabs() {
    const wsId = useWorkspaceStore().activeId
    for (const tab of tabs.value) {
        if (tab.localFilePath) disposeLocalFileHost(tab.localFilePath)
      idbTabsDelete(KEYS.tab(wsId, tab.id))
      idbTabsDeleteByPrefix(KEYS.tabPrefix(wsId, tab.id))
    }
    _bookStateCache.clear()
    const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
    tabs.value = [home]
    activeTabId.value = home.id
  }

  function closeTab(id: string) {
    const idx = tabs.value.findIndex((t) => t.id === id)
    if (idx === -1) return
    const tab = tabs.value[idx]!
    if (tab.localFilePath) disposeLocalFileHost(tab.localFilePath)
    const wsId = useWorkspaceStore().activeId
    idbTabsDelete(KEYS.tab(wsId, id))
    idbTabsDeleteByPrefix(KEYS.tabPrefix(wsId, id))
    // Evict all book state cache entries for this tab
    for (const key of _bookStateCache.keys()) {
      if (key.startsWith(`${wsId}:${id}:`)) _bookStateCache.delete(key)
    }
    tabs.value.splice(idx, 1)
    if (tabs.value.length === 0) {
      const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
      tabs.value.push(home)
      activeTabId.value = home.id
    } else if (activeTabId.value === id) {
      activeTabId.value = tabs.value[Math.min(idx, tabs.value.length - 1)]!.id
    }
  }

  function updateActiveTab(patch: Partial<Omit<Tab, 'id'>>) {
    const tab = tabs.value.find((t) => t.id === activeTabId.value)
    if (tab) {
      Object.assign(tab, patch)
      // Move to front for MRU ordering
      const idx = tabs.value.findIndex((t) => t.id === activeTabId.value)
      if (idx > 0) {
        tabs.value.splice(idx, 1)
        tabs.value.unshift(tab)
      }
    }
  }

  function updateTab(tabId: string, patch: Partial<Omit<Tab, 'id'>>) {
    const tab = tabs.value.find((t) => t.id === tabId)
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
      // Already open in another tab — switch to it and close the current one
      const currentId = activeTabId.value
      switchTab(existing.id)
      if (currentId !== existing.id) closeTab(currentId)
    } else {
      // Not open anywhere — navigate in place (replace current tab's content)
      updateActiveTab({ route, title: SINGLETON_TITLES[route] ?? route })
    }
  }

  // ── PDF viewer title bar visibility ───────────────────────────────────────

  function togglePdfViewerTitleBar() {
    const tab = tabs.value.find((t) => t.id === activeTabId.value)
    if (tab) {
      tab.pdfViewerTitleBarVisible = tab.pdfViewerTitleBarVisible !== false ? false : true
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
    updateTab,
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
    togglePdfViewerTitleBar,
  }
})
