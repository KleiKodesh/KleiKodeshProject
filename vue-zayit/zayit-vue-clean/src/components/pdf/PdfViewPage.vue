<script setup lang="ts">
import { computed } from 'vue'
import { usePdfStore } from '@/stores/pdfStore'
import { syncPdfViewerTheme } from '@/theme/themes'

const pdfStore = usePdfStore()

const iframeSrc = computed(() => {
  const url = pdfStore.blobUrl
  if (!url) return null
  const p = new URLSearchParams({ file: url, locale: 'he', enableHWA: 'true', cMapPacked: 'true' })
  if (pdfStore.fileName) p.set('filename', encodeURIComponent(pdfStore.fileName))
  return `/pdfjs/web/viewer.html?${p}`
})
</script>

<template>
  <div class="pdf-page">
    <iframe v-if="iframeSrc" :src="iframeSrc" class="pdf-iframe" allowfullscreen @load="setTimeout(syncPdfViewerTheme, 100)" />
    <div v-else class="pdf-empty">לא נבחר קובץ</div>
  </div>
</template>

<style scoped>
.pdf-page { display: flex; flex-direction: column; height: 100%; }
.pdf-iframe { flex: 1; width: 100%; border: none; }
.pdf-empty { flex: 1; display: flex; align-items: center; justify-content: center; color: var(--text-secondary); font-size: 14px; }
</style>
