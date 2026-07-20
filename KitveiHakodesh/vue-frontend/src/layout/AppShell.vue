<script setup lang="ts">
import AppTitleBar from './AppTitleBar.vue'
import AppPageView from './AppPageView.vue'
import { provide } from 'vue'
import { useAppShellPane } from '@/composables/useAppShellPane'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
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
  navigateToSingleton: pane.navigateToSingleton,
  switchTab: pane.switchTab,
  get activeTabId() { return pane.activeTabId.value },
  get activeTab() { return pane.activeTab.value },
  get tabs() { return pane.tabs.value },
})
</script>

<template>
  <div class="app-shell" @pointerdown.capture="onPaneFocus">
    <AppTitleBar :pane-id="props.paneId" />
    <main class="app-shell-content">
      <AppPageView :pane-id="props.paneId" />
    </main>
  </div>
</template>

<style scoped>
.app-shell {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  min-width: 0;
  /* The chrome surface flows continuously from the title bar down around the
     content panel's rounded corners — no separator line between them. */
  background: var(--bg-secondary);
}
.app-shell-content {
  flex: 1;
  overflow: hidden;
  /* Edge-style content panel: inset into the shared chrome surface by a small
     margin on the sides and bottom, so the rounded border clearly frames the
     page on all visible edges. The top has no margin — the chrome flows
     straight from the title bar into the panel with no separator line. */
  margin: 0 var(--content-inset, 3px) var(--content-inset, 3px);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  background: var(--bg-primary);
}
</style>
