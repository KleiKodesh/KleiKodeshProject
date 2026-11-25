<script setup lang="ts">
/**
 * The page organizer in a window of its own (DetachedWindow, like the widgets): opened and
 * closed by the PDF viewer's toolbar toggle, working on that tab's open document - the
 * window shares this page's script, so it is the very same PDFDocumentProxy.
 */
import { ref, watch } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import DetachedWindow from '@/components/DetachedWindow.vue'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'
import PdfOrganizerView from './PdfOrganizerView.vue'

const props = defineProps<{
  document: PDFDocumentProxy
  fileName: string | null
  /** The file on disk, when the tab has one: Save writes it; without it only Save a copy. */
  filePath: string | null
  /** Bumped by the toolbar toggle to bring an open organizer to the front. */
  focusRequest?: number
}>()
const emit = defineEmits<{ close: []; saved: [bytes: Uint8Array] }>()

const ORGANIZER_SIZE = { width: 980, height: 720 }
const detachedWindow = ref<InstanceType<typeof DetachedWindow> | null>(null)
const view = ref<InstanceType<typeof PdfOrganizerView> | null>(null)
watch(() => props.focusRequest, () => detachedWindow.value?.focus())

/** The toolbar toggle: closes, asking first when there are unsaved changes. */
function requestClose() {
  if (view.value) view.value.requestClose()
  else emit('close')
}
defineExpose({ requestClose })

function onBlocked() {
  emit('close')
  showToast(t('layout.widgets.windowBlocked'), { variant: 'error' })
}
</script>

<template>
  <DetachedWindow
    ref="detachedWindow"
    name="pdf-organizer"
    :title="fileName ? `${$t('pdfOrganizer.title')} - ${fileName}` : $t('pdfOrganizer.title')"
    :width="ORGANIZER_SIZE.width"
    :height="ORGANIZER_SIZE.height"
    :min-width="560"
    @closed="emit('close')"
    @blocked="onBlocked"
  >
    <PdfOrganizerView
      ref="view"
      :document="document"
      :file-name="fileName"
      :file-path="filePath"
      @close="emit('close')"
      @saved="emit('saved', $event)"
    />
  </DetachedWindow>
</template>
