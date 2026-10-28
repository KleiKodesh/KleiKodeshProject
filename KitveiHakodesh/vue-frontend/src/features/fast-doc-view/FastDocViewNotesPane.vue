<script setup lang="ts">
import type { FastDocViewNote } from '@/webview-host/fastDocViewApi'

defineProps<{ notes: FastDocViewNote[]; isRightToLeft: boolean }>()

function noteKey(note: FastDocViewNote): string {
  return note.kind + ':' + note.number + ':' + note.label
}
</script>

<template>
  <!-- The footnotes / endnotes of the blocks mounted in the text pane above. -->
  <div class="fast-doc-view-notes-pane" :dir="isRightToLeft ? 'rtl' : 'ltr'">
    <div v-if="notes.length === 0" class="notes-empty">{{ $t('fastDocView.noNotes') }}</div>
    <div
      v-for="note in notes"
      :key="noteKey(note)"
      class="reading-column-line fast-doc-view-note"
      :title="note.kind === 'endnote' ? $t('fastDocView.endnote') : $t('fastDocView.footnote')"
    >
      <span class="note-label">{{ note.label }}</span>
      <span class="note-body" v-html="note.html" />
    </div>
  </div>
</template>

<style scoped>
.fast-doc-view-notes-pane {
  height: 100%;
  overflow-y: auto;
  padding-block: 6px;
  font-size: 0.85em;
}
.notes-empty {
  padding: 8px 12px;
  color: var(--text-secondary);
}
.note-label {
  font-weight: 600;
  margin-inline-end: 6px;
  color: var(--accent-color);
}
.note-body :deep(p) {
  display: inline;
  margin: 0;
}
</style>
