<script setup lang="ts">
/**
 * A widget shown in a tab: what a widget window becomes when it is dragged onto a tab
 * strip. Same page as in the widget window (WidgetWindow.vue), with the same guard: the
 * tool pages were written as tab pages and write "their" tab, so updates reach this tab
 * but can never move it off its widget.
 */
import { computed, provide } from 'vue'
import { PANE_NAVIGATION_KEY, type PaneNavigation } from '@/composables/usePaneNavigation'
import { useAppShellPane } from '@/composables/useAppShellPane'
import type { Tab } from '@/stores/tabStore'
import { findWidgetItem } from './widgetItems'
import type { WidgetId } from './widgetWindowStore'

const pane = useAppShellPane()
const tabId = pane.activeTabId.value
const tab = computed<Tab | undefined>(() => pane.tabs.value.find((t) => t.id === tabId))
const item = computed(() => (tab.value?.widgetId ? findWidgetItem(tab.value.widgetId as WidgetId) : undefined))

// What the page sees as its tab: this tab, but on the widget's own route (a page that
// branches on its route sees what it saw in the widget window).
const pageTab = computed<Tab>(() => ({ ...(tab.value ?? { id: tabId, title: '', route: '/widget' }), route: item.value?.route ?? '/' }))

const navigation: PaneNavigation = {
  updateActiveTab: (patch) => {
    const { route: _route, widgetId: _widgetId, ...rest } = patch
    pane.updateTab(tabId, rest)
  },
  openTab: pane.openTab,
  openOrUpdateActiveTab: (patch, openInNewTab) => pane.openOrUpdateActiveTab(patch, openInNewTab),
  openBookTarget: pane.openBookTarget,
  navigateToDestination: pane.navigateToDestination,
  switchTab: pane.switchTab,
  get activeTabId() { return tabId },
  get activeTab() { return pageTab.value },
  get tabs() { return pane.tabs.value },
}
provide(PANE_NAVIGATION_KEY, navigation)
</script>

<template>
  <section v-if="item" class="widget-tab" :aria-label="item.text" :data-widget="item.id">
    <component :is="item.page" v-bind="item.props" />
  </section>
</template>

<style scoped>
.widget-tab {
  height: 100%;
  min-height: 0;
  overflow: auto;
  display: flex;
  flex-direction: column;
  background: var(--bg-primary);
}
.widget-tab > :deep(*) { flex: 1; min-height: 0; }
</style>
