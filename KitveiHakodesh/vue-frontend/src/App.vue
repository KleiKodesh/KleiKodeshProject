<script setup lang="ts">
import AppShell from '@/layout/AppShell.vue'
import ClockWidget from '@/components/ClockWidget.vue'
import GlobalContextMenu from '@/components/GlobalContextMenu.vue'
import ToastBanner from '@/components/ToastBanner.vue'
import { ref, computed, defineAsyncComponent, watch } from 'vue'
import { useResizeObserver } from '@vueuse/core'
import { resetting } from '@/features/settings/appResetState'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { storeToRefs } from 'pinia'
import { useTabSwipeNavigation } from '@/composables/useTabSwipeNavigation'
import { isVstoEnvironment as isVsto } from '@/webview-host/bridge'

useTabSwipeNavigation()

// Loaded lazily — only needed when setupDone is false (first launch).
const SetupWizard = defineAsyncComponent(
  () => import('@/features/settings/SetupWizard.vue'),
)

const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const tabStore = useTabStore()
const { setupDone, showClock } = storeToRefs(settingsStore)

// ── Split view availability ───────────────────────────────────────────────────
// Split view requires enough horizontal space for two usable panes.
// Below 768px the panes would be too narrow to be comfortable.
const SPLIT_VIEW_MIN_WIDTH = 768

const containerRef = ref<HTMLElement | null>(null)
const appWidth = ref(window.innerWidth)
useResizeObserver(containerRef, ([entry]) => {
  appWidth.value = entry!.contentRect.width
})
const isSplitViewAvailable = computed(() => !isVsto && appWidth.value >= SPLIT_VIEW_MIN_WIDTH)

// Auto-disable split view when the window shrinks below the minimum width.
watch(isSplitViewAvailable, (available) => {
  if (!available && bookViewStore.splitViewEnabled) bookViewStore.disableSplitView()
})

// Restoring with split view on: pane 2 must have a tab, and pane 1's restored
// activeTabId must not point at a pane-2 tab (possible when the active tab at
// exit was a non-persisted singleton and the persist fallback picked a pane-2
// tab) — that would render the same tab in both panes. Runs synchronously at
// setup, before either AppShell first renders.
if (bookViewStore.splitViewEnabled) {
  tabStore.reclaimPane1ActiveForSplit()
  tabStore.ensurePane2HasTab()
}

// When split view is (re-)enabled, pane 2 takes its orphaned tabs back: pane 1 must
// stop displaying an adopted orphan, and pane 2 must have at least one tab.
watch(() => bookViewStore.splitViewEnabled, (enabled) => {
  if (enabled) {
    tabStore.reclaimPane1ActiveForSplit()
    tabStore.ensurePane2HasTab()
  }
})

// ── Horizontal resize handle ──────────────────────────────────────────────────
let isDragging = false
let dragStartX = 0
let dragStartFraction = 0

function onDividerPointerDown(event: PointerEvent) {
  isDragging = true
  dragStartX = event.clientX
  dragStartFraction = bookViewStore.splitViewFraction
  ;(event.target as HTMLElement).setPointerCapture(event.pointerId)
}

function onPointerMove(event: PointerEvent) {
  if (!isDragging) return
  const containerWidth = containerRef.value?.getBoundingClientRect().width ?? window.innerWidth
  // In RTL, fraction controls pane 2 (physical left side). Dragging right
  // grows pane 2, so fraction increases with positive clientX delta.
  const delta = event.clientX - dragStartX
  const newFraction = Math.min(0.85, Math.max(0.15, dragStartFraction + delta / containerWidth))
  bookViewStore.setSplitViewFraction(newFraction)
}

function onPointerUp() {
  isDragging = false
}
</script>

<template>
  <div
    ref="containerRef"
    class="app-layout"
    :class="{ 'split-active': bookViewStore.splitViewEnabled }"
    :style="bookViewStore.splitViewEnabled
      ? { gridTemplateColumns: `1fr 4px ${bookViewStore.splitViewFraction * 100}%` }
      : undefined"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
  >
    <!-- Pane 1 — always present -->
    <AppShell :pane-id="1" />

    <!-- Resize divider — only in split view -->
    <div
      v-if="bookViewStore.splitViewEnabled"
      class="split-divider"
      @pointerdown="onDividerPointerDown"
    />

    <!-- Pane 2 — only in split view -->
    <AppShell v-if="bookViewStore.splitViewEnabled" :pane-id="2" />

    <ClockWidget v-if="showClock" />
    <SetupWizard v-if="!setupDone" />
    <GlobalContextMenu />
    <ToastBanner />
    <div v-if="resetting" class="reset-overlay" />
  </div>
</template>

<style scoped>
.app-layout {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.app-layout.split-active {
  display: grid;
  flex-direction: unset;
}

.split-divider {
  width: 4px;
  cursor: col-resize;
  background: var(--border-color);
  touch-action: none;
  position: relative;
  flex-shrink: 0;
  transition: background 120ms;
}

.split-divider::before {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: 50%;
  transform: translateX(-50%);
  width: 20px;
}

.split-divider:hover,
.split-divider:active {
  background: color-mix(in srgb, var(--accent-color) 50%, transparent);
}

.reset-overlay {
  position: fixed;
  inset: 0;
  z-index: 9999;
  background: rgba(0, 0, 0, 0.4);
  pointer-events: all;
}
</style>
