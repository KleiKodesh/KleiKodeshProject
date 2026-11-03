<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import WindowedScroller from '@/components/WindowedScroller.vue'
import type { FastDocViewIndexedNote } from './useFastDocViewNotes'
import { useSettingsStore } from '@/stores/settingsStore'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'

/**
 * Every footnote / endnote of the document, in body order, as ONE windowed scroller (the app's
 * WindowedScroller, as the text pane uses it): only the notes near the pane's window are fetched
 * and mounted, and the window fills the pane whatever the notes' lengths. Laid out like the book
 * view's bottom commentary panel, with no header row (Esc or the toolbar's notes toggle closes it).
 * Sync on: `followIndex` (the first note at or after the first block in view) lands at the top.
 * Sync off: the pane scrolls on its own. `landIndex` (a clicked reference's note) is loaded and
 * lands with a short ease in either mode; a note's number jumps the text to its reference.
 */
const props = defineProps<{
  noteCount: number
  ensureNotes: (start: number, end: number) => Promise<void>
  noteAt: (index: number) => FastDocViewIndexedNote | undefined
  rowSourceVersion?: unknown
  isRightToLeft: boolean
  /** A clicked reference's note: loaded and landed at the pane's top with a short ease. */
  landIndex?: number | null
  /** Bumped on every reference click, so clicking the same reference again lands it again. */
  landingNonce?: number
  /** Sync on: the note to follow (instant); null when sync is off. */
  followIndex?: number | null
  /** The document column's width at the note's reference block (the page margins); undefined = none. */
  columnStyle?: (blockIndex: number) => Record<string, string> | undefined
  fontPx: number
}>()
const emit = defineEmits<{ goToReference: [note: FastDocViewIndexedNote]; close: [] }>()

const scroller = ref<InstanceType<typeof WindowedScroller> | null>(null)
const pane = ref<HTMLElement | null>(null)
function onScrollerMounted(instance: unknown) {
  scroller.value = instance as InstanceType<typeof WindowedScroller> | null
  pane.value = scroller.value?.scrollerElement ?? null
}
/** The scroller: the page attaches the notes' own zoom handler to it (as a book-view commentary panel's). */
defineExpose({
  scrollerElement: pane,
  /** The mounted note rows [start, end): kept in the notes cache. */
  renderedRange: () => scroller.value?.renderedRange ?? { start: 0, end: 0 },
})

/** A clicked note lands with a short ease (not the browser's slow smooth scroll). */
const LANDING_MS = 180
let landingFrame = 0
let landingGeneration = 0
let isLanding = false
function cancelLanding() {
  if (landingFrame) cancelAnimationFrame(landingFrame)
  landingFrame = 0
  landingGeneration++
  isLanding = false
}
onBeforeUnmount(cancelLanding)

/** The row's top below the viewport's top, in px - read fresh each frame (the window may grow above it). */
function rowOffset(index: number): number | null {
  const viewport = pane.value
  const row = scroller.value?.rowElement(index)
  if (!viewport || !row || !scroller.value?.isRowRendered(index)) return null
  return row.getBoundingClientRect().top - viewport.getBoundingClientRect().top
}

/**
 * Loads note `index` and eases it to the pane's top. A note outside the mounted window is
 * loaded with the window rebuilt around the note before it (one exact landing), then eased
 * the last row. The reader's own wheel / press in the pane, or a newer landing, cancels it.
 */
async function landNote(index: number) {
  cancelLanding()
  const generation = landingGeneration
  isLanding = true
  try {
    if (!scroller.value) return
    if (rowOffset(index) == null) {
      await scroller.value.scrollToIndex(Math.max(0, index - 1))
      await nextTick()
      if (generation !== landingGeneration) return
    }
    const viewport = pane.value
    const initial = rowOffset(index)
    if (!viewport || initial == null || Math.abs(initial) < 2) return
    await new Promise<void>((resolve) => {
      const start = performance.now()
      const step = (now: number) => {
        const offset = generation === landingGeneration ? rowOffset(index) : null
        if (offset == null) { landingFrame = 0; resolve(); return }
        const t = Math.min(1, (now - start) / LANDING_MS)
        const remaining = initial * (1 - t) ** 3   // ease-out cubic: the distance still to go
        viewport.scrollTop = Math.min(viewport.scrollHeight - viewport.clientHeight, viewport.scrollTop + offset - remaining)
        if (t < 1) landingFrame = requestAnimationFrame(step)
        else { landingFrame = 0; resolve() }
      }
      landingFrame = requestAnimationFrame(step)
    })
  } finally {
    if (generation === landingGeneration) isLanding = false
  }
}

/** Waits (bounded) until the scroller exists and knows row `index` - the note list may still be loading. */
async function whenRowKnown(index: number): Promise<boolean> {
  for (let attempt = 0; attempt < 50 && (!scroller.value || props.noteCount <= index); attempt++) {
    await new Promise((resolve) => setTimeout(resolve, 100))
  }
  return !!scroller.value && props.noteCount > index
}

// Keep notes in sync with the text: the first note at or after the first block in view (instant).
watch(
  () => props.followIndex,
  async (index) => {
    // A click landing in progress wins.
    if (index == null || isLanding || !(await whenRowKnown(index))) return
    if (isLanding || props.followIndex !== index || rowOffset(index) === 0) return
    void scroller.value?.scrollToIndex(index)
  },
  { immediate: true, flush: 'post' },
)

