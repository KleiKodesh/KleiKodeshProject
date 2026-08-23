<script setup lang="ts">
import AppTitleBar from './AppTitleBar.vue'
import AppNavSidebar from './AppNavSidebar.vue'
import AppPageView from './AppPageView.vue'
import { provide } from 'vue'
import { useAppShellPane } from '@/composables/useAppShellPane'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { PANE_NAVIGATION_KEY } from '@/composables/usePaneNavigation'

const props = withDefaults(defineProps<{ paneId?: 1 | 2 }>(), { paneId: 1 })

// Ensure pane 2 always has at least one tab before any child component renders.
// This must run synchronously at setup time so AppPageView reads a valid pane-2
// tab on its first render, not the placeholder fallback.
if (props.paneId === 2) {
  useTabStore().ensurePane2HasTab()
}

const pane = useAppShellPane(props.paneId)
const bookViewStore = useBookViewStore()
const settingsStore = useSettingsStore()

function onPaneFocus() {
  bookViewStore.setFocusedPane(props.paneId as 1 | 2)
}

/** Every component inside this shell injects 'paneId' to know which pane it lives in. */
provide('paneId', props.paneId)

/** Every component inside this shell can inject PANE_NAVIGATION_KEY to get
 *  pane-scoped tab operations without importing useTabStore directly. */
provide(PANE_NAVIGATION_KEY, {
  updateActiveTab: pane.updateActiveTab,
  openTab: pane.openTab,
  openOrUpdateActiveTab: pane.openOrUpdateActiveTab,
  openBookTarget: pane.openBookTarget,
  navigateToDestination: pane.navigateToDestination,
  switchTab: pane.switchTab,
  get activeTabId() { return pane.activeTabId.value },
  get activeTab() { return pane.activeTab.value },
  get tabs() { return pane.tabs.value },
})
</script>

<template>
  <div class="app-shell" @pointerdown.capture="onPaneFocus">
    <!-- The nav menu as an always-on panel docked to this pane's edge: it owns the edge
         for the pane's full height, and everything belonging to the document - title bar
         included, since that is where the tab's title, breadcrumb and per-tab controls
         live - starts BESIDE it. One rail per pane: a pane here is a whole shell, so the
         rail splits with it. -->
    <AppNavSidebar v-if="settingsStore.getNavSidebarVisible(props.paneId)" />
    <div class="app-shell-main">
      <AppTitleBar :pane-id="props.paneId" />
      <main class="app-shell-content">
        <AppPageView :pane-id="props.paneId" />
      </main>
    </div>
  </div>
</template>

<style scoped>
.app-shell {
  display: flex;
  /* A row, not a column: the nav rail owns the pane's edge for its full height and the
     title-bar-plus-content column sits beside it. */
  flex-direction: row;
  height: 100%;
  overflow: hidden;
  min-width: 0;
  /* The chrome surface flows continuously from the title bar down around the
     content panel's rounded corners — no separator line between them. */
  background: var(--bg-secondary);
}
.app-shell-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.app-shell-content {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  /* Named container so page content (lines view, search results) can widen its
     side padding based on THIS pane's width, not the whole viewport — matters
     for split-shell where each pane is only part of the window. */
  container: app-shell / inline-size;
  background: var(--bg-primary);
}
</style>
