import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import type { TabRoute } from '@/stores/tabStore'

/**
 * Tab operations used by the native chrome-tabs mirror.
 *
 * The strip mirrors pane 1's view of the world: its own tabs, plus pane-2
 * "orphans" adopted while split view is off. Orphans navigate IN PANE 1 —
 * activating one never reopens the split shell. Plain functions, safe to call
 * outside component setup.
 */

/** Activate a tab in whichever pane currently displays it. */
export function activateTabAnyPane(tabId: string) {
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()
  const tab = tabStore.tabs.find((t) => t.id === tabId)
  if (!tab) return

  // A pane-2 tab while split view is open lives in the split shell.
  if (tab.pane === 2 && bookViewStore.splitViewEnabled) {
    bookViewStore.setFocusedPane(2)
    tabStore.switchPaneTab(tabId, 2)
    return
  }

  // Pane-1 tab, or an orphan adopted by pane 1 while split view is off.
  bookViewStore.setFocusedPane(1)
  tabStore.switchTab(tabId)
}

/** Close a tab in whichever pane currently displays it. */
export function closeTabAnyPane(tabId: string) {
  const tabStore = useTabStore()
  const tab = tabStore.tabs.find((t) => t.id === tabId)
  if (!tab) return
  // closePane2Tab keeps the "pane 2 never empty" guarantee — only wanted while
  // the split shell is actually open; adopted orphans close like any pane-1 tab.
  if (tab.pane === 2 && useBookViewStore().splitViewEnabled) tabStore.closePane2Tab(tabId)
  else tabStore.closeTab(tabId)
}

// Mirrors ROUTE_MAP in useAppShellPane.ts — the user's configured new-tab page.
const NEW_TAB_ROUTES: Record<string, { title: string; route: TabRoute }> = {
  homepage: { title: 'בית', route: '/' },
  openfile: { title: 'קטלוג הספרים', route: '/books' },
  hebrewbooks: { title: 'היברו-בוקס', route: '/hebrewbooks' },
  search: { title: 'חיפוש', route: '/search' },
}

/** Open a new tab in the given pane, honoring the user's configured new-tab page setting. */
export function openNewTabInPane(pane: 1 | 2 = 1) {
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()
  const target =
    NEW_TAB_ROUTES[useSettingsStore().newTabPage] ?? { title: 'בית', route: '/' as TabRoute }

  if (pane === 2 && bookViewStore.splitViewEnabled) {
    bookViewStore.setFocusedPane(2)
    const existingHome =
      target.route === '/' ? tabStore.pane2Tabs.find((t) => t.route === '/') : undefined
    if (existingHome) tabStore.switchPaneTab(existingHome.id, 2)
    else tabStore.openPane2Tab({ title: target.title, route: target.route })
    return
  }

  bookViewStore.setFocusedPane(1)
  if (target.route === '/') tabStore.openNewHomeTab()
  else tabStore.openTab({ title: target.title, route: target.route })
}
