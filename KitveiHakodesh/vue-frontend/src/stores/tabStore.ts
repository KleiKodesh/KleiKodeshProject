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
import { useRecentlyOpenedStore, TRACKABLE_ROUTES } from './recentlyOpenedStore'
import type { RecentlyOpenedRoute } from './recentlyOpenedStore'
import {
  dropUncheckedCommentaryForTab,
  pruneUncheckedCommentary,
} from '@/features/book-view/commentary/uncheckedCommentaryBooks'

export type TabRoute =
  | '/'
  | '/pdf-view'
  | '/html-view'
  | '/txt-view'
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
  /** Which split pane this tab belongs to. Defaults to 1 when absent. */
  pane?: 1 | 2
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
  // Active tab for the secondary (right) pane in split view. Empty string means no pane 2 tab exists yet.
  const pane2ActiveTabId = ref('')
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
          searchHighlightSnippet,
          searchHighlightTerms,
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

  /** All tabs belonging to pane 1 (default) — tabs without a pane field or pane === 1. */
  const pane1Tabs = computed(() => tabs.value.filter((t) => !t.pane || t.pane === 1))

  /** All tabs belonging to pane 2. */
  const pane2Tabs = computed(() => tabs.value.filter((t) => t.pane === 2))

  /** Active tab for the given pane number. */
  function activeTabForPane(pane: 1 | 2): Tab {
    if (pane === 1) return activeTab.value
    const id = pane2ActiveTabId.value
    // Never fall back to pane 1's active tab — return the first pane-2 tab, or a
    // stable placeholder. The placeholder prevents mirroring when pane 2 is just
    // being initialized (ensurePane2HasTab hasn't run yet on this render cycle).
    return (
      tabs.value.find((t) => t.id === id && t.pane === 2) ??
      pane2Tabs.value[0] ??
      { id: '', title: 'בית', route: '/', pane: 2 as const }
    )
  }

  /** Open a new tab in pane 2, making it the active pane-2 tab. */
  function openPane2Tab(partial: Omit<Tab, 'id' | 'pane'>): Tab {
    const tab: Tab = { id: String(++nextId), pane: 2, ...partial }
    tabs.value.push(tab)
    pane2ActiveTabId.value = tab.id
    if (TRACKABLE_ROUTES.has(tab.route)) {
      trackTabNavigation(tab)
    }
    return tab
  }

  /** Switch the active tab within a specific pane. */
  function switchPaneTab(id: string, pane: 1 | 2) {
    if (pane === 1) {
      switchTab(id)
    } else {
      if (tabs.value.some((t) => t.id === id && t.pane === 2)) {
        pane2ActiveTabId.value = id
      }
    }
  }

  /** Close a pane-2 tab. Falls back to another pane-2 tab or opens a home tab in pane 2. */
  function closePane2Tab(id: string) {
    const idx = tabs.value.findIndex((t) => t.id === id && t.pane === 2)
    if (idx === -1) return
    const tab = tabs.value[idx]!
    if (tab.localFilePath) disposeLocalFileHost(tab.localFilePath)
    const wsId = useWorkspaceStore().activeId
    idbTabsDelete(KEYS.tab(wsId, id))
    idbTabsDeleteByPrefix(KEYS.tabPrefix(wsId, id))
    for (const key of _bookStateCache.keys()) {
      if (key.startsWith(`${wsId}:${id}:`)) _bookStateCache.delete(key)
    }
    dropUncheckedCommentaryForTab(id)
    tabs.value.splice(idx, 1)
    // Update pane2ActiveTabId if the closed tab was active
    if (pane2ActiveTabId.value === id) {
      const remaining = tabs.value.filter((t) => t.pane === 2)
      if (remaining.length > 0) {
        pane2ActiveTabId.value = remaining[0]!.id
      } else {
        // Pane 2 must never be left with no tab — mirror the pane-1 closeTab guarantee.
        // Without this, pane2ActiveTabId stays '' and any subsequent navigateToSingleton
        // call hits the `if (!id) return` guard in updatePane2ActiveTab and silently does nothing.
        const home: Tab = { id: String(++nextId), pane: 2, title: 'בית', route: '/' }
        tabs.value.push(home)
        pane2ActiveTabId.value = home.id
      }
    }
  }

  /** Ensure pane 2 has at least one tab; returns the active pane-2 tab id. */
  function ensurePane2HasTab(): string {
    const existing = tabs.value.filter((t) => t.pane === 2)
    if (existing.length > 0) {
      if (!pane2ActiveTabId.value || !existing.some((t) => t.id === pane2ActiveTabId.value)) {
        pane2ActiveTabId.value = existing[0]!.id
      }
      return pane2ActiveTabId.value
    }
    const tab = openPane2Tab({ title: 'בית', route: '/' })
    return tab.id
  }

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

  // ── Recently opened tracking ──────────────────────────────────────────────

  /**
   * Records a tab navigation into the recently opened history.
   * Only called for trackable routes (/book-view, /pdf-view, /html-view, /txt-view).
   * Skips entries that lack enough identity information to be re-opened later:
   *   - /book-view requires a bookId
   *   - file routes require either a localFilePath or a localFileHbBookId
   * Skips tabs still in a converting/loading state — the final title and URL
   * are not known yet; the finished state will be tracked when finishLocalFileConversion
   * (or the hbPdfReady handler) calls updateTab → which goes through updateActiveTab only
   * when it's the active tab, so for non-active tabs we track via updateTab below.
   */
  function trackTabNavigation(tab: Tab) {
    if (tab.localFileConverting) return
    if (tab.route === '/book-view' && !tab.bookId) return
    if (
      (tab.route === '/pdf-view' || tab.route === '/html-view' || tab.route === '/txt-view') &&
      !tab.localFilePath &&
      !tab.localFileHbBookId &&
      !tab.localFileName
    ) {
      return
    }
    const recentlyOpenedStore = useRecentlyOpenedStore()
    recentlyOpenedStore.trackNavigation(
      tab.route as RecentlyOpenedRoute,
      tab.title,
      tab.bookId,
      tab.localFilePath,
      tab.localFileHbBookId,
      tab.localFileHbBookTitle,
      tab.localFileName,
    )
  }

  // ── Tab lifecycle ─────────────────────────────────────────────────────────

  function openTab(partial: Omit<Tab, 'id'>) {
    const tab: Tab = { id: String(++nextId), ...partial }
    tabs.value.push(tab)
    activeTabId.value = tab.id
    if (TRACKABLE_ROUTES.has(tab.route)) {
      trackTabNavigation(tab)
    }
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
    const pane1 = tabs.value.filter((t) => !t.pane || t.pane === 1)
    for (const tab of pane1) {
      if (tab.localFilePath) disposeLocalFileHost(tab.localFilePath)
      idbTabsDelete(KEYS.tab(wsId, tab.id))
      idbTabsDeleteByPrefix(KEYS.tabPrefix(wsId, tab.id))
    }
    // Evict pane-1 book state cache entries only
    for (const key of _bookStateCache.keys()) {
      const tabId = key.split(':')[1]
      if (pane1.some((t) => t.id === tabId)) _bookStateCache.delete(key)
    }
    const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
    // Keep pane-2 tabs intact — only replace pane-1 tabs with a single home tab
    tabs.value = [home, ...tabs.value.filter((t) => t.pane === 2)]
    activeTabId.value = home.id
    pruneUncheckedCommentary(tabs.value.map((t) => t.id))
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
    dropUncheckedCommentaryForTab(id)
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
      // Only track navigation when the route or bookId/file identity changes — not on every tocPath update.
      if (TRACKABLE_ROUTES.has(tab.route) && (patch.route || patch.bookId || patch.localFilePath || patch.localFileHbBookId || patch.localFileName)) {
        trackTabNavigation(tab)
      }
    }
  }

  function updateTab(tabId: string, patch: Partial<Omit<Tab, 'id'>>) {
    const tab = tabs.value.find((t) => t.id === tabId)
    if (tab) {
      Object.assign(tab, patch)
      // Only track navigation when the route or bookId/file identity changes — not on every tocPath update.
      if (TRACKABLE_ROUTES.has(tab.route) && (patch.route || patch.bookId || patch.localFilePath || patch.localFileHbBookId || patch.localFileName)) {
        trackTabNavigation(tab)
      }
    }
  }

  /** Navigate the active pane-2 tab in place (equivalent of updateActiveTab for pane 2). */
  function updatePane2ActiveTab(patch: Partial<Omit<Tab, 'id'>>) {
    const id = pane2ActiveTabId.value
    if (!id) return
    const tab = tabs.value.find((t) => t.id === id && t.pane === 2)
    if (tab) {
      Object.assign(tab, patch)
      // Only track navigation when the route or bookId/file identity changes — not on every tocPath update.
      if (TRACKABLE_ROUTES.has(tab.route) && (patch.route || patch.bookId || patch.localFilePath || patch.localFileHbBookId || patch.localFileName)) {
        trackTabNavigation(tab)
      }
    }
  }

  function openNewHomeTab() {
    const existing = tabs.value.find((t) => t.route === '/')
    if (existing) switchTab(existing.id)
    else openTab({ title: 'בית', route: '/' })
  }

  // Singleton pages — only one tab per route allowed *within a pane*.
  // Pane 1 and pane 2 each enforce their own singleton independently.
  // These routes are never persisted across sessions — they are always stripped before saving.
  //
  // openInNewTab (default false):
  //   false — replace the current tab in-place (home page tiles, nav dropdown)
  //   true  — open a new tab, UNLESS the current tab is the home page in which case
  //           replace in-place (keyboard shortcuts like F1)
  //
  // In both modes, if a singleton tab for the route already exists, switch to it
  // without closing or replacing anything.

  function navigateToSingleton(route: TabRoute, pane: 1 | 2 = 1, openInNewTab = false) {
    const paneTabs = pane === 1
      ? tabs.value.filter((t) => !t.pane || t.pane === 1)
      : tabs.value.filter((t) => t.pane === 2)
    const existing = paneTabs.find((t) => t.route === route)
    if (pane === 1) {
      if (existing) {
        if (existing.id === activeTabId.value) return
        switchTab(existing.id)
      } else if (openInNewTab && activeTab.value.route !== '/') {
        openTab({ route, title: SINGLETON_TITLES[route] ?? route })
      } else {
        updateActiveTab({ route, title: SINGLETON_TITLES[route] ?? route })
      }
    } else {
      const currentPane2Route = tabs.value.find((t) => t.id === pane2ActiveTabId.value)?.route
      if (existing) {
        if (existing.id === pane2ActiveTabId.value) return
        switchPaneTab(existing.id, 2)
      } else if (openInNewTab && currentPane2Route !== '/') {
        openPane2Tab({ route, title: SINGLETON_TITLES[route] ?? route })
      } else {
        updatePane2ActiveTab({ route, title: SINGLETON_TITLES[route] ?? route })
      }
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
    pane1Tabs,
    pane2Tabs,
    pane2ActiveTabId,
    activeTabForPane,
    openPane2Tab,
    switchPaneTab,
    closePane2Tab,
    ensurePane2HasTab,
    updatePane2ActiveTab,
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
