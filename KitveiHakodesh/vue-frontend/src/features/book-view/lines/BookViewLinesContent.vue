<script setup lang="ts">
import { computed, ref, watch, nextTick, onMounted, inject } from 'vue'
import { storeToRefs } from 'pinia'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import type { LineItem } from './useBookViewLinesTable'
import type { TocEntry } from '@/webview-host/queries.types'
import type { BookViewLinesScrollProps } from './useBookViewLinesScroll'
import ContextMenu from '@/components/ContextMenu.vue'
import { useContextMenuLongPress, hasActiveTextSelection } from '@/composables/useContextMenuLongPress'
import { useScopedKeys } from '../useTextSelectionKeys'
import { useScopedCopy, triggerCopy } from '@/composables/useLineCopy'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import { useZoomHandler } from '@/composables/useZoom'
import { useBookViewLineRenderer, setCurrentMark } from './useBookViewLineRenderer'
import { useBookViewLineCopyMenu } from './useBookViewLineCopyMenu'
import { useBookViewLineLink } from './useBookViewLineLink'
import { useBookViewAnnotations } from './useBookViewAnnotations'
import { useBookViewLinesScroll } from './useBookViewLinesScroll'
import { useBookViewLinesNavigation } from './useBookViewLinesNavigation'
import BookViewNoteBubble from './BookViewNoteBubble.vue'
import BookViewAbbrevTooltip from './BookViewAbbrevTooltip.vue'
import { useBookViewAbbrevTooltip } from './useBookViewAbbrevTooltip'
import WordLinkTooltip from './WordLinkTooltip.vue'
import { useWordLinkTooltip } from './useWordLinkTooltip'
import { useNoteTooltip } from './useNoteTooltip'
import { useWordLinkAnchors } from './useWordLinkAnchors'
import { useCopyExportData } from './useCopyExportData'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { pasteIntoWord } from '@/webview-host/bridge'

const emit = defineEmits<{
  scrolled: [firstVisible: number, firstFull: number, isUserScroll: boolean]
  lineSelected: [lineId: number, isShiftClick: boolean]
  'ctrl-f': []
}>()
// The scroll-related props come from useBookViewLinesScroll's own interface —
// never re-declare them here. The hand-copied list diverged once: all of those
// props are optional, so a renamed prop compiled clean while Vue silently
// dropped the page's binding into $attrs and saves went out empty.
const props = defineProps<
  BookViewLinesScrollProps & {
    lines: LineItem[]
    prioritise: (lineIndex: number) => void
    altTocLabelMap?: Map<number, string>
    searchQuery?: string
    currentMatchLineIndex?: number
    currentMatchOccurrence?: number
    searchHighlightQuery?: string
    searchHighlightSnippet?: string
    searchHighlightTerms?: string[]
    getActiveTocEntry?: (lineIndex: number) => TocEntry | null
    getTocPath?: (entry: TocEntry) => string
    selectedSectionLineIds?: number[] | null
    multiSelectLineIds?: number[] | null
    /**
     * This book's text carries cantillation marks. Switches the body font to the
     * teamim family. Per-pane rather than a global attribute, so a split view showing
     * a teamim book beside a plain one renders each in its own font.
     */
    bookHasTeamim?: boolean
  }
>()

const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const paneNavigation = usePaneNavigation()
const { autoSelectTopLine } = storeToRefs(bookViewStore)
const tabId = paneNavigation.activeTabId
const bookId = paneNavigation.activeTab.bookId!
const bookTitle = paneNavigation.activeTab.title

// Read lines zoom directly by tabId+bookId — NOT via bookViewStore.zoom computed (gated on
// activeTab). If this tab is not active when savePos fires, the computed returns DEFAULT.
const zoom = computed({
  get: () => bookViewStore.getLinesZoom(tabId, bookId),
  set: (value: number) => bookViewStore.setLinesZoom(tabId, bookId, value),
})

const diacriticsState = computed(() => settingsStore.diacriticsState)
const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)