// The clicked reference's note lands in view. `immediate` + post flush: a click on a reference
// also OPENS the pane (SplitPane mounts its bottom slot then), so the index is set before this
// component exists.
watch(
  () => [props.landIndex, props.landingNonce] as const,
  async ([index]) => {
    if (index == null) return
    isLanding = true
    if (!(await whenRowKnown(index))) { isLanding = false; return }
    void landNote(index)
  },
  { immediate: true, flush: 'post' },
)

/** The library marks a note's own number (Word's footnoteRef / ODF citation / RTF chftn / .doc
 *  auto mark) as <span class="note-mark">; that number is the note's one back-link to its reference. */
const settingsStore = useSettingsStore()
const NOTE_MARK = 'class="note-mark"'
function hasOwnMark(note: FastDocViewIndexedNote): boolean {
  return note.html.includes(NOTE_MARK)
}
function referenceTitle(note: FastDocViewIndexedNote, t: (key: string) => string): string {
  return note.kind === 'endnote' ? t('fastDocView.goToEndnoteReference') : t('fastDocView.goToFootnoteReference')
}
function bodyHtml(note: FastDocViewIndexedNote, title: string): string {
  // the book view's clean-text (Ctrl+D), as on its commentary
  return cleanHebrewText(note.html, settingsStore.cleanTextFlags).replace(NOTE_MARK, `${NOTE_MARK} role="button" tabindex="0" title="${title}"`)
}
function onBodyActivate(event: Event, note: FastDocViewIndexedNote) {
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
    <div v-if="noteCount === 0" class="state-overlay">
      <div class="hint-title">{{ $t('fastDocView.noNotes') }}</div>
    </div>
    <div v-else class="notes-body">
      <WindowedScroller
        :ref="onScrollerMounted"
        class="notes-scroller"
        :count="noteCount"
        :initial-index="landIndex ?? followIndex ?? 0"
        :ensure-rows="ensureNotes"
        :row-source-version="rowSourceVersion"
        tabindex="0"
        :style="{ fontSize: `${fontPx}px` }"
        @wheel.passive="cancelLanding"
        @pointerdown="cancelLanding"
      >
        <template #row="{ index }">
          <div
            v-if="noteAt(index)"
            class="line fast-doc-view-note"
            :style="columnStyle?.(noteAt(index)!.blockIndex)"
            :data-note-index="index"
            :data-block-index="noteAt(index)!.blockIndex"
          >
            <button
              v-if="!hasOwnMark(noteAt(index)!)"
              class="note-label"
              :title="referenceTitle(noteAt(index)!, $t)"
              @click="emit('goToReference', noteAt(index)!)"
            >{{ noteAt(index)!.label }}</button>
            <span
              class="note-body"
              @click="onBodyActivate($event, noteAt(index)!)"
              @keydown.enter="onBodyActivate($event, noteAt(index)!)"
              v-html="bodyHtml(noteAt(index)!, referenceTitle(noteAt(index)!, $t))"
            />
          </div>
        </template>
      </WindowedScroller>
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
.notes-body {
  flex: 1;
  min-height: 0;
}
/* the WindowedScroller's viewport (its attributes fall through to it) */
.notes-scroller {
  padding-block-start: 6px;
  --commentary-pad-inline: 12px;
}
/* As the commentary panel: more side room on a wide panel (its own width, not the window's). */
@container fast-doc-view-notes (min-width: 600px) {
  .notes-scroller {
    --commentary-pad-inline: 22px;
  }
}
/* The side margins of the document column above: its page text width (columnStyle sets
   --lines-content-max-width per note, from its reference's section) and its side padding. */
.line {
  padding-inline: var(--fast-doc-view-column-padding, var(--commentary-pad-inline, 12px));
  padding-block: 2px;
  max-width: var(--lines-content-max-width, none);
  margin-inline: auto;
  font-family: var(--commentary-text-font);
  font-size: var(--commentary-font-size, 100%);
  font-weight: var(--commentary-font-weight, 400);
  line-height: var(--commentary-line-height, 1.7);
  color: var(--text-primary);
  text-align: justify;
}
/* The note's own number looks like its footnote reference in the text (superscript, small,
   normal weight, link colour) and stays the click back to that reference. */
.note-label,
.note-body :deep(.note-mark) {
  font-family: inherit;
  font-weight: normal !important;
  font-size: 0.7em !important;
  line-height: 0 !important;
  vertical-align: super;
  margin-inline-end: 4px;
  padding: 0 1px;
  color: var(--accent-color);
  background: none;
  border: none;
  cursor: pointer;
}
.note-label:hover,
.note-body :deep(.note-mark:hover) {
  text-decoration: underline;
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
/* Note paragraphs stay blocks: Word footnotes have no margins of their own, but each paragraph keeps its
   own indents (w:ind - hanging indents are common in footnotes), which an inline paragraph would lose.
   Only a note without a number of its own runs its first paragraph on after the pane's label. */
.note-body {
  display: block;
}
.note-body :deep(p) {
  margin-block: 0;
}
.note-label + .note-body {
  display: inline;
}
.note-label + .note-body :deep(p:first-child) {
  display: inline;
}
</style>
