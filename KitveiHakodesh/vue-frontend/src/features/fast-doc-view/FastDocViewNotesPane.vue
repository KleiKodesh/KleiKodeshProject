<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import type { FastDocViewPlacedNote } from './useFastDocViewDocument'

/**
 * The footnotes / endnotes of the blocks mounted in the text pane above. `activeKey` (a note
 * whose reference was clicked in the text) is scrolled into view and highlighted; a note's
 * label jumps the text to the block holding its reference.
 */
const props = defineProps<{ notes: FastDocViewPlacedNote[]; isRightToLeft: boolean; activeKey?: string | null }>()
const emit = defineEmits<{ goToReference: [note: FastDocViewPlacedNote] }>()

const pane = ref<HTMLElement | null>(null)

function noteKey(note: Pick<FastDocViewPlacedNote, 'kind' | 'number' | 'label'>): string {
  return fastDocViewNoteKey(note.kind, note.number, note.label)
}

watch(
  () => [props.activeKey, props.notes.length] as const,
  async ([key]) => {
    if (!key) return
    await nextTick()
    const row = [...(pane.value?.querySelectorAll<HTMLElement>('[data-note-key]') ?? [])].find((element) => element.dataset.noteKey === key)
    row?.scrollIntoView({ block: 'nearest' })
  },
)
</script>

<script lang="ts">
/** One note's identity in the page: kind, auto number, label (a custom mark has number 0). */
export function fastDocViewNoteKey(kind: string, number: number, label: string): string {
  return kind + ':' + number + ':' + label
}
</script>

<template>
  <div ref="pane" class="fast-doc-view-notes-pane" :dir="isRightToLeft ? 'rtl' : 'ltr'">
    <div v-if="notes.length === 0" class="notes-empty">{{ $t('fastDocView.noNotes') }}</div>
    <div
      v-for="note in notes"
      :key="noteKey(note)"
      class="reading-column-line fast-doc-view-note"
      :class="{ 'fast-doc-view-note--active': noteKey(note) === activeKey }"
      :data-note-key="noteKey(note)"
    >
      <button
        class="note-label"
        :title="note.kind === 'endnote' ? $t('fastDocView.goToEndnoteReference') : $t('fastDocView.goToFootnoteReference')"
        @click="emit('goToReference', note)"
      >{{ note.label }}</button>
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
  font: inherit;
  font-weight: 600;
  margin-inline-end: 6px;
  padding: 0 2px;
  color: var(--accent-color);
  background: none;
  border: none;
  cursor: pointer;
}
.note-label:hover {
  text-decoration: underline;
}
.fast-doc-view-note--active {
  background: var(--highlight-bg, color-mix(in srgb, var(--accent-color) 14%, transparent));
}
.note-body :deep(p) {
  display: inline;
  margin: 0;
}
</style>
