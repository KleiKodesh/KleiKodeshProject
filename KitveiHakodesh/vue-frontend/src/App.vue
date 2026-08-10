<script setup lang="ts">
import AppShell from '@/layout/AppShell.vue'
import ClockWidget from '@/components/ClockWidget.vue'
import GlobalContextMenu from '@/components/GlobalContextMenu.vue'
import ToastBanner from '@/components/ToastBanner.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import SetupWizard from '@/features/settings/SetupWizard.vue'
import { ref, computed, watch } from 'vue'
import { useResizeObserver, useEventListener } from '@vueuse/core'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { storeToRefs } from 'pinia'
import { useTabSwipeNavigation } from '@/composables/useTabSwipeNavigation'
import { activateTabAnyPane } from '@/composables/useCrossPaneTabActions'
import { isVstoEnvironment as isVsto } from '@/webview-host/bridge'

useTabSwipeNavigation()

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

// ── Unsaved PDF edits guards ─────────────────────────────────────────────────

// Tab-close confirmation: tabStore's close paths park a pending request in
// bookViewStore instead of closing when a tab has unsaved PDF (TOC) edits.
const pdfClosePending = computed(() => bookViewStore.pdfClosePending)
const pdfCloseDesc = computed(() => {
  const titles = (pdfClosePending.value?.tabTitles ?? []).filter(Boolean)
  const names = titles.length ? ` (${titles.join(', ')})` : ''
  return `השינויים שבוצעו בתוכן העניינים${names} לא נשמרו ויאבדו אם הכרטיסייה תיסגר.`
})

// "שמירה בשם..." from the close dialog: save the tab's PDF through the
// viewer, then complete the close. The tab may be a BACKGROUND one whose
// iframe is long gone — activate it first and wait for the viewer (and the
// parked-edit rehydration) to come back before asking it to save.
const pdfSaveAsBusy = ref(false)

async function pdfCloseSaveAs() {
  if (pdfSaveAsBusy.value) return
  const pending = bookViewStore.takePdfClosePending()
  if (!pending) return
  if (!pending.tabId) {
    // Multi-tab request (close-all) — no single document to save; requeue
    // semantics: treat as cancel.
    return
  }
  pdfSaveAsBusy.value = true
  try {
    activateTabAnyPane(pending.tabId)
    // Wait for the viewer bridge AND for it to report dirty — the bridge
    // registers on documentloaded, but the parked-edit rehydration lands
    // later; saving in that window would write the file WITHOUT the edits
    // (and the clean-document download path never signals completion).
    const bridge = await (async () => {
      const deadline = Date.now() + 20_000
      for (;;) {
        const b = bookViewStore.getPdfBridge(pending.tabId!)
        if (b?.saveAs && b.hasUnsavedChanges?.()) return b
        if (Date.now() > deadline) return null
        await new Promise((r) => setTimeout(r, 150))
      }
    })()
    if (!bridge) return // viewer/rehydration never came back — leave the tab open
    const saved = await bridge.saveAs!()
    if (saved) pending.proceed() // completes the close, pre-approved
    // Not saved (picker cancelled / timeout): leave the tab open, no dialog.
  } finally {
    pdfSaveAsBusy.value = false
  }
}

// App close / reload / workspace switch (workspace switching ends in
// window.location.reload()): the browser prompt is the only interception
// point at window level. Any live-dirty viewer or parked dirty snapshot
// blocks a silent exit. NOTE: covers browser/dev contexts; the WinForms
// WebView2 host closing its window does not run beforeunload — that needs a
// FormClosing hook on the C# side (documented follow-up).
useEventListener(window, 'beforeunload', (e: BeforeUnloadEvent) => {
  if (bookViewStore.hasAnyUnsavedPdfChanges()) {
    e.preventDefault()
    e.returnValue = ''
  }
})
</script>

<template>
  <div
    ref="containerRef"
    class="app-layout"
    :class="{ 'split-active': bookViewStore.splitViewEnabled }"
    :style="bookViewStore.splitViewEnabled
      ? { gridTemplateColumns: `1fr 1px ${bookViewStore.splitViewFraction * 100}%` }
      : undefined"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
  >
    <!-- Pane 1 — always present -->
    <AppShell :pane-id="1" />

    <!-- Resize divider — only in split view -->
    <div
      v-if="bookViewStore.splitViewEnabled"
      class="sash sash-v"
      data-split-divider
      @pointerdown="onDividerPointerDown"
    />

    <!-- Pane 2 — only in split view -->
    <AppShell v-if="bookViewStore.splitViewEnabled" :pane-id="2" />

    <ClockWidget v-if="showClock" />
    <SetupWizard v-if="!setupDone" />
    <GlobalContextMenu />
    <ToastBanner />
    <ConfirmDialog
      v-if="pdfClosePending"
      title="שינויים שלא נשמרו"
      :desc="pdfCloseDesc"
      confirm-label="סגירה ללא שמירה"
      :extra-label="pdfClosePending.tabId ? 'שמירה בשם...' : undefined"
      @confirm="bookViewStore.resolvePdfCloseConfirm(true)"
      @cancel="bookViewStore.resolvePdfCloseConfirm(false)"
      @extra="pdfCloseSaveAs"
    />
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

</style>
