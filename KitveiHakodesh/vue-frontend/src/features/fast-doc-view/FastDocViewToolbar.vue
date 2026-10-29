<script setup lang="ts">
import {
  IconZoomIn20Regular,
  IconZoomOut20Regular,
  IconLayoutRowTwo20Regular,
  IconLayoutRowTwoFocusBottom20Filled,
  IconSearch20Regular,
  IconDocumentPdf20Regular,
  IconOpen20Regular,
} from '@iconify-prerendered/vue-fluent'
import IconTreeRtl from '@/components/IconTreeRtl.vue'
import ConversionActionButton, { type ConversionActionState } from '@/components/ConversionActionButton.vue'

defineProps<{
  tocVisible: boolean
  notesVisible: boolean
  hasHeadings: boolean
  hasNotes: boolean
  zoom: number
  pdfState: ConversionActionState
  searchVisible: boolean
  canOpenAsPdf: boolean
}>()

const emit = defineEmits<{
  toggleToc: [toggleButton: HTMLElement]
  toggleNotes: []
  zoomIn: []
  zoomOut: []
  openAsPdf: []
  cancelOpenAsPdf: []
  openConvertedPdf: []
  openInDefaultApp: []
  toggleSearch: []
}>()

function onToggleToc(event: MouseEvent) {
  emit('toggleToc', event.currentTarget as HTMLElement)
}
</script>

<template>
  <!-- Same look as the book-view toolbar (top position): 28px icon buttons on the toolbar surface. -->
  <div class="fast-doc-view-toolbar" dir="rtl">
    <button
      :class="{ active: tocVisible }"
      :disabled="!hasHeadings"
      :title="$t('fastDocView.toc')"
      @click="onToggleToc"
    >
      <IconTreeRtl />
    </button>
    <button
      :class="{ active: notesVisible }"
      :disabled="!hasNotes"
      :title="$t('fastDocView.notes')"
      @click="emit('toggleNotes')"
    >
      <!-- the book view's bottom-commentary toggle: the notes pane sits in the same place -->
      <IconLayoutRowTwoFocusBottom20Filled v-if="notesVisible" />
      <IconLayoutRowTwo20Regular v-else />
    </button>
    <button :class="{ active: searchVisible }" :title="$t('fastDocView.search')" @click="emit('toggleSearch')">
      <IconSearch20Regular />
    </button>
    <button :title="$t('fastDocView.zoomOut')" @click="emit('zoomOut')">
      <IconZoomOut20Regular />
    </button>
    <button :title="$t('fastDocView.zoomIn')" @click="emit('zoomIn')">
      <IconZoomIn20Regular />
    </button>
    <!-- The same file through the Word -> PDF conversion, on request only: while it runs the
         button is an animated X that cancels it; when done a check that opens the PDF. -->
    <ConversionActionButton
      v-if="canOpenAsPdf"
      class="fast-doc-view-open-as-pdf"
      :state="pdfState"
      :idle-title="$t('fastDocView.openAsPdf')"
      :busy-title="$t('fastDocView.cancelOpenAsPdf')"
      :done-title="$t('fastDocView.openConvertedPdf')"
      @start="emit('openAsPdf')"
      @cancel="emit('cancelOpenAsPdf')"
      @open="emit('openConvertedPdf')"
    >
      <IconDocumentPdf20Regular />
    </ConversionActionButton>
    <!-- The file in whatever program the system opens it with (shell open, in the user's own process). -->
    <button
      class="fast-doc-view-open-in-default-app"
      :title="$t('fastDocView.openInDefaultApp')"
      @click="emit('openInDefaultApp')"
    >
      <IconOpen20Regular />
    </button>
  </div>
</template>

<style scoped>
.fast-doc-view-toolbar {
  position: relative;
  display: flex;
  flex-direction: row;
  align-items: center;
  justify-content: center;
  height: var(--toolbar-horizontal-height);
  background: var(--bg-toolbar);
  flex-shrink: 0;
}
.fast-doc-view-toolbar::after {
  content: '';
  position: absolute;
  inset: auto 0 0 0;
  border: 0 solid var(--border-color);
  border-top-width: 1px;
  pointer-events: none;
}
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
button svg {
  width: 16px;
  height: 16px;
}
button.active {
  color: var(--accent-color);
}
button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
</style>
