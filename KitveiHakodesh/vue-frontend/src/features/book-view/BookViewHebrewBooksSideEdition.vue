<script setup lang="ts">
/**
 * A HebrewBooks scanned edition shown side by side with the text, in the book view's left
 * side panel (it takes the left commentary column's place while shown).
 *
 * No tab is involved: the PDF is fetched through the same local folder / cache / download
 * path as the tab flow (fetchHbPdfUrl) and loaded in the same PDF.js viewer iframe the PDF
 * tab builds (buildPdfViewerSrc), opened at the outline entry for `outlineTarget` - the
 * section the reader was in when this edition loaded. OCR works here as in the PDF tab: the
 * title-bar OCR button drives this iframe through pdfOcrStore.
 */
import { ref, shallowRef, computed, watch, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import { fetchHbPdfUrl } from '@/webview-host/bridge'
import type { HebrewBooksEdition } from '@/webview-host/queries.types'
import { useSettingsStore } from '@/stores/settingsStore'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import { syncPdfViewerTheme } from '@/theme/themes'
import { editionAsHebrewBook } from '@/features/hebrewbooks/useHebrewBooks'
import { buildPdfViewerSrc, outlineJumpHash } from '@/features/pdf-viewer/buildPdfViewerSrc'
import { usePdfOcrSelection } from '@/features/pdf-viewer/usePdfOcrSelection'
import { usePdfOcrStoreSync } from '@/features/pdf-viewer/usePdfOcrStoreSync'
import PdfOcrToolbar from '@/features/pdf-viewer/PdfOcrToolbar.vue'
import PdfOcrResultPopup from '@/features/pdf-viewer/PdfOcrResultPopup.vue'

const props = defineProps<{
  edition: HebrewBooksEdition
  /** Read when the edition loads: the book view breadcrumb the viewer jumps to. */
  getOutlineTarget: () => string | undefined
}>()

const { t } = useI18n()
const settingsStore = useSettingsStore()
const historyStore = useHebrewBooksHistoryStore()
const pdfOcrStore = usePdfOcrStore()

const iframeRef = ref<HTMLIFrameElement | null>(null)
const ocr = usePdfOcrSelection(() => iframeRef.value)
usePdfOcrStoreSync(ocr)

// ── Fetch the PDF ─────────────────────────────────────────────────────────────

const iframeSrc = shallowRef<string | null>(null)
const errorMessage = ref<string | null>(null)
const isLoading = computed(() => !iframeSrc.value && !errorMessage.value)

// Bumped per load so a slow fetch for an edition the user already switched away from is dropped.
let loadGeneration = 0

async function loadEdition(edition: HebrewBooksEdition) {
  const generation = ++loadGeneration
  iframeSrc.value = null
  errorMessage.value = null
  const outlineTarget = props.getOutlineTarget()
  historyStore.trackAccess(editionAsHebrewBook(edition)).catch(() => {})
  const result = await fetchHbPdfUrl(
    String(edition.id),
    settingsStore.hebrewBooksLocalFolder || undefined,
    navigator.onLine,
  )
  if (generation !== loadGeneration) return
  if (result.url) {
    iframeSrc.value = buildPdfViewerSrc(result.url, edition.title, outlineTarget ? outlineJumpHash(outlineTarget) : null)
  } else if (result.cancelled) {
    errorMessage.value = t('bookView.hebrewBooks.sideLoadFailed')
  } else if (result.noInternet) {
    errorMessage.value = t('hebrewBooks.error.noInternet')
  } else if (result.notFound) {
    errorMessage.value = t('hebrewBooks.error.notOnServer')
  } else {
    errorMessage.value = result.error || t('bookView.hebrewBooks.sideLoadFailed')
  }
}

watch(() => props.edition.id, () => {
  if (ocr.isActive.value) ocr.deactivate()
  loadEdition(props.edition)
}, { immediate: true })

function onIframeLoad() {
  setTimeout(syncPdfViewerTheme, 100)
}

// Release the PDF.js worker and canvases now, not at the next GC - and an OCR mode left on
// would otherwise keep the title-bar button lit with nothing to drive.
onBeforeUnmount(() => {
  loadGeneration++
  if (pdfOcrStore.isActive) pdfOcrStore.deactivate()
  if (iframeRef.value) {
    iframeRef.value.src = 'about:blank'
    iframeRef.value.remove()
    iframeRef.value = null
  }
})
</script>

<template>
  <div class="side-edition">
    <div v-if="isLoading" class="side-edition-status">
      <LoadingAnimation />
      <span>{{ $t('bookView.hebrewBooks.sideLoading') }}</span>
    </div>
    <div v-else-if="errorMessage" class="side-edition-status">{{ errorMessage }}</div>
    <div v-else class="side-edition-frame-wrap">
      <iframe
        ref="iframeRef"
        :src="iframeSrc!"
        class="side-edition-frame"
        allowfullscreen
        @load="onIframeLoad"
      />
      <PdfOcrToolbar :ocr="ocr" contained />
    </div>

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
.side-edition {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-width: 0;
  background: var(--bg-primary);
}
.side-edition-status {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 16px;
  font-size: 13px;
  text-align: center;
  color: var(--text-secondary);
}
.side-edition-frame-wrap {
  flex: 1;
  position: relative;
  min-height: 0;
}
.side-edition-frame {
  width: 100%;
  height: 100%;
  border: none;
}
</style>
