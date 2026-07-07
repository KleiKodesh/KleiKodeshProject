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

  const activeTab = computed<Tab>(() => tabStore.activeTabForPane(paneId))

  function switchTab(id: string) {
    tabStore.switchPaneTab(id, paneId)
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

  function openNewHomeTab() {
    if (paneId === 1) {
      tabStore.openNewHomeTab()
    } else {
      const existing = tabStore.pane2Tabs.find((t) => t.route === '/')
      if (existing) tabStore.switchPaneTab(existing.id, 2)
      else tabStore.openPane2Tab({ title: 'בית', route: '/' })
    }
  }

  function navigateToSingleton(route: TabRoute, openInNewTab = false) {
    tabStore.navigateToSingleton(route, paneId, openInNewTab)
  }

  const ROUTE_MAP: Record<string, { title: string; route: TabRoute }> = {
    homepage: { title: 'בית', route: '/' },
    openfile: { title: 'ספרים', route: '/books' },
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
    activeTabId,
    activeTab,
    switchTab,
    closeTab,
    closeAllTabs,
    openTab,
    openNewTab,
    openNewHomeTab,
    updateActiveTab,
    navigateToSingleton,
    goHome,
    togglePdfViewerTitleBar,
  }
}
