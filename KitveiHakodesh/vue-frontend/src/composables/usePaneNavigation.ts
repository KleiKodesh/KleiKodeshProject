import { inject, type InjectionKey } from 'vue'
import { useAppShellPane } from './useAppShellPane'
import type { Tab } from '@/stores/tabStore'

/**
 * Injection key for the pane-scoped navigation interface.
 * AppShell provides this via `provide(PANE_NAVIGATION_KEY, ...)`.
 * Feature components inject it via `usePaneNavigation()` to perform
 * tab operations that automatically target the correct pane.
 */
export interface PaneNavigation {
  updateActiveTab: (patch: Partial<Omit<Tab, 'id'>>) => void
  openTab: (partial: Omit<Tab, 'id'>) => Tab
  /**
   * Open a document either in a new tab or in-place in the active tab.
   * When `openInNewTab` is true (e.g. the user Ctrl/⌘-clicked an item) a fresh
   * tab is opened and focused; otherwise the active tab is updated in place.
   */
  openOrUpdateActiveTab: (patch: Partial<Omit<Tab, 'id'>>, openInNewTab?: boolean) => void
  navigateToDestination: (route: import('@/stores/tabStore').TabRoute, openInNewTab?: boolean) => void
  switchTab: (id: string) => void
  readonly activeTabId: string
  readonly activeTab: Tab
  readonly tabs: Tab[]
}

export const PANE_NAVIGATION_KEY: InjectionKey<PaneNavigation> = Symbol('paneNavigation')

/**
 * Returns the pane-scoped navigation interface injected by the nearest AppShell.
 * Falls back to pane 1 behaviour when called outside a shell.
 */
export function usePaneNavigation(): PaneNavigation {
  const injected = inject(PANE_NAVIGATION_KEY, null)
  if (injected) return injected

  // Fallback: construct pane 1 navigation directly
  const pane = useAppShellPane(1)
  return {
    updateActiveTab: pane.updateActiveTab,
    openTab: pane.openTab,
    openOrUpdateActiveTab: pane.openOrUpdateActiveTab,
    navigateToDestination: pane.navigateToDestination,
    switchTab: pane.switchTab,
    get activeTabId() { return pane.activeTabId.value },
    get activeTab() { return pane.activeTab.value },
    get tabs() { return pane.tabs.value },
  }
}
