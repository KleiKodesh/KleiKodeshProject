<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount } from 'vue'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useTabStore } from '@/stores/tabStore'
import { syncPdfViewerTheme } from '@/theme/themes'
import { IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import PdfOcrResultPopup from './PdfOcrResultPopup.vue'
import { usePdfOcrSelection } from './usePdfOcrSelection'

import { usePdfOcrStore } from '@/stores/pdfOcrStore'

const localFileStore = useLocalFileStore()
const tabStore = useTabStore()
const pdfOcrStore = usePdfOcrStore()

const iframeRef = ref<HTMLIFrameElement | null>(null)
const ocr = usePdfOcrSelection(() => iframeRef.value)

// Aggressively tear down the iframe when this tab unmounts so the PDF.js worker,
// all rendered canvases, and the WebView2 sub-frame are released immediately
// rather than waiting for the browser's garbage collector.
onBeforeUnmount(() => {
  if (iframeRef.value) {
    iframeRef.value.src = 'about:blank'
    iframeRef.value.remove()
    iframeRef.value = null
  }
})

// Sync composable active state with store
watch(pdfOcrStore, () => {
  if (pdfOcrStore.isActive !== ocr.isActive.value) {
    pdfOcrStore.isActive ? ocr.activate() : ocr.deactivate()
  }
  if (pdfOcrStore.script !== ocr.script.value) {
    ocr.setScript(pdfOcrStore.script)
  }
})

// Deactivate store when composable deactivates (e.g. after selection)
watch(ocr.isActive, (active) => {
  if (!active && pdfOcrStore.isActive) pdfOcrStore.deactivate()
})

function onIframeLoad() {
  setTimeout(syncPdfViewerTheme, 100)
}

const iframeSrc = computed(() => {
  const url = localFileStore.virtualUrl
  if (!url) return null
  const p = new URLSearchParams({ file: url, locale: 'he', cMapPacked: 'true' })
  if (localFileStore.fileName) p.set('filename', encodeURIComponent(localFileStore.fileName))
  // No hash fragment — any hash value becomes initialBookmark in PDF.js and
  // takes priority over the stored scroll/zoom position from ViewHistory,
  // breaking session restore. All options (including disableAutoFetch) are
  // set via AppOptions.setAll() in the viewer.mjs patch instead.
  return `/pdfjs/web/viewer.html?${p}`
})

function cancelConversion() {
  localFileStore.cancelConversion(tabStore.activeTabId)
}

</script>

<template>
  <div class="pdf-page">
    <div v-if="localFileStore.converting" class="converting">
      <div class="converting-card">
        <LoadingAnimation />
        <div class="converting-name">{{ localFileStore.fileName }}</div>
        <div class="converting-sub">
          {{
            localFileStore.loadingType === 'downloading'
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
      <div class="iframe-wrap">
        <iframe
          ref="iframeRef"
          :src="iframeSrc"
          class="pdf-iframe"
          allowfullscreen
          @load="onIframeLoad"
        />
        <div v-if="ocr.isActive.value" class="ocr-overlay" />
        <div v-if="ocr.isActive.value" class="ocr-toolbar">
          <div class="toolbar-content">
            <div class="script-buttons">
              <button
                class="script-btn"
                :class="{ active: pdfOcrStore.script === 'hebrew' }"
                @click="pdfOcrStore.setScript('hebrew')"
                title="עברי רגיל"
              >
                עברי
              </button>
              <button
                class="script-btn"
                :class="{ active: pdfOcrStore.script === 'rashi' }"
                @click="pdfOcrStore.setScript('rashi')"
                title="כתב רש״י"
              >
                רש"י
              </button>
              <button
                class="script-btn"
                :class="{ active: pdfOcrStore.script === 'mixed' }"
                @click="pdfOcrStore.setScript('mixed')"
                title="עברי + רש״י"
              >
                מעורב
              </button>
            </div>
<!--
<button
  class="toggle-btn"
  :class="{ active: pdfOcrStore.skipExistingText }"
  @click="pdfOcrStore.toggleSkipExistingText()"
  title="כפה OCR גם אם קיים טקסט"
>
  כפה OCR
</button>
-->
            <button class="close-btn" @click="ocr.deactivate()" title="סגור (Esc)">
              <IconDismiss20Regular />
            </button>
          </div>
        </div>
      </div>
    </template>

    <div v-else class="pdf-empty">לא נבחר קובץ</div>

    <PdfOcrResultPopup
      v-if="ocr.result.value"
      :result="ocr.result.value"
      :script="pdfOcrStore.script"
      :is-processing="ocr.isProcessing.value"
      :processing-progress="ocr.processingProgress.value"
      @dismiss="ocr.dismissResult"
      @update:script="pdfOcrStore.setScript"
    />
  </div>
</template>

<style scoped>
.pdf-page {
  display: flex;
  flex-direction: column;
  height: 100%;
}
.iframe-wrap {
  flex: 1;
  position: relative;
  min-height: 0;
}
.pdf-iframe {
  width: 100%;
  height: 100%;
  border: none;
}
.ocr-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.2);
  pointer-events: none;
  z-index: 8000;
}

.ocr-toolbar {
  position: fixed;
  top: 12px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 10000;
  animation: slideDown 200ms cubic-bezier(0.16, 1, 0.3, 1);
}

@keyframes slideDown {
  from {
    opacity: 0;
    transform: translateX(-50%) translateY(-10px);
  }
  to {
    opacity: 1;
    transform: translateX(-50%) translateY(0);
  }
}

.toolbar-content {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 16px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
}

.script-buttons {
  display: flex;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  overflow: hidden;
  background: var(--bg-primary);
}

.script-btn {
  padding: 4px 12px;
  font-size: 12px;
  font-weight: 500;
  color: var(--text-secondary);
  background: none;
  border: none;
  cursor: pointer;
  transition: all 100ms ease;
}

.script-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 4%, transparent);
}

.script-btn.active {
  background: var(--accent-color);
  color: #fff;
}

.toggle-btn {
  padding: 4px 12px;
  font-size: 12px;
  font-weight: 500;
  color: var(--text-secondary);
  background: none;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  cursor: pointer;
  transition: all 100ms ease;
  white-space: nowrap;
}

.toggle-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 4%, transparent);
}

.toggle-btn.active {
  background: #f0a500;
  color: #fff;
  border-color: #f0a500;
}

.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  padding: 0;
  border-radius: 4px;
  border: none;
  background: none;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 100ms ease;
}

.close-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
  color: var(--text-primary);
}

.close-btn svg {
  width: 14px;
  height: 14px;
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