const scrollerEl = ref<HTMLElement | null>(null)
useZoomHandler({ zoom, target: scrollerEl, keyboard: true })
const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, {
  onCtrlF: () => emit('ctrl-f'),
  // Ctrl+C is intercepted so it takes the same path as the menu's copy: the export
  // warm-up has to be awaited before the clipboard event fires (see prepareAndCopy).
  onCtrlC: () => onCopy(),
  onCtrlV: () => triggerCopy(() => pasteIntoWord().catch(() => {})),
  onCtrlShiftC: () => onSearchInRepository(),
})

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.lines.length,
    getScrollElement: () => scrollerEl.value,
    estimateSize: () => 32,
    overscan: 10,
  })),
)
const virtualItems = computed(() => virtualizer.value.getVirtualItems())
const totalSize = computed(() => virtualizer.value.getTotalSize())

// Type alias for the concrete Virtualizer type — avoids repeating the double-cast
// every time a composable that expects the concrete type receives virtualizer.value.
type ConcreteVirtualizer = import('@tanstack/vue-virtual').Virtualizer<Element, Element>
const getVirtualizer = () => virtualizer.value as unknown as ConcreteVirtualizer

useVirtualScrollerKeys(
  scrollerEl,
  getVirtualizer,
  () => props.lines.length,
)

const {
  getHighlightsForLine,
  getNotesForLine,
  loadNotesForLines,
  updateNote,
  deleteNote,
  activeBubbleNote,
  activeBubbleAnchorRect,
  closeNoteBubble,
  onHighlight,
  onClearHighlight,
  onAddNote,
  onMarkerClick,
} = useBookViewAnnotations(
  bookId,
  scrollerEl,
  () => props.lines,
  () => virtualItems.value.map((v) => props.lines[v.index]?.id ?? 0).filter((id) => id > 0),
)

const { suppressPositionSave, onScroll, captureScrollPos, readCurrentPosition, cancelRestoreCorrection } =
  useBookViewLinesScroll(
  scrollerEl,
  getVirtualizer,
  () => virtualItems.value,
  () => props.lines,
  props,
  { tabStore, bookViewStore, autoSelectTopLine, zoom, tabId, bookId },
  (event, firstVisible, firstFull, isUserScroll) =>
    emit(event, firstVisible, firstFull, isUserScroll),
  props.prioritise,
)

// Word-level link anchors (link_anchor, schema v2+ DBs) — lazy, viewport-driven,
// a no-op on DBs without the table.
const { getWordLinkAnchorsForLine, loadWordLinkAnchorsForLines } = useWordLinkAnchors(
  () => virtualItems.value.map((v) => props.lines[v.index]?.id ?? 0).filter((id) => id > 0),
)

const { lineContent } = useBookViewLineRenderer(settingsStore, diacriticsState, () => ({
  searchQuery: props.searchQuery,
  searchHighlightLineIndex: props.searchHighlightLineIndex,
  searchHighlightQuery: props.searchHighlightQuery,
  searchHighlightSnippet: props.searchHighlightSnippet,
  searchHighlightTerms: props.searchHighlightTerms,
  getHighlightsForLine,
  getNotesForLine,
  getWordLinkAnchorsForLine,
}))

// Apply .current class via DOM toggle — no re-render needed when only occurrence changes.
watch(
  () => [props.currentMatchLineIndex, props.currentMatchOccurrence] as const,
  ([lineIndex, occurrence]) => {
    if (!scrollerEl.value) return
    nextTick(() => {
      if (!scrollerEl.value) return
      setCurrentMark(scrollerEl.value, lineIndex ?? -1, occurrence ?? 0)
    })
  },
)

