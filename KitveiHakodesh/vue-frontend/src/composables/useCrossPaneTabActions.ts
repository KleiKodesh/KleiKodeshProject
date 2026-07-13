import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { isVstoEnvironment } from '@/webview-host/bridge'
import type { TabRoute } from '@/stores/tabStore'

/**
 * Tab operations that work across both split panes.
 *
 * Used by the pane-1 title-bar dropdown (which lists ALL tabs, including pane-2
 * ones) and by the native chrome-tabs mirror (whose strip also shows all tabs).
 * Plain functions, safe to call outside component setup.
 */

// Must match SPLIT_VIEW_MIN_WIDTH in App.vue / AppTitleBar.vue.
const SPLIT_VIEW_MIN_WIDTH = 768

/**
 * Activate a tab regardless of which pane it lives in.
 * Pane-2 tabs focus (and if needed re-open) the split view; when split view
 * cannot be shown (narrow window / VSTO), the tab is adopted into pane 1 instead
 * so the click never goes dead.
 */
export function activateTabAnyPane(tabId: string) {
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()
  const tab = tabStore.tabs.find((t) => t.id === tabId)
  if (!tab) return

  if (tab.pane === 2) {
    if (
      !bookViewStore.splitViewEnabled &&
      !isVstoEnvironment &&
      window.innerWidth >= SPLIT_VIEW_MIN_WIDTH
    ) {
      bookViewStore.toggleSplitView()
    }
    if (bookViewStore.splitViewEnabled) {
      bookViewStore.setFocusedPane(2)
      tabStore.switchPaneTab(tabId, 2)
      return
    }
    // Split view unavailable — move the tab into pane 1 and activate it there.
    tabStore.updateTab(tabId, { pane: 1 })
  }

  bookViewStore.setFocusedPane(1)
  tabStore.switchTab(tabId)
}

/** Close a tab regardless of which pane it lives in. */
export function closeTabAnyPane(tabId: string) {
  const tabStore = useTabStore()
  const tab = tabStore.tabs.find((t) => t.id === tabId)
  if (!tab) return
  if (tab.pane === 2) tabStore.closePane2Tab(tabId)
  else tabStore.closeTab(tabId)
}

// Mirrors ROUTE_MAP in useAppShellPane.ts — the user's configured new-tab page.
const NEW_TAB_ROUTES: Record<string, { title: string; route: TabRoute }> = {
  homepage: { title: 'בית', route: '/' },
  openfile: { title: 'ספרים', route: '/books' },
  hebrewbooks: { title: 'היברו-בוקס', route: '/hebrewbooks' },
  search: { title: 'חיפוש', route: '/search' },
}

/** Open a new pane-1 tab honoring the user's configured new-tab page setting. */
export function openNewTabPane1() {
  const tabStore = useTabStore()
  const target =
    NEW_TAB_ROUTES[useSettingsStore().newTabPage] ?? { title: 'בית', route: '/' as TabRoute }
  if (target.route === '/') tabStore.openNewHomeTab()
  else tabStore.openTab({ title: target.title, route: target.route })
}
