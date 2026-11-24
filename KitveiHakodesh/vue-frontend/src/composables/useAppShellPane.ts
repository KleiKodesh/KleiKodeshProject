import { computed } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import {
  historyEntries as tabHistoryEntries,
  historyCursor as tabHistoryCursor,
} from '@/stores/navHistory'
import { navigatesDestinationsInPlace } from '@/webview-host/bridge'
import type { Tab, TabRoute } from '@/stores/tabStore'
import { t } from '@/i18n'
import { useAppWindowKey } from './appWindow'

/**
 * The tab operations AppTitleBar, AppPageView and the pages inside the shell use, so they
 * do not call tabStore directly.
 */
export function useAppShellPane() {
  const tabStore = useTabStore()
  const settingsStore = useSettingsStore()
  // The app window this shell draws (appWindow.ts): its own tabs and active tab.
  const windowKey = useAppWindowKey()

  const tabs = computed<Tab[]>(() => tabStore.tabsIn(windowKey))

  const activeTabId = computed<string>(() => tabStore.activeIdIn(windowKey))

  /**
   * The same tabs as `tabs`, ordered most-recently-active first. For list UIs
   * (the address-bar dropdown) — the real tab order is unchanged.
   */
  const mruTabs = computed<Tab[]>(() => tabStore.mruTabs().filter((t) => (t.windowKey ?? '') === windowKey))

  const activeTab = computed<Tab>(() => tabs.value.find((t) => t.id === activeTabId.value) ?? tabStore.activeTab)

  function switchTab(id: string) {
    tabStore.switchTab(id)
  }

  // ── Back / Forward within the active tab ───────────────────────────
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

  // The active tab's full history plus its cursor — read straight from the
  // navHistory module (tabStore only wraps the guards). Feeds the back/forward
  // buttons' hold-to-show dropdown.
  const historyEntries = computed(() => tabHistoryEntries(activeTabId.value))
  const historyCursor = computed(() => tabHistoryCursor(activeTabId.value))

  function goToHistoryIndex(index: number) {
    tabStore.goHistoryToIndex(activeTabId.value, index)
  }

  /**
   * Step the active tab by ±1 through the tabs, wrapping at both ends.
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
    tabStore.closeTab(id)
  }

  function closeAllTabs() {
    tabStore.focusWindow(windowKey)
    tabStore.closeAllTabs()
  }

  /**
   * VSTO / dev only. Removes the current page from both the per-tab history
   * stack and recentLocations, then navigates back to the previous entry (or
   * home if there is no previous entry).
   */
  function closeCurrentPage() {
    tabStore.closeCurrentPage(activeTabId.value)
  }

  /**
   * VSTO / dev only. Clears the entire per-tab history stack and all
   * recentLocations, then navigates home.
   */
  function closeAllPages() {
    tabStore.closeAllPages(activeTabId.value)
  }

  function openTab(partial: Omit<Tab, 'id'>) {
    return tabStore.openTab(windowKey ? { ...partial, windowKey } : partial)
  }

  function updateActiveTab(patch: Partial<Omit<Tab, 'id'>>) {
    tabStore.updateTab(activeTabId.value, patch)
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

  /**
   * Open a book at a line — the word-link / cross-reference path.
   *
   * The VSTO task pane shows one tab and has nowhere to put a second, so links
   * there take over the current tab (with a nonce, since AppPageView keys on
   * tabId:bookId and a same-book jump would otherwise not remount). The dev
   * browser mirrors that, so the task-pane path is what we exercise day to day.
   * Only the standalone demo host, which has a real tab strip, opens its own tab.
   *
   * The outgoing book's TOC state does not need clearing here: the patch changes
   * identity, and applyTabPatch drops position state on an identity change (see
   * POSITION_FIELDS in tabStore).
   */
  function openBookTarget(patch: Omit<Tab, 'id'>) {
    if (navigatesDestinationsInPlace)
      updateActiveTab({ ...patch, navNonce: tabStore.nextNavNonce() })
    else openTab(patch)
  }

  // Each gesture opens its own home tab — see openNewHomeTab in tabStore
  // for why home is deliberately not unique.
  function openNewHomeTab() {
    tabStore.focusWindow(windowKey)
    tabStore.openNewHomeTab()
  }

  function navigateToDestination(route: TabRoute, openInNewTab = false) {
    tabStore.focusWindow(windowKey)
    tabStore.navigateToDestination(route, openInNewTab)
  }

  const ROUTE_MAP: Record<string, { title: string; route: TabRoute }> = {
    homepage: { title: t('stores.tabTitle.home'), route: '/' },
    openfile: { title: t('stores.tabTitle.catalog'), route: '/books' },
    hebrewbooks: { title: t('stores.tabTitle.hebrewbooks'), route: '/hebrewbooks' },
    search: { title: t('stores.tabTitle.search'), route: '/search' as TabRoute },
  }

  function openNewTab() {
    const target = ROUTE_MAP[settingsStore.newTabPage] ?? { title: t('stores.tabTitle.home'), route: '/' as TabRoute }
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
      updateActiveTab({ route: '/', title: t('stores.tabTitle.home') })
    }
  }

  function togglePdfViewerTitleBar() {
    tabStore.togglePdfViewerTitleBar()
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
    historyEntries,
    historyCursor,
    goToHistoryIndex,
    closeTab,
    closeAllTabs,
    closeCurrentPage,
    closeAllPages,
    openTab,
    openNewTab,
    openNewHomeTab,
    updateActiveTab,
    openOrUpdateActiveTab,
    updateTab: (id: string, patch: Partial<Omit<Tab, 'id'>>) => tabStore.updateTab(id, patch),
    openBookTarget,
    navigateToDestination,
    goHome,
    togglePdfViewerTitleBar,
  }
}
