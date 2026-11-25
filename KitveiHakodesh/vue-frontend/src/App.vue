<script setup lang="ts">
import AppShell from '@/layout/AppShell.vue'
import ClockWidget from '@/components/ClockWidget.vue'
import GlobalContextMenu from '@/components/GlobalContextMenu.vue'
import ToastBanner from '@/components/ToastBanner.vue'
import SiteFrameLayer from '@/features/websites/SiteFrameLayer.vue'
import WidgetWindowLayer from '@/features/widgets/WidgetWindowLayer.vue'
import AppWindowLayer from '@/layout/appWindows/AppWindowLayer.vue'
import ServiceUnavailableBanner from '@/components/ServiceUnavailableBanner.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import SetupWizard from '@/features/settings/SetupWizard.vue'
import HomePageDateBar from '@/features/home/HomePageDateBar.vue'
import { ref, computed, watch, onMounted } from 'vue'
import { useResizeObserver, useEventListener } from '@vueuse/core'
import { useSettingsStore } from '@/stores/settingsStore'
import { useUserSpaceStore } from '@/stores/userSpaceStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { storeToRefs } from 'pinia'
import { useTabSwipeNavigation } from '@/composables/useTabSwipeNavigation'
import { useIndexBuildReasonLog } from '@/composables/useIndexBuildReasonLog'
import { useKeepScreenOn } from '@/composables/useKeepScreenOn'
import { isVstoEnvironment as isVsto, hasNativeChromeTabs } from '@/webview-host/bridge'
import { startErrorReportRetries } from '@/features/book-view/errorReport/errorReportService'
import { detectAvailableFonts } from '@/webview-host/fontsApi'
import { ownLibraryOffer } from '@/webview-host/ownLibraryOffer'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

// Prewarm the per-session font list at launch so the settings font dropdowns open instantly.
void detectAvailableFonts()

useTabSwipeNavigation()
// Error reports that could not go out (offline, no endpoint yet) are retried from
// here — once for the whole app, not once per book pane.
startErrorReportRetries()
// Why each service index last rebuilt, to the console (diagnostics).
useIndexBuildReasonLog()
useKeepScreenOn()

const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const tabStore = useTabStore()
const { setupDone, termsAccepted, showClock } = storeToRefs(settingsStore)

// The default UserSpace folders (Torat Emet books, when installed) are added once at app start, off the render path.
const userSpaceStore = useUserSpaceStore()
onMounted(() => { void userSpaceStore.claimDefaultFolders() })

// ── Unsaved PDF edits guards ─────────────────────────────────────────────────

// Unsaved-changes prompt (Save / Don't save / Cancel): tabStore's close and leave paths park
// a pending request in bookViewStore instead of closing when a tab's PDF has unsaved edits.
const pdfClosePending = computed(() => bookViewStore.pdfClosePending)
const pdfCloseDesc = computed(() => {
  const titles = (pdfClosePending.value?.tabTitles ?? []).filter(Boolean)
  const names = titles.length ? ` (${titles.join(', ')})` : ''
  return t('app.unsavedDesc', { names })
})

// Save from the unsaved-changes dialog: the tab's viewer is the mounted one (only a tab's
// active page is mounted, and only a mounted viewer can be dirty). Saved: the close /
// navigation goes on; not saved (cancelled Save As, failed write): the tab stays as it is.
const pdfSaveBusy = ref(false)

async function pdfCloseSave() {
  if (pdfSaveBusy.value) return
  const pending = bookViewStore.takePdfClosePending()
  if (!pending?.tabId) return
  const bridge = bookViewStore.getPdfBridge(pending.tabId)
  if (!bridge) return pending.proceed() // nothing mounted, nothing unsaved
  pdfSaveBusy.value = true
  try {
    if (await bridge.save()) pending.proceed()
  } finally {
    pdfSaveBusy.value = false
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
  <!-- The pinned date bar is the WINDOW's status bar, so it lives here and not in AppShell. -->
  <div class="app-frame">
    <div class="app-layout">
      <AppShell />
    </div>
    <!-- Website iframes: after the panes (their CSS anchors must come first), never moved. -->
    <SiteFrameLayer />
    <HomePageDateBar v-if="settingsStore.dateBarPinned" status-bar class="app-status-bar" />

    <!-- Widgets & tools: floating tool windows over the page. -->
    <WidgetWindowLayer />
    <!-- The extra app windows this page draws (tabs dragged out of the strip). -->
    <AppWindowLayer v-if="hasNativeChromeTabs" />
    <ClockWidget v-if="showClock" />
    <SetupWizard v-if="!setupDone || !termsAccepted" />
    <GlobalContextMenu />
    <ToastBanner />
    <ServiceUnavailableBanner />
    <ConfirmDialog
      v-if="pdfClosePending"
      :title="t('app.unsavedTitle')"
      :desc="pdfCloseDesc"
      :confirm-label="t('app.closeWithoutSaving')"
      :extra-label="pdfClosePending.tabId ? t('app.save') : undefined"
      @confirm="bookViewStore.resolvePdfCloseConfirm(true)"
      @cancel="bookViewStore.resolvePdfCloseConfirm(false)"
      @extra="pdfCloseSave"
    />
    <!-- Switched away from the library this app downloaded: offer to free its disk space. -->
    <ConfirmDialog
      v-if="ownLibraryOffer"
      :title="t('settings.advanced.freeOwnLibraryTitle')"
      :desc="t('settings.advanced.freeOwnLibraryDesc', { size: (ownLibraryOffer.sizeBytes / 1024 ** 3).toFixed(1) })"
      :confirm-label="t('settings.advanced.freeOwnLibraryConfirm')"
      @confirm="ownLibraryOffer.answer(true)"
      @cancel="ownLibraryOffer.answer(false)"
    />
  </div>
</template>

<style scoped>
.app-frame {
  display: flex;
  flex-direction: column;
  height: 100%;
}
.app-layout {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}
.app-status-bar {
  flex-shrink: 0;
}

.app-layout.split-active {
  display: grid;
  flex-direction: unset;
}

</style>
