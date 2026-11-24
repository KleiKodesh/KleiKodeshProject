import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import type { TabRoute } from '@/stores/tabStore'
import { t } from '@/i18n'

/**
 * Tab operations used by the native chrome-tabs mirror. Plain functions, safe to call
 * outside component setup.
 */

/** Activate a strip tab. */
export function activateStripTab(tabId: string) {
  useTabStore().switchTab(tabId)
}

/** Close a strip tab. */
export function closeStripTab(tabId: string) {
  useTabStore().closeTab(tabId)
}

// Mirrors ROUTE_MAP in useAppShellPane.ts — the user's configured new-tab page.
const NEW_TAB_ROUTES: Record<string, { title: string; route: TabRoute }> = {
  homepage: { title: t('stores.tabTitle.home'), route: '/' },
  openfile: { title: t('stores.tabTitle.catalog'), route: '/books' },
  hebrewbooks: { title: t('stores.tabTitle.hebrewbooks'), route: '/hebrewbooks' },
  search: { title: t('stores.tabTitle.search'), route: '/search' },
}

/** Open a new tab, honoring the user's configured new-tab page setting, in the strip's window ('' = main). */
export function openNewStripTab(windowKey = '') {
  const tabStore = useTabStore()
  tabStore.focusWindow(windowKey)
  const target =
    NEW_TAB_ROUTES[useSettingsStore().newTabPage] ?? { title: t('stores.tabTitle.home'), route: '/' as TabRoute }

  if (target.route === '/') tabStore.openNewHomeTab()
  else tabStore.openTab({ title: target.title, route: target.route })
}
