<script setup lang="ts">
import AppShell from '@/layout/AppShell.vue'
import ClockWidget from '@/components/ClockWidget.vue'
import { ref, defineAsyncComponent, onMounted } from 'vue'
import { resetting } from '@/features/settings/appResetState'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { storeToRefs } from 'pinia'
import { useTabSwipeNavigation } from '@/composables/useTabSwipeNavigation'

useTabSwipeNavigation()

// Loaded lazily — only needed when setupDone is false (first launch).
const SetupWizard = defineAsyncComponent(
  () => import('@/features/settings/SetupWizard.vue'),
)

const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const tabStore = useTabStore()
const { setupDone, showClock } = storeToRefs(settingsStore)

// Ensure pane 2 always has at least one tab when split view activates
onMounted(() => {
  if (bookViewStore.splitViewEnabled) tabStore.ensurePane2HasTab()
})

// Watch for split view being enabled and ensure pane 2 has a tab
import { watch } from 'vue'
watch(() => bookViewStore.splitViewEnabled, (enabled) => {
  if (enabled) tabStore.ensurePane2HasTab()
})

// ── Horizontal resize handle ──────────────────────────────────────────────────

const containerRef = ref<HTMLElement | null>(null)
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
      ? { gridTemplateColumns: `${bookViewStore.splitViewFraction * 100}% 4px 1fr` }
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
  background: color-mix(in srgb, var(--text-secondary) 25%, transparent);
}

.reset-overlay {
  position: fixed;
  inset: 0;
  z-index: 9999;
  background: rgba(0, 0, 0, 0.4);
  pointer-events: all;
}
</style>
