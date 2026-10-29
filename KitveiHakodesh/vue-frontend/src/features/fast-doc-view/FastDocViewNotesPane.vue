<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import { IconMinimize20Regular } from '@iconify-prerendered/vue-fluent'
import type { FastDocViewPlacedNote } from './useFastDocViewDocument'

/**
 * The footnotes / endnotes of the blocks mounted in the text pane above - laid out like the
 * book view's bottom commentary panel: a 32 px header row (title, close button on the far side),
 * entries drawn with the commentary's reading-column variables (font, size, weight, leading,
 * side padding), the commentary's empty-state hint. `activeKey` (a note whose reference was
 * clicked) is scrolled into view and highlighted; a note's label jumps the text to its reference.
 */
const props = defineProps<{ notes: FastDocViewPlacedNote[]; isRightToLeft: boolean; activeKey?: string | null; fontPx: number }>()
const emit = defineEmits<{ goToReference: [note: FastDocViewPlacedNote]; close: [] }>()

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

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
  }
}
</script>

<script lang="ts">
/** One note's identity in the page: kind, auto number, label (a custom mark has number 0). */
export function fastDocViewNoteKey(kind: string, number: number, label: string): string {
  return kind + ':' + number + ':' + label
}
</script>

<template>
  <div class="fast-doc-view-notes-pane" :dir="isRightToLeft ? 'rtl' : 'ltr'" tabindex="-1" @keydown="onKeydown">
    <div class="notes-header">
      <span class="notes-title">{{ $t('fastDocView.notes') }}</span>
      <button class="btn c-pointer hover-bg notes-close" :title="$t('fastDocView.closeNotes')" @click.stop="emit('close')">
        <IconMinimize20Regular />
      </button>
    </div>
    <div v-if="notes.length === 0" class="state-overlay">
      <div class="hint-title">{{ $t('fastDocView.noNotes') }}</div>
    </div>
    <div v-else ref="pane" class="notes-scroller" :style="{ fontSize: `${fontPx}px` }">
      <div
        v-for="note in notes"
        :key="noteKey(note)"
        class="line fast-doc-view-note"
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
  </div>
</template>

<style scoped>
/* The book view's commentary panel (CommentaryView / CommentaryHeaderNav), item for item. */
.fast-doc-view-notes-pane {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  outline: none;
  container: fast-doc-view-notes / inline-size;
}
.notes-header {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
  height: 32px;
  font-size: 13px;
  overflow: hidden;
  background: var(--bg-toolbar);
  border-bottom: 1px solid var(--border-color);
  padding-inline: 6px;
}
.notes-title {
  font-weight: 600;
  color: var(--text-secondary);
}
.notes-close {
  margin-inline-start: auto;
}
.btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  flex-shrink: 0;
  border-radius: 4px;
  color: var(--text-primary);
}
.btn :deep(svg) {
  width: 14px;
  height: 14px;
}
.state-overlay {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}
.hint-title {
  font-size: 13px;
  color: var(--text-secondary);
  font-weight: 500;
  text-align: center;
}
.notes-scroller {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  --commentary-pad-inline: 12px;
}
/* As the commentary panel: more side room on a wide panel (its own width, not the window's). */
@container fast-doc-view-notes (min-width: 600px) {
  .notes-scroller {
    --commentary-pad-inline: 22px;
  }
}
.line {
  padding-inline: var(--commentary-pad-inline, 12px);
  padding-block: 2px;
  max-width: var(--commentary-max-width, none);
  margin-inline: auto;
  font-family: var(--commentary-text-font);
  font-size: var(--commentary-font-size, 100%);
  font-weight: var(--commentary-font-weight, 400);
  line-height: var(--commentary-line-height, 1.7);
  color: var(--text-primary);
  text-align: justify;
}
.note-label {
  font-family: var(--commentary-header-font);
  font-weight: 800;
  margin-inline-end: 6px;
  padding: 0 2px;
  color: var(--accent-color);
  background: none;
  border: none;
  cursor: pointer;
  font-size: inherit;
  line-height: inherit;
}
.note-label:hover {
  text-decoration: underline;
}
.fast-doc-view-note--active {
  background: color-mix(in srgb, var(--accent-color) 14%, transparent);
}
.note-body :deep(p) {
  display: inline;
  margin: 0;
}
</style>
