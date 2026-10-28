<script setup lang="ts">
import {
  IconZoomIn20Regular,
  IconZoomOut20Regular,
  IconNotepad20Regular,
} from '@iconify-prerendered/vue-fluent'
import IconTreeRtl from '@/components/IconTreeRtl.vue'

defineProps<{
  tocVisible: boolean
  notesVisible: boolean
  hasHeadings: boolean
  hasNotes: boolean
  zoom: number
}>()

const emit = defineEmits<{
  toggleToc: [toggleButton: HTMLElement]
  toggleNotes: []
  zoomIn: []
  zoomOut: []
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
      <IconNotepad20Regular />
    </button>
    <button :title="$t('fastDocView.zoomOut')" @click="emit('zoomOut')">
      <IconZoomOut20Regular />
    </button>
    <button :title="$t('fastDocView.zoomIn')" @click="emit('zoomIn')">
      <IconZoomIn20Regular />
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
