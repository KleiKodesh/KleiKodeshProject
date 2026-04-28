<script setup lang="ts">
import { ref, computed } from 'vue'
import { useEventListener } from '@vueuse/core'
import { usePdfStore } from '@/stores/pdfStore'
import { useTabStore } from '@/stores/tabStore'
import { syncPdfViewerTheme } from '@/theme/themes'
import { IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import PdfToolbar from './PdfToolbar.vue'

const pdfStore = usePdfStore()
const tabStore = useTabStore()

const iframeRef = ref<HTMLIFrameElement | null>(null)
const toolbarRef = ref<InstanceType<typeof PdfToolbar> | null>(null)

function openFindBar() {
  toolbarRef.value?.openFind()
}

// Ctrl+F when focus is in the Vue app (outside the iframe)
useEventListener('keydown', (event: KeyboardEvent) => {
  if (event.ctrlKey && event.key === 'f' && iframeRef.value) {
    event.preventDefault()
    openFindBar()
  }
})

function onIframeLoad() {
  setTimeout(syncPdfViewerTheme, 100)
  // Inject Ctrl+F listener into the iframe so it works when focus is inside the PDF
  try {
    iframeRef.value?.contentWindow?.addEventListener('keydown', (event: KeyboardEvent) => {
      if (event.ctrlKey && event.key === 'f') {
        event.preventDefault()
        openFindBar()
      }
    })
  } catch {
    // cross-origin guard — won't happen since pdfjs is same-origin
  }
}

const iframeSrc = computed(() => {
  const url = pdfStore.virtualUrl
  if (!url) return null
  const p = new URLSearchParams({ file: url, locale: 'he', enableHWA: 'true', cMapPacked: 'true' })
  if (pdfStore.fileName) p.set('filename', encodeURIComponent(pdfStore.fileName))
  return `/pdfjs/web/viewer.html?${p}`
})

function cancelConversion() {
  pdfStore.cancelConversion(tabStore.activeTabId)
}
</script>

<template>
  <div class="pdf-page">
    <div v-if="pdfStore.converting" class="converting">
      <div class="converting-card">
        <LoadingAnimation />
        <div class="converting-name">{{ pdfStore.fileName }}</div>
        <div class="converting-sub">
          {{
            pdfStore.loadingType === 'downloading'
              ? 'מוריד את הספר — אנא המתן'
              : 'ממיר לקובץ PDF — התהליך עשוי לארוך זמן מה'
          }}
        </div>
        <button class="cancel-btn" @click="cancelConversion">
          <IconDismiss20Regular />
          <span>ביטול</span>
        </button>
      </div>
    </div>

    <template v-else-if="iframeSrc">
      <PdfToolbar ref="toolbarRef" :iframe-el="iframeRef" />
      <iframe
        ref="iframeRef"
        :src="iframeSrc"
        class="pdf-iframe"
        allowfullscreen
        @load="onIframeLoad"
      />
    </template>

    <div v-else class="pdf-empty">לא נבחר קובץ</div>
  </div>
</template>

<style scoped>
.pdf-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: visible;
}
.pdf-iframe {
  flex: 1;
  width: 100%;
  border: none;
  min-height: 0;
  overflow: hidden;
}
.pdf-empty {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-secondary);
  font-size: 14px;
}

.converting {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--bg-primary);
}

.converting-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 40px 48px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  min-width: 260px;
  text-align: center;
}

.converting-name {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
  max-width: 240px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.converting-sub {
  font-size: 12px;
  color: var(--text-secondary);
}

.cancel-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 4px;
  padding: 6px 16px;
  font-size: 13px;
  border-radius: 4px;
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  background: var(--bg-primary);
}
.cancel-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
</style>
