<script setup lang="ts">
import { computed } from 'vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import PdfReaderView from './PdfReaderView.vue'

/**
 * The /pdf-view route on the new viewer: the tab's PDF in PdfReaderView. Keyed by the tab, not
 * the url: the view reopens a new url itself, and a Save As re-points the tab at the new file
 * while the view keeps showing it (no remount, page and zoom kept).
 */
const paneNavigation = usePaneNavigation()
const url = computed(() => paneNavigation.activeTab.localFileVirtualUrl ?? null)
</script>

<template>
  <PdfReaderView
    v-if="url"
    :key="paneNavigation.activeTab.id"
    :url="url"
    :tab-id="paneNavigation.activeTab.id"
    :file-name="paneNavigation.activeTab.localFileName ?? null"
    :file-path="paneNavigation.activeTab.localFilePath ?? null"
  />
</template>