const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const { copyLineLink } = useBookViewLineLink({ scrollerEl, lines: () => props.lines, bookId })
const booksDataStore = useBooksDataStore()
// What copy-with-notes needs beyond the rendered markup: the notes and citations of
// lines that were never scrolled into view, plus the target lines those citations
// point at (they become endnotes). Warmed by the copy actions, never on render.
const { prepareForLines, prepareForRenderedHtml, resolveWordLinkTarget } = useCopyExportData({
  loadNotes: loadNotesForLines,
  loadWordLinkAnchors: loadWordLinkAnchorsForLines,
  getWordLinkAnchorsForLine,
  getBookTitle: (targetBookId) => booksDataStore.allBooksMap.get(targetBookId)?.title ?? '',
})
const { items: contextMenuItems, buildFormattedHtml, onCopy, onPasteIntoWord, onSearchInRepository } = useBookViewLineCopyMenu({
  scrollerEl,
  lines: () => props.lines,
  isSelectAll,
  selectAllInContainer,
  bookTitle,
  bookId,
  tabStore,
  paneNavigation,
  getActiveTocEntry: props.getActiveTocEntry,
  getTocPath: props.getTocPath,
  getNotesForLine,
  getRenderedLineContent: lineContent,
  onHighlight,
  onClearHighlight,
  onAddNote,
  onCopyLineLink: copyLineLink,
  prepareForLines,
  prepareForRenderedHtml,
  resolveWordLinkTarget,
})
useScopedCopy(
  scrollerEl,
  () => props.lines.map((line) => line.content).filter(Boolean) as string[],
  isSelectAll,
  buildFormattedHtml,
)
useContextMenuLongPress(scrollerEl, (event) => {
  if (!scrollerEl.value) return
  // In RTL layout the scrollbar is on the physical LEFT side of the container.
  // clientWidth excludes the scrollbar track, so the scrollbar occupies the gap
  // between the element's left edge and (left + offsetWidth - clientWidth).
  // Any pointer position to the left of (rect.left + scrollbarWidth) hit the scrollbar.
  const el = scrollerEl.value
  const rect = el.getBoundingClientRect()
  const scrollbarWidth = el.offsetWidth - el.clientWidth
  if (event.clientX <= rect.left + scrollbarWidth) return
  contextMenuRef.value?.showAtPosition(event.clientX, event.clientY)
})

const { abbrevTooltip } = useBookViewAbbrevTooltip(scrollerEl)

// User-note hover preview — the marker carries its text in data-note-text, so this
// replaces the native title tooltip the marker used to have.
const { noteTooltip } = useNoteTooltip(scrollerEl)

const {
  wordLinkTooltip,
  closeWordLinkTooltip,
  keepOpen: keepWordLinkTooltipOpen,
  releaseOpen: releaseWordLinkTooltip,
  beginSelection: beginWordLinkTooltipSelection,
} = useWordLinkTooltip(scrollerEl, {
  getBookTitle: (targetBookId) => booksDataStore.allBooksMap.get(targetBookId)?.title ?? '',
  onNavigate: (target) => {
    paneNavigation.openBookTarget({
      title: booksDataStore.allBooksMap.get(target.bookId)?.title ?? '',
      route: '/book-view',
      bookId: target.bookId,
      openTocLineIndex: target.lineIndex,
    })
  },
})

const { scrollToLine, scrollToLineId } = useBookViewLinesNavigation(
  scrollerEl,
  getVirtualizer,
  () => virtualItems.value,
  () => props.lines,
  () => props.searchBarVisible ?? false,
  suppressPositionSave,
  cancelRestoreCorrection,
  props.prioritise,
)

function onLineClick(index: number, event: MouseEvent) {
  // A drag to select text ends with a `click` event too. Don't hijack it to change
  // the commentary line — the user only wants to select text. A plain click leaves
  // the selection collapsed; a drag-select leaves real (non-empty) text selected.
  if (hasActiveTextSelection()) return
  const line = props.lines[index]
  if (props.commentaryVisible && line) emit('lineSelected', line.id, event.ctrlKey || event.metaKey)
}

const selectedSectionLineIdSet = computed(() =>
  props.selectedSectionLineIds ? new Set(props.selectedSectionLineIds) : null,
)

