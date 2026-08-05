import { computed } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import type { Tab, TabRoute } from '@/stores/tabStore'

/**
 * Provides pane-aware tab operations for AppTitleBar and AppPageView.
 *
 * Pane 1 — the primary pane. Uses the main tabStore tab list and activeTabId.
 * Pane 2 — the secondary pane in split view. Uses pane2Tabs and pane2ActiveTabId.
 *
 * All tab mutations (open, close, switch, update) route to the correct pane.
 * Components in the shell use this composable instead of calling tabStore directly.
 */
export function useAppShellPane(paneId: 1 | 2) {
  const tabStore = useTabStore()
  const settingsStore = useSettingsStore()

  const tabs = computed<Tab[]>(() =>
    paneId === 1 ? tabStore.pane1Tabs : tabStore.pane2Tabs,
  )

  const activeTabId = computed<string>(() =>
    paneId === 1 ? tabStore.activeTabId : tabStore.pane2ActiveTabId,
  )

  /**
   * The same tabs as `tabs`, ordered most-recently-active first. For list UIs
   * (the address-bar dropdown) — the real tab order is unchanged.
   */
  const mruTabs = computed<Tab[]>(() => tabStore.mruTabsForPane(paneId))

  const activeTab = computed<Tab>(() => tabStore.activeTabForPane(paneId))

  function switchTab(id: string) {
    tabStore.switchPaneTab(id, paneId)
  }

  // ── Back / Forward within the pane's active tab ───────────────────────────
  // The browser model: these move through the ACTIVE TAB's own history, not
  // between tabs. Ctrl+Tab still switches tabs (cycleTab below).

  const canGoBack = computed(() => tabStore.canGoBack(activeTabId.value))
  const canGoForward = computed(() => tabStore.canGoForward(activeTabId.value))

  function goBack() {
    tabStore.goHistory(activeTabId.value, -1)
  }

  function goForward() {
    tabStore.goHistory(activeTabId.value, 1)
  }

  /**
   * Step the active tab by ±1 through this pane's tabs, wrapping at both ends.
   * Walks `tabs` — the stable strip order — not `mruTabs`, so repeated steps
   * advance through the list instead of oscillating between the last two.
   * Still used by Ctrl+Tab, which keeps its tab-switching meaning.
   */
  function cycleTab(step: 1 | -1) {
    const paneTabs = tabs.value
    if (!paneTabs.length) return
    const currentIndex = paneTabs.findIndex((t) => t.id === activeTabId.value)
    const nextIndex = (currentIndex + step + paneTabs.length) % paneTabs.length
    switchTab(paneTabs[nextIndex]!.id)
  }

  function closeTab(id: string) {
    if (paneId === 1) tabStore.closeTab(id)
    else tabStore.closePane2Tab(id)
  }

  function closeAllTabs() {
    if (paneId === 1) {
      tabStore.closeAllTabs()
    } else {
      // Close all pane-2 tabs one by one
      for (const tab of [...tabStore.pane2Tabs]) {
        tabStore.closePane2Tab(tab.id)
      }
    }
  }

  function openTab(partial: Omit<Tab, 'id'>) {
    if (paneId === 1) return tabStore.openTab(partial)
    else return tabStore.openPane2Tab(partial)
  }

  function updateActiveTab(patch: Partial<Omit<Tab, 'id'>>) {
    if (paneId === 1) tabStore.updateActiveTab(patch)
    else tabStore.updatePane2ActiveTab(patch)
  }

  /**
   * Open a document in a new tab (and focus it) when `openInNewTab` is true —
   * this is the Ctrl/⌘-click path — otherwise update the active tab in place.
   * `openTab` requires title + route, so callers using this must supply them.
   */
  function openOrUpdateActiveTab(patch: Partial<Omit<Tab, 'id'>>, openInNewTab = false) {
    if (openInNewTab) openTab(patch as Omit<Tab, 'id'>)
    else updateActiveTab(patch)
  }

  function openNewHomeTab() {
    if (paneId === 1) {
      tabStore.openNewHomeTab()
    } else {
      const existing = tabStore.pane2Tabs.find((t) => t.route === '/')
      if (existing) tabStore.switchPaneTab(existing.id, 2)
      else tabStore.openPane2Tab({ title: 'בית', route: '/' })
    }
  }

  function navigateToDestination(route: TabRoute, openInNewTab = false) {
    tabStore.navigateToDestination(route, paneId, openInNewTab)
  }

  const ROUTE_MAP: Record<string, { title: string; route: TabRoute }> = {
    homepage: { title: 'בית', route: '/' },
    openfile: { title: 'קטלוג הספרים', route: '/books' },
    hebrewbooks: { title: 'היברו-בוקס', route: '/hebrewbooks' },
    search: { title: 'חיפוש', route: '/search' as TabRoute },
  }

  function openNewTab() {
    const target = ROUTE_MAP[settingsStore.newTabPage] ?? { title: 'בית', route: '/' as TabRoute }
    if (target.route === '/') openNewHomeTab()
    else openTab({ title: target.title, route: target.route })
  }

  function goHome() {
    const currentId = activeTabId.value
    const existing = tabs.value.find((t) => t.route === '/')
    if (existing) {
      if (existing.id !== currentId) {
        switchTab(existing.id)
        closeTab(currentId)
      }
    } else {
      updateActiveTab({ route: '/', title: 'בית' })
    }
  }

  function togglePdfViewerTitleBar() {
    const tab = tabs.value.find((t) => t.id === activeTabId.value)
    if (!tab) return
    if (paneId === 1) {
      tabStore.togglePdfViewerTitleBar()
    } else {
      tabStore.updateTab(tab.id, {
        pdfViewerTitleBarVisible: tab.pdfViewerTitleBarVisible !== false ? false : true,
      })
    }
  }

  return {
    tabs,
    mruTabs,
    activeTabId,
    activeTab,
    switchTab,
    cycleTab,
    canGoBack,
    canGoForward,
    goBack,
    goForward,
    closeTab,
    closeAllTabs,
    openTab,
    openNewTab,
    openNewHomeTab,
    updateActiveTab,
    openOrUpdateActiveTab,
    navigateToDestination,
    goHome,
    togglePdfViewerTitleBar,
  }
}
