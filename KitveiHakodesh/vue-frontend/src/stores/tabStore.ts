import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'
import { tabsListKey } from './workspaceStore'
import { useWorkspaceStore } from './workspaceStore'
import {
  getTabViewState,
  setTabViewState,
  getBookViewState,
  setBookViewState,
  clearBookViewState,
  deleteAllStateForTab,
} from './tabStatePersistence'
import { getLastReadPos, setLastReadPos } from './bookLastRead'
import { useBookViewStore } from './bookViewStore'
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
  localFileDownloadProgress?: string // in-memory only — live HB download progress text (e.g. "6.2 / 13.2 MB · 47%")
  pdfViewerTitleBarVisible?: boolean // persisted — whether to show PDF.js viewer title bar (default true)
  // Kiwix ZIM state — removed; feature deferred to a later stage
  // Book reader state
  bookId?: number
  openToc?: boolean
  openTocEntryId?: number
  openTocLineIndex?: number
  // Set when the book is opened from a VSTO host deep link (otzaria:// / zayit://).
  // Triggers a momentary background flash on the target line, then fades. Consumed
  // and cleared on open like the openToc* fields.
  flashOpenLine?: boolean
  searchHighlightLineIndex?: number
  searchHighlightQuery?: string
  searchHighlightSnippet?: string
  searchHighlightTerms?: string[]
  searchQuery?: string
  // Seed query for the book-catalog page (route '/books'), pushed from the VSTO
  // host's "חיפוש ספר בכתבי הקודש" context menu. BookCatalogPage reads this on mount
  // into its reactive searchQuery. Kept separate from searchQuery, which the FTS page owns.
  catalogQuery?: string
  // Query for the local-file-search page (route '/file-search'), saved so the input
  // survives navigating away and back. Kept separate from searchQuery, which the FTS
  // page owns — sharing that field leaked an FTS query into the file-search input.
  fileSearchQuery?: string
  // Query typed into the book-catalog page (route '/books'), saved so the input
  // survives a tab switch and back. Cleared when the tab navigates in place to a
  // book (see BookCatalogPage) — distinct from catalogQuery, which is a one-shot
  // seed pushed from the VSTO host.
  booksSearchQuery?: string
  // Query typed into the HebrewBooks page (route '/hebrewbooks'), saved so the
  // input survives a tab switch and back. Cleared when the tab navigates in place.
  hebrewBooksSearchQuery?: string
  tocPath?: string
  /** Persisted — true when this /html-view tab is hosting an Otzaria addin (manifest.json detected next to the HTML file). */
  isOtzariaAddin?: boolean
}

interface PersistedTabList {
  tabs: Omit<Tab, 'localFileVirtualUrl' | 'openToc'>[]
  activeTabId: string
  nextId: number
  /**
   * Tab-id → access stamp, most-recently-active = highest. Optional: a list saved
   * by an older build restores with no recency and falls back to strip order.
   */
  accessOrder?: Record<string, number>
}

const DEFAULT_TAB: Tab = { id: '1', title: 'בית', route: '/' }