const multiSelectLineIdSet = computed(() =>
  props.multiSelectLineIds ? new Set(props.multiSelectLineIds) : null,
)

function isInActiveSection(lineIndex: number): boolean {
  const set = selectedSectionLineIdSet.value
  if (!set) return false
  const line = props.lines[lineIndex]
  if (!line) return false
  return set.has(line.id)
}

function isMultiSelected(lineIndex: number): boolean {
  const set = multiSelectLineIdSet.value
  if (!set) return false
  const line = props.lines[lineIndex]
  if (!line) return false
  return set.has(line.id)
}

function focusScroller() {
  scrollerEl.value?.focus({ preventScroll: true })
}

// Focus the lines view once it has loaded (this component only mounts once
// scrollStateReady is true), so the arrow keys / PageUp-PageDown drive the
// reader immediately without a click. Guard against stealing focus from a
// background pane in split view, or from an input the user is already typing
// in (e.g. an open search bar).
const paneId = inject<1 | 2>('paneId', 1)
onMounted(() => {
  nextTick(() => {
    if (bookViewStore.splitViewEnabled && bookViewStore.focusedPaneId !== paneId) return
    const focused = document.activeElement as HTMLElement | null
    if (focused && (focused.tagName === 'INPUT' || focused.tagName === 'TEXTAREA' || focused.isContentEditable)) return
    focusScroller()
  })
})

defineExpose({ scrollToLine, scrollToLineId, focusScroller, captureScrollPos, readCurrentPosition })
</script>

<template>
  <div class="lines-content">
    <ContextMenu ref="contextMenuRef" :items="contextMenuItems" />
    <BookViewNoteBubble
      v-if="activeBubbleNote && activeBubbleAnchorRect"
      :note="activeBubbleNote"
      :anchor-rect="activeBubbleAnchorRect"
      :update-note="updateNote"
      :delete-note="deleteNote"
      @close="closeNoteBubble"
      @deleted="closeNoteBubble"
    />
    <BookViewAbbrevTooltip
      v-if="abbrevTooltip"
      :key="abbrevTooltip.id"
      :data="abbrevTooltip"
    />
    <WordLinkTooltip
      v-if="wordLinkTooltip"
      :key="wordLinkTooltip.id"
      :data="wordLinkTooltip"
      @pointer-enter="keepWordLinkTooltipOpen"
      @pointer-leave="releaseWordLinkTooltip"
      @select-start="beginWordLinkTooltipSelection"
      @close="closeWordLinkTooltip"
    />
    <WordLinkTooltip
      v-if="noteTooltip"
      :key="`note-${noteTooltip.id}`"
      :data="noteTooltip"
      :interactive="false"
    />
    <div
      ref="scrollerEl"
      class="scroller"
      tabindex="0"
      data-ctrlf-enabled
      :style="{ fontSize: `${fontPx}px` }"
      :data-teamim="bookHasTeamim ? 'true' : 'false'"
      @scroll="onScroll"
      @click="onMarkerClick"
      @contextmenu="contextMenuRef?.show($event)"
    >
      <div :style="{ height: `${totalSize}px`, position: 'relative' }">
        <div
          v-for="vItem in virtualItems"
          :key="String(vItem.key)"
          :ref="(el) => el && virtualizer.measureElement(el as Element)"
          :data-index="vItem.index"
          :style="{
            position: 'absolute',
            top: 0,
            right: 0,
            left: 0,
            transform: `translateY(${vItem.start}px)`,
          }"
        >
          <div
            v-if="lines[vItem.index]?.content != null"
            class="line"
            :class="{
              selected: props.commentaryVisible && !multiSelectLineIdSet && selectedLineId === lines[vItem.index]?.id,
              'toc-section': props.commentaryVisible && !multiSelectLineIdSet && isInActiveSection(vItem.index),
              'multi-selected': props.commentaryVisible && isMultiSelected(vItem.index),
            }"
            :data-alt-toc="props.altTocLabelMap?.get(vItem.index)"
            v-html="lineContent(lines[vItem.index]!.content!, vItem.index, lines[vItem.index]!.id)"
            @click="onLineClick(vItem.index, $event)"
          />
          <div v-else class="line placeholder" />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.lines-content {
  height: 100%;
  position: relative;
}
.scroller {
  height: 100%;
  overflow-y: auto;
  padding-block-end: 24px;
}
.line {
  padding-inline: 12px;
  max-width: var(--lines-content-max-width, none);
  margin-inline: auto;
  font-family: var(--text-font);
  font-size: var(--font-size, 100%);
  font-weight: var(--font-weight, 400);
  line-height: var(--line-height, 1.7);
  color: var(--text-primary);
  text-align: justify;
  position: relative;
}
/* Books whose text carries cantillation marks get their own body font — set on the
   scroller so each pane in a split view follows its own book, not a global flag. */
