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
  navigateToSingleton: (route: import('@/stores/tabStore').TabRoute, openInNewTab?: boolean) => void
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
    navigateToSingleton: pane.navigateToSingleton,
    switchTab: pane.switchTab,
    get activeTabId() { return pane.activeTabId.value },
    get activeTab() { return pane.activeTab.value },
    get tabs() { return pane.tabs.value },
  }
}
