<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount } from 'vue'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useTabStore } from '@/stores/tabStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { syncPdfViewerTheme } from '@/theme/themes'
import { IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import ContextMenu from '@/components/ContextMenu.vue'
import PdfOcrResultPopup from './PdfOcrResultPopup.vue'
import { usePdfOcrSelection } from './usePdfOcrSelection'
import { usePdfContextMenu } from './usePdfContextMenu'

import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import { usePdfViewPageTracking } from './usePdfViewPageTracking'

const localFileStore = useLocalFileStore()
const tabStore = useTabStore()
const paneNavigation = usePaneNavigation()
const pdfOcrStore = usePdfOcrStore()
const pageTracking = usePdfViewPageTracking()

const iframeRef = ref<HTMLIFrameElement | null>(null)
const ocr = usePdfOcrSelection(() => iframeRef.value)

// ── Custom right-click menu (copy / copy into Word / copy page as image) ──────
const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const toast = ref('')
let toastTimer: ReturnType<typeof setTimeout> | null = null
function showToast(message: string) {
  toast.value = message
  if (toastTimer) clearTimeout(toastTimer)
  toastTimer = setTimeout(() => (toast.value = ''), 2400)
}
const pdfContextMenu = usePdfContextMenu(() => iframeRef.value, contextMenuRef, {
  // OCR mode has its own selection overlay — don't shadow it with our menu.
  isBlocked: () => pdfOcrStore.isActive,
  notify: showToast,
})

import { TAB_SWIPE_EVENT, createWheelSwipeHandler, type TabSwipeGestureEventDetail } from '@/composables/useTabSwipeNavigation'
import { useSwipe } from '@vueuse/core'
import { shallowRef } from 'vue'

// ── Touch swipe relay ────────────────────────────────────────────────────────
// The PDF.js iframe captures pointer focus, so touch events fire on the
// iframe's contentWindow and never bubble to the parent document. We set
// iframeContentWindow to the iframe's contentWindow after load so that
// useSwipe (VueUse) attaches its listeners there directly — the proper API,
// not a manual touchstart/touchend hack.

const RELAY_TOUCH_THRESHOLD_PX = 60

const iframeContentWindow = shallowRef<Window | null>(null)

function fireSwipe(direction: 'next' | 'previous') {
  window.dispatchEvent(
    new CustomEvent<TabSwipeGestureEventDetail>(TAB_SWIPE_EVENT, {
      detail: { direction },
    }),
  )
}

useSwipe(iframeContentWindow, {
  threshold: RELAY_TOUCH_THRESHOLD_PX,
  onSwipeEnd(_event, direction) {
    if (direction === 'right') fireSwipe('next')
    else if (direction === 'left') fireSwipe('previous')
  },
})

// Trackpad horizontal scroll — wheel events also stay inside the iframe.
// useSwipe only handles touch, so we still need a wheel relay. The shared handler
// carries the RTL direction convention and one-switch-per-gesture debounce.
let iframeWheelCleanup: (() => void) | null = null

function attachIframeWheelRelay() {
  detachIframeWheelRelay()
  const contentWindow = iframeRef.value?.contentWindow
  if (!contentWindow) return

  const onWheel = createWheelSwipeHandler(fireSwipe)
  contentWindow.addEventListener('wheel', onWheel, { passive: true })
  iframeWheelCleanup = () => contentWindow.removeEventListener('wheel', onWheel)
}

function detachIframeWheelRelay() {
  iframeWheelCleanup?.()
  iframeWheelCleanup = null
}

// Aggressively tear down the iframe when this tab unmounts so the PDF.js worker,
// all rendered canvases, and the WebView2 sub-frame are released immediately
// rather than waiting for the browser's garbage collector.
onBeforeUnmount(() => {
  pdfContextMenu.detach(iframeRef.value?.contentWindow ?? null)
  if (toastTimer) clearTimeout(toastTimer)
  iframeContentWindow.value = null
  detachIframeWheelRelay()
  pageTracking.detach()
  // Clear the TOC path so it doesn't bleed into other tabs or after navigation.
  paneNavigation.updateActiveTab({ tocPath: undefined })
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
  if (pdfOcrStore.skipExistingText !== ocr.forceOcr.value) {
    ocr.setForceOcr(pdfOcrStore.skipExistingText)
  }
})

// Deactivate store when composable deactivates (e.g. after selection)
watch(ocr.isActive, (active) => {
  if (!active && pdfOcrStore.isActive) pdfOcrStore.deactivate()
})

// Update PDF.js toolbar visibility when the setting changes
watch(
  () => paneNavigation.activeTab?.pdfViewerTitleBarVisible,
  (visible) => {
    setPdfToolbarVisible(visible !== false)
  },
)

function setPdfToolbarVisible(visible: boolean) {
  if (!iframeRef.value?.contentWindow) return
  const doc = iframeRef.value.contentWindow.document
  const toolbarEl = doc.querySelector('.toolbar') as HTMLElement | null
  const viewerContainerEl = doc.getElementById('viewerContainer') as HTMLElement | null

  if (toolbarEl) {
    toolbarEl.style.display = visible ? '' : 'none'
  }

  // #viewerContainer has inset: var(--toolbar-height) 0 0 in PDF.js CSS.
  // When the toolbar is hidden that gap must collapse to 0; restore to '' to
  // let PDF.js's own CSS take over when the toolbar is visible again.
  if (viewerContainerEl) {
    viewerContainerEl.style.insetBlockStart = visible ? '' : '0'
  }
}

function onIframeLoad() {
  const contentWindow = iframeRef.value?.contentWindow ?? null
  iframeContentWindow.value = contentWindow
  attachIframeWheelRelay()
  if (contentWindow) {
    pageTracking.attach(contentWindow)
    pdfContextMenu.attach(contentWindow)
  }
  setTimeout(() => {
    syncPdfViewerTheme()
    // Apply toolbar visibility based on current setting
    setPdfToolbarVisible(paneNavigation.activeTab?.pdfViewerTitleBarVisible !== false)
  }, 100)
}

const iframeSrc = computed(() => {
  const url = paneNavigation.activeTab.localFileVirtualUrl
  if (!url) return null
  const p = new URLSearchParams({ file: url, locale: 'he', cMapPacked: 'true' })
  const fileName = paneNavigation.activeTab.localFileName
  if (fileName) p.set('filename', encodeURIComponent(fileName))
  return `/pdfjs/web/viewer.html?${p}`
})

// Computed pane-specific converting state (localFileStore.converting reads pane 1 only)
const converting = computed(() => paneNavigation.activeTab.localFileConverting ?? false)
const loadingType = computed(() => paneNavigation.activeTab.localFileLoadingType ?? 'converting')
const fileName = computed(() => paneNavigation.activeTab.localFileName ?? null)

function cancelConversion() {
  localFileStore.cancelConversion(paneNavigation.activeTabId)
}

</script>

<template>
  <div class="pdf-page">
    <div v-if="converting" class="converting">
      <div class="converting-card">
        <LoadingAnimation />
        <div class="converting-name">{{ fileName }}</div>
        <div class="converting-sub">
          {{
            loadingType === 'downloading'
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
            <button
              class="toggle-btn"
              :class="{ active: pdfOcrStore.skipExistingText }"
              @click="pdfOcrStore.toggleSkipExistingText()"
              title="כפה — דלג על שכבת הטקסט וזהה ישירות מהתמונה"
            >
              כפה
            </button>
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

    <ContextMenu ref="contextMenuRef" :items="pdfContextMenu.items.value" />

    <Transition name="pdf-toast-fade">
      <div v-if="toast" class="pdf-toast">{{ toast }}</div>
    </Transition>
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
  background: var(--status-warning);
  color: #fff;
  border-color: var(--status-warning);
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
.pdf-toast {
  position: absolute;
  bottom: 24px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 10001;
  padding: 8px 16px;
  border-radius: 6px;
  background: var(--bg-secondary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
  font-size: 13px;
  max-width: 80%;
  text-align: center;
  pointer-events: none;
}
.pdf-toast-fade-enter-active,
.pdf-toast-fade-leave-active {
  transition: opacity 150ms ease;
}
.pdf-toast-fade-enter-from,
.pdf-toast-fade-leave-to {
  opacity: 0;
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
