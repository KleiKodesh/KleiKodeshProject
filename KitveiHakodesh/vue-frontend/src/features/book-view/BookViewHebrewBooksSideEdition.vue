<script setup lang="ts">
/**
 * A HebrewBooks scanned edition shown side by side with the text, in the book view's left
 * side panel (it takes the left commentary column's place while shown).
 *
 * No tab is involved: the PDF is fetched through the same local folder / cache / download
 * path as the tab flow (fetchHbPdfUrl) and shown in the same PDF view the PDF tab uses
 * (PdfReaderView, with its own toolbar, OCR included), opened at the outline entry for the
 * outline target - the section the reader was in when this edition loaded.
 */
import { ref, shallowRef, computed, watch, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import { fetchHbPdfUrl } from '@/webview-host/bridge'
import type { HebrewBooksEdition } from '@/webview-host/queries.types'
import { useSettingsStore } from '@/stores/settingsStore'
import { useHebrewBooksHistoryStore } from '@/stores/hebrewBooksHistoryStore'
import { editionAsHebrewBook } from '@/features/hebrewbooks/useHebrewBooks'
import PdfReaderView from '@/features/pdf-reader/PdfReaderView.vue'

const props = defineProps<{
  edition: HebrewBooksEdition
  /** Read when the edition loads: the book view breadcrumb the viewer jumps to. */
  getOutlineTarget: () => string | undefined
}>()

const { t } = useI18n()
const settingsStore = useSettingsStore()
const historyStore = useHebrewBooksHistoryStore()

// ── Fetch the PDF ─────────────────────────────────────────────────────────────

const pdfUrl = shallowRef<string | null>(null)
/** The section to open at, read when the edition loads. */
const outlineTarget = shallowRef<string | null>(null)
const errorMessage = ref<string | null>(null)
const isLoading = computed(() => !pdfUrl.value && !errorMessage.value)

// Bumped per load so a slow fetch for an edition the user already switched away from is dropped.
let loadGeneration = 0

async function loadEdition(edition: HebrewBooksEdition) {
  const generation = ++loadGeneration
  pdfUrl.value = null
  errorMessage.value = null
  const target = props.getOutlineTarget() ?? null
  historyStore.trackAccess(editionAsHebrewBook(edition)).catch(() => {})
  const result = await fetchHbPdfUrl(
    String(edition.id),
    settingsStore.hebrewBooksLocalFolder || undefined,
    navigator.onLine,
  )
  if (generation !== loadGeneration) return
  if (result.url) {
    outlineTarget.value = target
    pdfUrl.value = result.url
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

watch(() => props.edition.id, () => loadEdition(props.edition), { immediate: true })

// A fetch still running when the panel closes is dropped.
onBeforeUnmount(() => {
  loadGeneration++
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
      <!-- Keyed by the file: another edition is a fresh view (its own document, OCR and position). -->
      <PdfReaderView :key="pdfUrl!" :url="pdfUrl!" :file-name="edition.title" :outline-target="outlineTarget" />
    </div>
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
</style>
