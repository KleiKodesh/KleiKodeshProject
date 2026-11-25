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
import { watchFullTextSearchIndexStatus } from '@/features/full-text-search/useFullTextSearchIndexingStatus'


const pane = useAppShellPane()
// App-wide: a failed full-text-search index build must toast even with its page closed.
// Idempotent, so a remount does not open a second stream.
watchFullTextSearchIndexStatus()
const bookViewStore = useBookViewStore()
const settingsStore = useSettingsStore()



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
  <div class="app-shell">
    <!-- Title bar (address bar) spans the full width on top; the nav rail sits below it,
         beside the content. One rail per pane: a pane here is a whole shell. -->
    <AppTitleBar />
    <div class="app-shell-main">
      <AppNavSidebar v-if="settingsStore.getNavSidebarVisible()" />
      <main class="app-shell-content">
        <AppPageView />
      </main>
    </div>
  </div>
</template>

<style scoped>
.app-shell {
  display: flex;
  /* Title bar on top, then the rail-plus-content row. */
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  min-width: 0;
  /* Named container so the shell's own chrome - the nav rail, which is a sibling of the
     content and so cannot read the `app-shell` container declared below - can size itself
     off the shell's own width. The rail and side panels take part of the window, so the
     viewport is the wrong thing to ask: a narrow pane in a wide window needs the compact
     rail just as much as a narrow window does. */
  container: app-pane / inline-size;
  /* The chrome surface flows continuously from the title bar down around the
     content panel's rounded corners — no separator line between them. */
  background: var(--bg-secondary);
}
.app-shell-main {
  flex: 1;
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: row;
  overflow: hidden;
}
.app-shell-content {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  /* Named container so page content (lines view, search results) can widen its
     side padding based on the shell's width, not the whole viewport — matters
     for a shell that is only part of the window. */
  container: app-shell / inline-size;
  background: var(--bg-primary);
}
</style>