.scroller[data-teamim='true'] .line {
  font-family: var(--teamim-text-font);
}
/* Exact line spacing (רווח מדוייק), opt-in from settings.
   `line-height` is normally a unitless multiplier, so every inline element
   recomputes its own line box from its own font-size — one enlarged word mid-line
   makes THAT row taller than its neighbours. Multiplying by 1em resolves the
   leading once, against .line's font-size, and it then inherits as a fixed length;
   the explicit inherit on inline children stops those with their own font-size
   (bold runs, note markers, wrapper spans) from contributing a taller box.
   Trade-off: a genuinely oversized word now overlaps the row above instead of
   pushing it away — which is why this is off by default.

   The attribute is matched on a bare `html[...]` ancestor, NOT via `:global()`:
   Vue's scoped-CSS compiler replaces the whole selector with the `:global()`
   argument and discards the rest, so `:global([attr]) .line` would collapse to a
   bare `[attr]` — matching <html> itself and leaking line-height app-wide. */
html[data-fixed-line-height='true'] .line {
  line-height: calc(var(--line-height, 1.7) * 1em);
}
/* Inline text elements only. Deliberately NOT `*`: the superscript markers below
   set `line-height: 1` so they contribute no box at all, and nested blocks keep
   their own leading. */
html[data-fixed-line-height='true'] .line :deep(b),
html[data-fixed-line-height='true'] .line :deep(strong),
html[data-fixed-line-height='true'] .line :deep(i),
html[data-fixed-line-height='true'] .line :deep(em),
html[data-fixed-line-height='true'] .line :deep(big),
html[data-fixed-line-height='true'] .line :deep(span),
html[data-fixed-line-height='true'] .line :deep(a),
html[data-fixed-line-height='true'] .line :deep(mark),
html[data-fixed-line-height='true'] .line :deep(font) {
  line-height: inherit;
}
/* …but the markers keep their zero-height box even in exact mode. */
html[data-fixed-line-height='true'] .line :deep(.user-note-marker),
html[data-fixed-line-height='true'] .line :deep(.word-link-marker) {
  line-height: 1;
}
/* On a wide pane give the reading column a bit more breathing room from the
   edges (רווח נוסף מהצדדים במסך רחב). Container query on the app-shell pane, so
   it reacts to THIS pane's width, not the viewport (split-shell aware). */
@container app-shell (min-width: 600px) {
  .line {
    padding-inline: 22px;
  }
}
.line.placeholder {
  height: 28px;
  max-width: var(--lines-content-max-width, none);
  margin-inline: auto;
  padding-inline: 0;
  margin-block: 4px;
  border-radius: 4px;
  background: color-mix(in srgb, var(--text-primary) 5%, transparent);
}
.line::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  right: 4px;
  width: 3px;
  background: var(--accent-color);
  opacity: 0;
  transition: opacity 150ms ease;
}
.line.toc-section::after {
  opacity: 0.2;
}
.line.selected::after {
  opacity: 1;
}
.line.multi-selected {
  background: color-mix(in srgb, var(--accent-color) 8%, transparent);
}
/* Momentary flash when the line is opened from a deep link (otzaria:// / zayit://).
   A soft, light orange that eases in, holds briefly, then fades through several
   gradient stops to transparent for a gentle finish. One-shot animation; the class
   is removed imperatively. */
