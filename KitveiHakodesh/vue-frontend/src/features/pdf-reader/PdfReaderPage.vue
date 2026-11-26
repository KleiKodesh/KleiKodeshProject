<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useTabStore } from '@/stores/tabStore'
import PdfReaderView from './PdfReaderView.vue'
// After PdfReaderView (and so pdf_viewer.css): the viewer in the app's theme and page filter.
import './pdfReaderTheme.css'

/**
 * The /pdf-view route on the new viewer: the tab's PDF in PdfReaderView. Keyed by the tab, not
 * the url: the view reopens a new url itself, and a Save As re-points the tab at the new file
 * while the view keeps showing it (no remount, page and zoom kept).
 */
const paneNavigation = usePaneNavigation()
const url = computed(() => paneNavigation.activeTab.localFileVirtualUrl ?? null)
const tabStore = useTabStore()

// One-shot outline jump (tab.pdfInitialOutlineTarget, set when a HebrewBooks edition is opened
// from a text book): taken off the tab as soon as the view is about to show the file and handed
// to the view, which jumps there once the document is in. Cleared from the tab so a reload or a
// session restore opens at the saved reading place instead of jumping again.
const outlineTarget = ref<string | null>(null)
watch(
  () => [paneNavigation.activeTab.id, url.value, paneNavigation.activeTab.pdfInitialOutlineTarget] as const,
  ([tabId, fileUrl, target], previous) => {
    // Another tab or another file: the target taken earlier belongs to that one.
    if (previous && (previous[0] !== tabId || previous[1] !== fileUrl)) outlineTarget.value = null
    if (!fileUrl || !target) return
    outlineTarget.value = target
    tabStore.updateTabWithoutHistory(tabId, { pdfInitialOutlineTarget: undefined })
  },
  { immediate: true },
)

// Before the file is served (a HebrewBooks download, a Word conversion), the pane shows the
// viewer's own loading bar: filled by the download's percentage when the service reports one
// ("6.2 / 13.2 MB · 47%"), sliding otherwise. The viewer's bar takes over once the file loads.
const preparing = computed(() => !url.value && !!paneNavigation.activeTab.localFileConverting)
const downloadPercent = computed(() => {
  const m = /(\d+(?:\.\d+)?)\s*%/.exec(paneNavigation.activeTab.localFileDownloadProgress ?? '')
  return m ? Math.min(100, Number(m[1])) : NaN
})
</script>

<template>
  <PdfReaderView
    v-if="url"
    :key="paneNavigation.activeTab.id"
    :url="url"
    :tab-id="paneNavigation.activeTab.id"
    :file-name="paneNavigation.activeTab.localFileName ?? null"
    :file-path="paneNavigation.activeTab.localFilePath ?? null"
    :outline-target="outlineTarget"
  />
  <div v-else-if="preparing" class="pdf-reader-preparing">
    <div
      class="pdf-reader-progress"
      :class="{ indeterminate: Number.isNaN(downloadPercent) }"
      role="progressbar"
      :aria-label="$t('pdfReader.load.progress')"
      :aria-valuenow="Number.isNaN(downloadPercent) ? undefined : Math.round(downloadPercent)"
      :title="paneNavigation.activeTab.localFileDownloadProgress ?? undefined"
    >
      <div class="pdf-reader-progress-fill" :style="Number.isNaN(downloadPercent) ? undefined : { width: `${downloadPercent}%` }" />
    </div>
  </div>
</template>

<style scoped>
/* The pages area while the file is being prepared, with PdfReaderView's loading bar (same look). */
.pdf-reader-preparing {
  position: relative;
  width: 100%;
  height: 100%;
  background: var(--bg-secondary);
}
.pdf-reader-progress {
  position: absolute;
  top: 0;
  inset-inline: 0;
  height: 3px;
  overflow: hidden;
  pointer-events: none;
}
.pdf-reader-progress-fill {
  height: 100%;
  width: 0;
  background: var(--accent-color, #0078d4);
  transition: width 150ms linear;
}
.pdf-reader-progress.indeterminate .pdf-reader-progress-fill {
  width: 30%;
  transition: none;
  animation: pdf-reader-preparing-slide 1.2s ease-in-out infinite;
}
@keyframes pdf-reader-preparing-slide {
  from { transform: translateX(-100%); }
  to { transform: translateX(340%); }
}
</style>
