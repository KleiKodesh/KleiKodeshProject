import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import type { TabRoute } from '@/stores/tabStore'
import { t } from '@/i18n'

/**
 * Tab operations used by the native chrome-tabs mirror. Plain functions, safe to call
 * outside component setup.
 */

/** Activate a strip tab. */
export function activateTabAnyPane(tabId: string) {
  useTabStore().switchTab(tabId)
}

/** Close a strip tab. */
export function closeTabAnyPane(tabId: string) {
  useTabStore().closeTab(tabId)
}

// Mirrors ROUTE_MAP in useAppShellPane.ts — the user's configured new-tab page.
const NEW_TAB_ROUTES: Record<string, { title: string; route: TabRoute }> = {
  homepage: { title: t('stores.tabTitle.home'), route: '/' },
  openfile: { title: t('stores.tabTitle.catalog'), route: '/books' },
  hebrewbooks: { title: t('stores.tabTitle.hebrewbooks'), route: '/hebrewbooks' },
  search: { title: t('stores.tabTitle.search'), route: '/search' },
}

/** Open a new tab, honoring the user's configured new-tab page setting. */
export function openNewTabInPane() {
  const tabStore = useTabStore()
  const target =
    NEW_TAB_ROUTES[useSettingsStore().newTabPage] ?? { title: t('stores.tabTitle.home'), route: '/' as TabRoute }

  if (target.route === '/') tabStore.openNewHomeTab()
  else tabStore.openTab({ title: target.title, route: target.route })
}
