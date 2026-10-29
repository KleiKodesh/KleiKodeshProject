<script setup lang="ts">
import {
  IconZoomIn20Regular,
  IconChevronLeft20Regular,
  IconChevronRight20Regular,
  IconZoomOut20Regular,
  IconLayoutRowTwo20Regular,
  IconLayoutRowTwoFocusBottom20Filled,
  IconSearch20Regular,
  IconDocumentPdf20Regular,
  IconTimeline20Filled,
  IconLayoutColumnTwoFocusRight20Filled,
  IconLayoutColumnTwoFocusLeft20Filled,
  IconTimeline20Regular,
} from '@iconify-prerendered/vue-fluent'
import IconTreeRtl from '@/components/IconTreeRtl.vue'
import WordDocumentGlyph from '@/components/WordDocumentGlyph.vue'
import { usePressRepeat } from '@/composables/usePressRepeat'
import { ZOOM_CONFIG } from '@/composables/useZoom'
import ConversionActionButton, { type ConversionActionState } from '@/components/ConversionActionButton.vue'
import { ref } from 'vue'
import { storeToRefs } from 'pinia'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useToolbarDrag } from '@/composables/useToolbarDrag'

defineProps<{
  tocVisible: boolean
  notesVisible: boolean
  notesSync: boolean
  /** Where the notes pane sits; the side places only on a wide page (as the book view's side commentary). */
  notesPlace: 'bottom' | 'right' | 'left'
  canPlaceNotesBeside: boolean
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
  toggleNotesSync: []
  cycleNotesPlace: []
  zoomIn: []
  zoomOut: []
  openAsPdf: []
  cancelOpenAsPdf: []
  openConvertedPdf: []
  openInDefaultApp: []
  toggleSearch: []
  navigateToNextSection: []
  navigateToPreviousSection: []
}>()

const zoomRepeat = usePressRepeat()

// Dragged by its background to any edge of the page, as the book view's toolbar (the shared
// useToolbarDrag and the same app-wide toolbar position setting).
const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)
const toolbarEl = ref<HTMLElement | null>(null)
const { isDragging, dropPreviewStyle } = useToolbarDrag(toolbarEl, toolbarPosition, '.fast-doc-view')

function onToggleToc(event: MouseEvent) {
  emit('toggleToc', event.currentTarget as HTMLElement)
}
</script>

<template>
  <!-- The book view toolbar's shell (.reading-toolbar, assets/styles/readingToolbar.css): any edge, dragged by its background. -->
  <div
    ref="toolbarEl"
    class="fast-doc-view-toolbar reading-toolbar"
    dir="rtl"
    :class="[`toolbar-${toolbarPosition}`, { dragging: isDragging }]"
    :title="$t('bookView.toolbar.dragHint')"
  >
    <Teleport to="body">
      <div v-if="isDragging" class="toolbar-drop-preview" :style="dropPreviewStyle" />
    </Teleport>
    <button
      :class="{ active: tocVisible }"
      :disabled="!hasHeadings"
      :title="$t('fastDocView.toc')"
      @click="onToggleToc"
    >
      <IconTreeRtl />
    </button>
    <!-- The book view's previous / next section buttons: here the previous / next heading. -->
    <button
      :disabled="!hasHeadings"
      :title="$t('bookView.toolbar.prevSection')"
      @click="emit('navigateToPreviousSection')"
    >
      <IconChevronRight20Regular class="dir-icon" />
    </button>
    <button
      :disabled="!hasHeadings"
      :title="$t('bookView.toolbar.nextSection')"
      @click="emit('navigateToNextSection')"
    >
      <IconChevronLeft20Regular class="dir-icon" />
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
    <!-- The notes pane's place, cycled bottom -> right -> left: the icon is the current place (the
         book view's bottom / side / side-left commentary icons), the tooltip names the next. -->
    <button
      v-if="hasNotes && canPlaceNotesBeside"
      :title="$t(notesPlace === 'bottom' ? 'fastDocView.notesPlaceToRight' : notesPlace === 'right' ? 'fastDocView.notesPlaceToLeft' : 'fastDocView.notesPlaceToBottom')"
      @click="emit('cycleNotesPlace')"
    >
      <IconLayoutColumnTwoFocusRight20Filled v-if="notesPlace === 'right'" />
      <IconLayoutColumnTwoFocusLeft20Filled v-else-if="notesPlace === 'left'" />
      <IconLayoutRowTwoFocusBottom20Filled v-else />
    </button>
    <!-- the book view's sync-commentaries toggle (same icon and tooltips), for the notes pane -->
    <button
      v-if="hasNotes"
      :class="{ active: notesSync }"
      :title="notesSync ? $t('fastDocView.syncNotesOnTitle') : $t('fastDocView.syncNotesOffTitle')"
      @click="emit('toggleNotesSync')"
    >
      <IconTimeline20Filled v-if="notesSync" />
      <IconTimeline20Regular v-else />
    </button>
    <button :class="{ active: searchVisible }" :title="$t('fastDocView.search')" @click="emit('toggleSearch')">
      <IconSearch20Regular />
    </button>
    <!-- hold to repeat, as in the book view (shared usePressRepeat) -->
    <button
      :title="$t('fastDocView.zoomOut', { text: Math.round(zoom) })"
      :disabled="zoom <= ZOOM_CONFIG.MIN"
      @pointerdown="zoomRepeat.start(() => emit('zoomOut'))"
      @pointerup="zoomRepeat.stop"
      @pointerleave="zoomRepeat.stop"
      @pointercancel="zoomRepeat.stop"
    >
      <IconZoomOut20Regular />
    </button>
    <button
      :title="$t('fastDocView.zoomIn', { text: Math.round(zoom) })"
      :disabled="zoom >= ZOOM_CONFIG.MAX"
      @pointerdown="zoomRepeat.start(() => emit('zoomIn'))"
      @pointerup="zoomRepeat.stop"
      @pointerleave="zoomRepeat.stop"
      @pointercancel="zoomRepeat.stop"
    >
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
      <WordDocumentGlyph />
    </button>
  </div>
</template>

<style scoped>
/* The shell, orientations, edge line and drag preview: assets/styles/readingToolbar.css. */
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