export const useTabStore = defineStore('tabs', () => {
  const tabs = ref<Tab[]>([DEFAULT_TAB])
  const activeTabId = ref('1')
  // Active tab for the secondary (right) pane in split view. Empty string means no pane 2 tab exists yet.
  const pane2ActiveTabId = ref('')
  let nextId = 1

  // Access order (MRU) — see the mruTabsForPane section below. Declared up here
  // because init() restores it alongside the tab list.
  const accessOrder = ref(new Map<string, number>())
  let accessTick = 0

  // ── Init (called once from main.ts before mount) ──────────────────────────

  // Synchronous — tab list is in localStorage
  function init() {
    const wsStore = useWorkspaceStore()
    const wsId = wsStore.activeId
    const saved = lsGet<PersistedTabList>(tabsListKey(wsId))
    if (saved && saved.tabs.length > 0) {
      tabs.value = saved.tabs
      activeTabId.value = saved.activeTabId
      nextId = saved.nextId
      if (saved.accessOrder) {
        // Drop stamps for tabs that no longer exist (singletons were stripped on
        // save), and rebase the tick above the highest restored stamp so new
        // accesses still sort on top.
        const live = new Map<string, number>()
        for (const t of saved.tabs) {
          const stamp = saved.accessOrder[t.id]
          if (stamp !== undefined) live.set(t.id, stamp)
        }
        accessOrder.value = live
        accessTick = live.size > 0 ? Math.max(...live.values()) : 0
      }
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
    lsSet<PersistedTabList>(tabsListKey(wsId), {
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
        // Fallback must prefer a pane-1 tab: restoring a pane-2 id as pane 1's
        // active tab makes both panes render the same tab on next startup.
        : (persistable.find((t) => t.pane !== 2)?.id ?? persistable[0]?.id ?? activeTabId.value),
      nextId,
      // Only for tabs actually being saved — a stamp for a stripped singleton would
      // be dead weight that init() discards anyway.
      accessOrder: Object.fromEntries(
        persistable
          .map((t) => [t.id, accessOrder.value.get(t.id)] as const)
          .filter((e): e is readonly [string, number] => e[1] !== undefined),
      ),
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
  // accessOrder is included so a pane-2 switch (which changes neither the snapshot
  // nor activeTabId) still persists the new recency.
  watch([_persistedSnapshot, activeTabId, accessOrder], persistTabs)

  const activeTab = computed(
    (): Tab => tabs.value.find((t) => t.id === activeTabId.value) ?? tabs.value[0]!,
  )

  // ── Access order (MRU) ────────────────────────────────────────────────────
  // The `tabs` array stays in stable strip order (see switchTab) — sequential
  // navigation depends on it. Recency is tracked separately (accessOrder, declared
  // at the top), as a monotonic stamp per tab id, so views that want a
  // most-recently-used order (the address bar dropdown) can sort by it without
  // touching the real tab order. Persisted with the tab list, so the order
  // survives a restart; tabs never visited fall back to strip order.

  function markAccessed(id: string) {
    accessOrder.value.set(id, ++accessTick)
    // Reassign so the ref's own dependents (mruTabsForPane) re-evaluate — Map
    // mutation alone isn't tracked by Vue's reactivity for a plain ref.
    accessOrder.value = new Map(accessOrder.value)
  }

  /**
   * A pane's visible tabs ordered most-recently-active first. Unvisited tabs keep
   * their relative strip order behind the visited ones.
   */
  function mruTabsForPane(pane: 1 | 2): Tab[] {
    const paneTabs = pane === 1 ? pane1Tabs.value : pane2Tabs.value
    const order = accessOrder.value
    return paneTabs
      .map((t, i) => ({ t, i }))
      .sort((a, b) => {
        const sa = order.get(a.t.id)
        const sb = order.get(b.t.id)
        if (sa !== undefined && sb !== undefined) return sb - sa
        if (sa !== undefined) return -1
        if (sb !== undefined) return 1
        return a.i - b.i
      })
      .map(({ t }) => t)
  }

  // Stamp whichever tab each pane switches to. Deliberately NOT immediate: these
  // watchers are created during store setup, before init() runs, so an immediate
  // call would stamp the placeholder default tab and persist it before the saved
  // list is even read. The restored active tab already carries the highest saved
  // stamp, so it starts on top without a re-stamp.
  watch(activeTabId, (id) => id && markAccessed(id))
  watch(pane2ActiveTabId, (id) => id && markAccessed(id))

  // Orphan adoption: while split view is CLOSED, pane 1 displays and navigates the
  // pane-2 tabs too (they keep their pane: 2 identity so they return to pane 2 the
  // moment split view reopens). useBookViewStore() is called lazily inside the
  // computed bodies — never at store setup — to keep the module cycle harmless.

  /**
   * Pane 1's visible tabs: its own tabs, plus pane-2 "orphans" adopted while
   * split view is off.
   */
  const pane1Tabs = computed(() => {
    if (!useBookViewStore().splitViewEnabled) return tabs.value
    return tabs.value.filter((t) => !t.pane || t.pane === 1)
  })

  /** Pane 2's visible tabs — empty while split view is off (pane 1 adopts them). */
  const pane2Tabs = computed(() => {
    if (!useBookViewStore().splitViewEnabled) return []
    return tabs.value.filter((t) => t.pane === 2)
  })

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
    deleteAllStateForTab(id)
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

  /**
   * Called when split view re-opens: pane 2 takes its orphaned tabs back, so pane 1
   * must stop displaying one if the user was reading it. The orphan becomes pane 2's
   * active tab (it stays front-of-mind, just in its own shell again).
   */
  function reclaimPane1ActiveForSplit() {
    const active = tabs.value.find((t) => t.id === activeTabId.value)
    if (!active || active.pane !== 2) return
    pane2ActiveTabId.value = active.id
    const own = tabs.value.filter((t) => !t.pane || t.pane === 1)
    if (own.length > 0) {
      activeTabId.value = own[0]!.id
    } else {
      const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
      tabs.value.push(home)
      activeTabId.value = home.id
    }
  }

  /**
   * Move a tab between split panes — driven by a cross-region drag in the native chrome
   * tab strip. The tab becomes the active tab of its destination pane; the source pane's
   * active pointer is reassigned if it pointed at the moved tab, and pane 2 is never left
   * empty (a home tab is spawned, mirroring the closePane2Tab guarantee).
   */
  function moveTabToPane(id: string, targetPane: 1 | 2) {
    const tab = tabs.value.find((t) => t.id === id)
    if (!tab) return
    const fromPane: 1 | 2 = tab.pane === 2 ? 2 : 1
    if (fromPane === targetPane) return

    tab.pane = targetPane

    if (targetPane === 2) {
      // Left pane 1 — hand it another active tab if this was the one on screen.
      if (activeTabId.value === id) {
        const own = tabs.value.filter((t) => !t.pane || t.pane === 1)
        if (own.length > 0) {
          activeTabId.value = own[0]!.id
        } else {
          const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
          tabs.value.push(home)
          activeTabId.value = home.id
        }
      }
      pane2ActiveTabId.value = id
    } else {
      // Moved into pane 1 — becomes its active tab.
      activeTabId.value = id
      // Pane 2 must never be left empty while split view is open.
      const remaining = tabs.value.filter((t) => t.pane === 2)
      if (remaining.length === 0) {
        const home: Tab = { id: String(++nextId), pane: 2, title: 'בית', route: '/' }
        tabs.value.push(home)
        pane2ActiveTabId.value = home.id
      } else if (pane2ActiveTabId.value === id) {
        pane2ActiveTabId.value = remaining[0]!.id
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
    // Keep the array in stable tab-strip order — do NOT reorder on switch.
    // Sequential navigation (Ctrl+Tab, swipe) cycles by index, which only works
    // if a tab keeps its position; MRU reordering made "next" oscillate between
    // the two most-recent tabs instead of advancing through the list.
    const tab = tabs.value.find((t) => t.id === id)
    if (!tab) return
    // While split view is open, pane 1 must never activate a pane-2 tab — both
    // panes would render the same tab object. Route the switch to pane 2 instead.
    if (tab.pane === 2 && useBookViewStore().splitViewEnabled) {
      pane2ActiveTabId.value = id
      return
    }
    activeTabId.value = id
  }

  function closeAllTabs() {
    // While split view is off, pane 1 also owns the pane-2 orphans it adopted —
    // "close all" closes everything the user sees, orphans included.
    const splitOn = useBookViewStore().splitViewEnabled
    const closing = splitOn ? tabs.value.filter((t) => !t.pane || t.pane === 1) : [...tabs.value]
    for (const tab of closing) {
      if (tab.localFilePath) disposeLocalFileHost(tab.localFilePath)
      deleteAllStateForTab(tab.id)
    }
    const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
    // In split view, keep pane-2 tabs intact — only replace pane-1 tabs
    tabs.value = [home, ...(splitOn ? tabs.value.filter((t) => t.pane === 2) : [])]
    activeTabId.value = home.id
    pruneUncheckedCommentary(tabs.value.map((t) => t.id))
  }

  function closeTab(id: string) {
    const idx = tabs.value.findIndex((t) => t.id === id)
    if (idx === -1) return
    const tab = tabs.value[idx]!
    // A pane-2 tab while split view is open must close through the pane-2 path,
    // which keeps pane2ActiveTabId valid and pane 2 never empty.
    if (tab.pane === 2 && useBookViewStore().splitViewEnabled) {
      closePane2Tab(id)
      return
    }
    if (tab.localFilePath) disposeLocalFileHost(tab.localFilePath)
    deleteAllStateForTab(id)
    dropUncheckedCommentaryForTab(id)
    // The next active tab must be the closed tab's neighbor among the tabs pane 1
    // can display — picking by index from the mixed array could land on a pane-2
    // tab, making both panes render the same tab.
    const visIdx = pane1Tabs.value.findIndex((t) => t.id === id)
    tabs.value.splice(idx, 1)
    if (activeTabId.value === id) {
      const visible = pane1Tabs.value
      if (visible.length > 0) {
        activeTabId.value = visible[Math.min(Math.max(visIdx, 0), visible.length - 1)]!.id
      } else {
        const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
        tabs.value.push(home)
        activeTabId.value = home.id
      }
    } else if (tabs.value.length === 0) {
      const home: Tab = { id: String(++nextId), title: 'בית', route: '/' }
      tabs.value.push(home)
      activeTabId.value = home.id
    }
  }

  function updateActiveTab(patch: Partial<Omit<Tab, 'id'>>) {
    const tab = tabs.value.find((t) => t.id === activeTabId.value)
    if (tab) {
      Object.assign(tab, patch)
      // Stable order — see switchTab: navigating in place must not move the tab.
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
    // Only consider tabs pane 1 can display (its own + adopted orphans) — never
    // steal a home tab that lives in the open split pane.
    const existing = pane1Tabs.value.find((t) => t.route === '/')
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
    // pane1Tabs is split-aware: while split view is off it includes adopted orphans,
    // so an orphaned singleton (e.g. settings opened in pane 2) is reused, not duplicated.
    const paneTabs = pane === 1
      ? pane1Tabs.value
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
    mruTabsForPane,
    pane2ActiveTabId,
    activeTabForPane,
    openPane2Tab,
    switchPaneTab,
    closePane2Tab,
    ensurePane2HasTab,
    reclaimPane1ActiveForSplit,
    moveTabToPane,
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
    getLastReadPos,
    setLastReadPos,
    getTabViewState,
    setTabViewState,
    getBookViewState,
    setBookViewState,
    clearBookViewState,
    togglePdfViewerTitleBar,
  }
})