.line.flash-open {
  border-radius: 6px;
  animation: line-flash-open 3.5s cubic-bezier(0.33, 0, 0.2, 1);
}
@keyframes line-flash-open {
  0%   { background: rgba(255, 190, 90, 0); }
  12%  { background: rgba(255, 190, 90, 0.28); }
  40%  { background: rgba(255, 190, 90, 0.28); }
  60%  { background: rgba(255, 196, 105, 0.2); }
  78%  { background: rgba(255, 205, 125, 0.11); }
  92%  { background: rgba(255, 215, 150, 0.04); }
  100% { background: rgba(255, 215, 150, 0); }
}
@media (prefers-reduced-motion: reduce) {
  .line.flash-open { animation-duration: 1.5s; }
}
.line.multi-selected::after {
  opacity: 1;
}
.line[data-alt-toc]::before {
  content: attr(data-alt-toc);
  display: block;
  font-size: 0.85rem;
  font-weight: 600;
  opacity: 0.35;
  padding-block-end: 2px;
}
/* Headings stay heavier than the body at EVERY slider stop, instead of inheriting
   its weight. 800 rather than bold(700): the weight slider tops out at 700, so a
   700 pin would render headings identically to a maxed-out body. */
.line :deep(h1),
.line :deep(h2),
.line :deep(h3),
.line :deep(h4),
.line :deep(h5),
.line :deep(h6) {
  font-family: var(--header-font);
  font-weight: 800;
}
.line :deep(mark.search-match) {
  background: rgba(255, 165, 0, 0.4);
  color: inherit;
  border-radius: 2px;
}
.line :deep(mark.search-match.current) {
  background: rgba(255, 165, 0, 0.9);
  color: #000;
}
.line :deep(mark.user-highlight) {
  border-radius: 2px;
}
.line :deep(.user-note-marker) {
  font-size: 0.72em;
  vertical-align: super;
  line-height: 1;
  color: var(--accent-color);
  cursor: pointer;
  font-style: normal;
  font-weight: normal;
  letter-spacing: 0;
  transition: color 100ms;
}
/* The mark is drawn by CSS so the marker holds no text — see applyUserNoteMarkers. */
.line :deep(.user-note-marker)::before {
  content: '✎︎';
}
.line :deep(.user-note-marker:hover) {
  color: color-mix(in srgb, var(--accent-color) 70%, var(--text-primary));
}
.line :deep(.word-link) {
  color: var(--accent-color);
  cursor: pointer;
  text-decoration: underline dotted transparent;
  text-underline-offset: 3px;
  transition: text-decoration-color 100ms;
}
.line :deep(.word-link:hover) {
  text-decoration-color: currentColor;
}
/* Colored per commentary via --wl-marker-color (palette in main.css), muted by
   opacity so the marks don't compete with the text; hover restores the affordance. */
.line :deep(.word-link-marker) {
  font-size: 0.72em;
  vertical-align: super;
  line-height: 1;
  color: var(--wl-marker-color, var(--text-secondary));
  opacity: 0.65;
  cursor: pointer;
  font-style: normal;
  font-weight: var(--wl-marker-weight, normal);
  text-decoration: var(--wl-marker-decoration, none);
  text-underline-offset: 2px;
  letter-spacing: 0;
  transition: color 100ms;
}
/* Label rendered via CSS so the marker contributes zero text characters —
   annotation offsets and selection extraction must not drift. */
.line :deep(.word-link-marker)::before {
  content: var(--wl-marker-open, '') attr(data-wl-label) var(--wl-marker-close, '');
}
.line :deep(.word-link-marker:hover) {
  color: color-mix(in srgb, var(--accent-color) 70%, var(--text-primary));
  opacity: 1;
}
</style>
