<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import type { FastDocViewPlacedNote } from './useFastDocViewDocument'
import { useSettingsStore } from '@/stores/settingsStore'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'

/**
 * The footnotes / endnotes of the blocks mounted in the text pane above - laid out like the
 * book view's bottom commentary panel, with no header row (just the sash above it; Esc or the
 * toolbar's notes toggle closes it): entries drawn with the commentary's reading-column variables (font, size, weight, leading,
 * side padding), the commentary's empty-state hint. `activeKey` (a note whose reference was
 * clicked) lands at the pane's top with a short ease; a note's label jumps the text to its reference.
 */
const props = defineProps<{
  notes: FastDocViewPlacedNote[]
  isRightToLeft: boolean
  activeKey?: string | null
  /** Bumped on every reference click, so clicking the same reference again lands it again. */
  landingNonce?: number
  /** Sync on: the first block in view; the pane follows the first note at or after it. */
  followIndex?: number | null
  fontPx: number
}>()
const emit = defineEmits<{ goToReference: [note: FastDocViewPlacedNote]; close: [] }>()

const pane = ref<HTMLElement | null>(null)

function noteKey(note: Pick<FastDocViewPlacedNote, 'kind' | 'number' | 'label'>): string {
  return fastDocViewNoteKey(note.kind, note.number, note.label)
}

/** A clicked note lands with a short ease (not the browser's slow smooth scroll). */
const LANDING_MS = 180
let landingFrame = 0
function cancelLanding() {
  if (landingFrame) cancelAnimationFrame(landingFrame)
  landingFrame = 0
}

/**
 * Puts a note row at the top of the pane's own scroller (never scrolls ancestors).
 * `animate`: the short ease for a clicked note; sync-follow stays instant. Any new landing
 * or the reader's own wheel / press in the pane cancels a running ease.
 */
function landRow(predicate: (element: HTMLElement) => boolean, animate: boolean) {
  cancelLanding()
  const scroller = pane.value
  if (!scroller) return
  const row = [...scroller.querySelectorAll<HTMLElement>('[data-note-key]')].find(predicate)
  if (!row) return
  const target = Math.min(row.offsetTop, scroller.scrollHeight - scroller.clientHeight)
  const from = scroller.scrollTop
  if (!animate || Math.abs(target - from) < 2) {
    scroller.scrollTop = target
    return
  }
  const start = performance.now()
  const step = (now: number) => {
    const t = Math.min(1, (now - start) / LANDING_MS)
    scroller.scrollTop = from + (target - from) * (1 - (1 - t) ** 3)   // ease-out cubic
    landingFrame = t < 1 ? requestAnimationFrame(step) : 0
  }
  landingFrame = requestAnimationFrame(step)
}
onBeforeUnmount(cancelLanding)

// Keep notes in sync with the text: the first note at or after the first block in view.
watch(
  () => props.followIndex,
  (index) => {
    // A click landing in progress wins (watchers run in definition order: this one first,
    // so the click landing below, on the same mount, is never cancelled by it).
    if (index == null || landingFrame) return
    landRow((element) => Number(element.dataset.blockIndex) >= index, false)
  },
  { immediate: true, flush: 'post' },
)

// The clicked reference's note lands in view. `immediate` + post flush: a click on a
// reference also OPENS the pane (SplitPane mounts its bottom slot then), so the key is set
// before this component exists - a non-immediate watcher never saw it (the old bug).
watch(
  () => [props.activeKey, props.landingNonce] as const,
  ([key]) => {
    if (key) landRow((element) => element.dataset.noteKey === key, true)
  },
  { immediate: true, flush: 'post' },
)

/** The library marks a note's own number (Word's footnoteRef / ODF citation / RTF chftn / .doc
 *  auto mark) as <span class="note-mark">; that number is the note's one back-link to its reference. */
const NOTE_MARK = 'class="note-mark"'
function hasOwnMark(note: FastDocViewPlacedNote): boolean {
const settingsStore = useSettingsStore()
  return note.html.includes(NOTE_MARK)
}
function bodyHtml(note: FastDocViewPlacedNote, title: string): string {
  // the book view's clean-text (Ctrl+D), as on its commentary
  return cleanHebrewText(note.html, settingsStore.cleanTextFlags).replace(NOTE_MARK, `${NOTE_MARK} role="button" tabindex="0" title="${title}"`)
}
function onBodyActivate(event: Event, note: FastDocViewPlacedNote) {
  if (!(event.target as HTMLElement | null)?.closest?.('.note-mark')) return
  event.preventDefault()
  emit('goToReference', note)
}

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
    <div v-if="notes.length === 0" class="state-overlay">
      <div class="hint-title">{{ $t('fastDocView.noNotes') }}</div>
    </div>
    <div v-else ref="pane" class="notes-scroller" @wheel.passive="cancelLanding" @pointerdown="cancelLanding" :style="{ fontSize: `${fontPx}px` }">
      <div
        v-for="note in notes"
        :key="noteKey(note)"
        class="line fast-doc-view-note"
        :data-note-key="noteKey(note)"
        :data-block-index="note.blockIndex"
      >
        <button
          v-if="!hasOwnMark(note)"
          class="note-label"
          :title="note.kind === 'endnote' ? $t('fastDocView.goToEndnoteReference') : $t('fastDocView.goToFootnoteReference')"
          @click="emit('goToReference', note)"
        >{{ note.label }}</button>
        <span
          class="note-body"
          @click="onBodyActivate($event, note)"
          @keydown.enter="onBodyActivate($event, note)"
          v-html="bodyHtml(note, note.kind === 'endnote' ? $t('fastDocView.goToEndnoteReference') : $t('fastDocView.goToFootnoteReference'))"
        />
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
  position: relative;
  padding-block-start: 6px;
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
.note-label,
.note-body :deep(.note-mark) {
  font-family: var(--commentary-header-font);
  font-weight: 600;
  margin-inline-end: 6px;
  padding: 0 2px;
  color: var(--text-primary);
  background: none;
  border: none;
  cursor: pointer;
  font-size: inherit;
  line-height: inherit;
}
.note-body :deep(.note-mark) {
  cursor: pointer;
  vertical-align: baseline;
  font-size: inherit;
}
/* Like the commentary entry header (CommentaryHeader .commentary-header:hover). */
.note-label:hover,
.note-body :deep(.note-mark:hover) {
  color: var(--accent-color);
}
/* The note's text follows the commentary font settings, as book-view entries do: the
   document's own run/paragraph sizes, families and leading (e.g. Word's "Footnote Text"
   10pt / Calibri / 1.15 written inline on each <p>) are dropped; bold/italic stay. */
.note-body :deep(*) {
  font-family: inherit !important;
  font-size: inherit !important;
  line-height: inherit !important;
}
/* Real superscript / subscript runs inside the note text keep their smaller size. */
.note-body :deep([style*='vertical-align:super']),
.note-body :deep([style*='vertical-align:sub']),
.note-body :deep(sup),
.note-body :deep(sub) {
  font-size: smaller !important;
}
/* The superscript run Word wraps around the note's own number: the number is the label now. */
.note-body :deep(:has(> .note-mark)) {
  vertical-align: baseline !important;
  font-size: inherit !important;
}
.note-body :deep(p) {
  display: inline;
  margin: 0;
}
</style>
