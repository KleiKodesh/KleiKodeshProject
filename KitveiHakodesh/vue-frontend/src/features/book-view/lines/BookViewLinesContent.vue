<script setup lang="ts">
import { computed, ref, watch, nextTick, onMounted, inject } from 'vue'
import { storeToRefs } from 'pinia'
import WindowedScroller from '@/components/WindowedScroller.vue'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
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
import { useVirtualizedTextSelection } from '@/composables/useVirtualizedTextSelection'
import { useScopedCopy, triggerCopy } from '@/composables/useLineCopy'
import { useZoomHandler } from '@/composables/useZoom'
import {
  useBookViewLineRenderer,
  setCurrentMark,
  annotationOffsetLength,
  contentInAnnotationOffsetSpace,
} from './useBookViewLineRenderer'
import { useBookViewLineCopyMenu } from './useBookViewLineCopyMenu'
import { useBookViewLineLink } from './useBookViewLineLink'
import BookViewErrorReportDialog from '../errorReport/BookViewErrorReportDialog.vue'
import { useBookViewErrorReport } from '../errorReport/useBookViewErrorReport'
import { useBookViewAnnotations } from './useBookViewAnnotations'
import { useBookViewLinesScroll } from './useBookViewLinesScroll'
import { useBookViewLinesNavigation } from './useBookViewLinesNavigation'
import BookViewNoteBubble from './BookViewNoteBubble.vue'
import BookViewAbbrevTooltip from './BookViewAbbrevTooltip.vue'
import { useBookViewAbbrevTooltip } from './useBookViewAbbrevTooltip'
import WordLinkTooltip from './WordLinkTooltip.vue'
import { useWordLinkTooltip } from './useWordLinkTooltip'
import { useNoteTooltip } from './useNoteTooltip'
import BookViewScrollbarTocTooltip from './BookViewScrollbarTocTooltip.vue'
import { useBookViewScrollbarTocTooltip } from './useBookViewScrollbarTocTooltip'
import { useWordLinkAnchors } from './useWordLinkAnchors'
import { useCopyExportData } from './useCopyExportData'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { pasteIntoWord } from '@/webview-host/bridge'
import { showToast } from '@/composables/useToast'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

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
    /**
     * Bumped on every write to `lines`, which is a shallow store: the row slot reads it
     * so the mounted rows re-render when their text changes in place (a version swap).
     */
    linesRevision?: number
    /** Resolves once the lines of [start, end) have their text — the scroller's row source. */
    ensureLines: (start: number, end: number) => Promise<void>
    /** Resolves once every line of the book has its text — whole-book actions only. */
    ensureAllLines: () => Promise<void>
    altTocLabelMap?: Map<number, string>
    searchQuery?: string
    currentMatchLineIndex?: number
    currentMatchOccurrence?: number
    searchHighlightQuery?: string
    searchHighlightSnippet?: string
    searchHighlightTerms?: string[]
    getActiveTocEntry?: (lineIndex: number) => TocEntry | null
    getActiveAltTocEntry?: (lineIndex: number) => TocEntry | null
    getTocPath?: (entry: TocEntry) => string
    /** The book's TOC — the scrollbar's landmarks, one tier per level. */
    tocEntries?: TocEntry[]
    selectedSectionLineIds?: number[] | null
    multiSelectLineIds?: number[] | null
    /**
     * This book's text carries cantillation marks. Switches the body font to the
     * teamim family. Per-pane rather than a global attribute, so a split view showing
     * a teamim book beside a plain one renders each in its own font.
     */
    bookHasTeamim?: boolean
    /**
     * Every line's character count by lineIndex, from the library — the scrollbar's
     * static per-row height estimate. null while loading, or for a library without
     * the column; the bar then estimates from the rows it measures.
     */
    lineCharacterCounts?: ArrayLike<number> | null
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

const cleanTextFlags = computed(() => settingsStore.cleanTextFlags)
const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)

// The windowed scroller mounts the rows around any target before it scrolls, so
// every position read here is a real DOM measurement — see WindowedScroller.vue.
const windowedScroller = ref<InstanceType<typeof WindowedScroller> | null>(null)
const scroller = (): WindowedScrollerApi | null => windowedScroller.value
// The native scroll container inside it: focus, keys and every pointer listener
// below go on this element. Filled once the child has mounted.
const scrollerEl = ref<HTMLElement | null>(null)
onMounted(() => { scrollerEl.value = windowedScroller.value?.scrollerElement ?? null })
const scrollbarElement = computed(() => windowedScroller.value?.scrollbarElement ?? null)
// The TOC's section starts by level, coarse first: the scrollbar gives each a
// minimum span of track, taking as many levels as fit.
const tocLandmarkTiers = computed<number[][]>(() => {
  const byLevel = new Map<number, number[]>()
  for (const entry of props.tocEntries ?? []) {
    if (entry.lineIndex == null) continue
    let tier = byLevel.get(entry.level)
    if (!tier) byLevel.set(entry.level, (tier = []))
    tier.push(entry.lineIndex)
  }
  return [...byLevel.entries()]
    .sort(([first], [second]) => first - second)
    .map(([, tier]) => tier.sort((first, second) => first - second))
})
/** Ids of the lines mounted in the window — the viewport-driven loaders' input. */
const renderedLineIds = (): number[] => {
  const range = windowedScroller.value?.renderedRange
  if (!range) return []
  const ids: number[] = []
  for (let index = range.start; index < range.end; index++) {
    const id = props.lines[index]?.id ?? 0
    if (id > 0) ids.push(id)
  }
  return ids
}
useZoomHandler({ zoom, target: scrollerEl, keyboard: true })
const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, {
  onCtrlF: () => emit('ctrl-f'),
  // Ctrl+C is intercepted so it takes the same path as the menu's copy: the export
  // warm-up has to be awaited before the clipboard event fires (see prepareAndCopy).
  onCtrlC: () => onCopy(),
  onCtrlV: () => triggerCopy(() => pasteIntoWord().catch(() => {})),
  onCtrlShiftC: () => onSearchInRepository(),
})

// Keeps the rows holding a selection's two ends mounted however far they scroll
// away, so a drag past the viewport edge cannot break the selection.
const {
  pinnedRowIndexes,
  selectedRowIndexRange,
  restoreSelectionFromPinnedRows,
} = useVirtualizedTextSelection(
  scrollerEl,
  // The version overlay keeps the line ids and the count but replaces every line's
  // text, so the count alone would not notice it.
  () => `${props.activeVersionId ?? 0}:${props.lines.length}`,
)

// A selection dragged past the window spans lines the scroller never mounted: their
// text is fetched as the selection grows, so copy and annotations can read it.
watch(
  () => selectedRowIndexRange(),
  (range) => {
    if (range) void props.ensureLines(range.firstRowIndex, range.lastRowIndex + 1).catch(() => {})
  },
)

const {
  getHighlightsForLine,
  getNotesForLine,
  getNoteById,
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
  renderedLineIds,
  restoreSelectionFromPinnedRows,
  selectedRowIndexRange,
  // Measured in the annotation offset space — after the diacritics and censor
  // transforms, which is the text the highlight walk addresses.
  (content) =>
    annotationOffsetLength(
      contentInAnnotationOffsetSpace(content, cleanTextFlags.value, settingsStore.censorOptions),
    ),
)

const { suppressPositionSave, onScroll, onScrollerOpened, captureScrollPos, readCurrentPosition, cancelRestore } =
  useBookViewLinesScroll(
  scroller,
  () => props.lines,
  props,
  { tabStore, bookViewStore, autoSelectTopLine, zoom, tabId, bookId },
  (event, firstVisible, firstFull, isUserScroll) =>
    emit(event, firstVisible, firstFull, isUserScroll),
)

// Word-level link anchors (link_anchor, schema v2+ DBs) — lazy, viewport-driven,
// a no-op on DBs without the table.
const { getWordLinkAnchorsForLine, loadWordLinkAnchorsForLines, evictWordLinkAnchorsForLines } = useWordLinkAnchors(renderedLineIds)

const { lineContent } = useBookViewLineRenderer(settingsStore, cleanTextFlags, () => ({
  activeVersionId: props.activeVersionId,
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
const { copyLineLink, pressedLineIndex } = useBookViewLineLink({ scrollerEl, lines: () => props.lines, bookId })
const {
  target: errorReportTarget,
  openErrorReport,
  closeErrorReport,
  awaitSourceResolution: awaitErrorReportSource,
} = useBookViewErrorReport({
  lines: () => props.lines,
  pressedLineIndex,
  scrollerEl,
  bookId,
  activeVersionId: () => props.activeVersionId ?? null,
})
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
const { items: contextMenuItems, buildFormattedHtml, onCopy, onPasteIntoWord, onSearchInRepository, buildSource } = useBookViewLineCopyMenu({
  scrollerEl,
  lines: () => props.lines,
  isSelectAll,
  selectAllInContainer,
  selectedRowIndexRange,
  bookTitle,
  bookId,
  tabStore,
  paneNavigation,
  getActiveTocEntry: props.getActiveTocEntry,
  getTocPath: props.getTocPath,
  getNoteById,
  getRenderedLineContent: lineContent,
  onHighlight,
  onClearHighlight,
  onAddNote,
  onCopyLineLink: copyLineLink,
  onReportError: openErrorReport,
  prepareForLines,
  prepareForRenderedHtml,
  resolveWordLinkTarget,
  ensureAllLines: () => props.ensureAllLines(),
})
useScopedCopy(
  scrollerEl,
  () => props.lines.map((line) => line.content).filter(Boolean) as string[],
  isSelectAll,
  buildFormattedHtml,
)
useContextMenuLongPress(scrollerEl, (event) => {
  contextMenuRef.value?.showAtPosition(event.clientX, event.clientY)
})

const { abbrevTooltip } = useBookViewAbbrevTooltip(scrollerEl)

// User-note hover preview — the marker carries its text in data-note-text, so this
// replaces the native title tooltip the marker used to have.
const { noteTooltip, closeNoteTooltip } = useNoteTooltip(scrollerEl)

// Hovering the scrollbar shows the reader's current book and TOC path — reaching
// for the scrollbar is when "where am I?" gets asked.
const { scrollbarTocTooltip, refreshScrollbarTocTooltip } = useBookViewScrollbarTocTooltip(scrollbarElement, {
  getCurrentLineIndex: () => readCurrentPosition()?.lineIndex ?? null,
  getTargetLineIndex: (clientY) => scroller()?.barTargetAt(clientY) ?? null,
  viewportElement: scrollerEl,
  getBookTitle: () => bookTitle,
  getActiveTocEntry: () => props.getActiveTocEntry,
  getActiveAltTocEntry: () => props.getActiveAltTocEntry,
  getTocPath: () => props.getTocPath,
})

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
  scroller,
  () => props.lines,
  () => props.searchBarVisible ?? false,
  suppressPositionSave,
  cancelRestore,
)

/** Whether the row at `index` has its text — read in the row slot, with the lines revision so a text change under the same index re-renders the row. */
function rowHasText(index: number): boolean {
  void props.linesRevision
  return props.lines[index]?.content != null
}

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

// Toolbar export to Word: the custom copy + paste-into-Word path, over the whole book
// (as if the user had pressed Ctrl+A first) rather than writing a new document.
//
// Unlike Ctrl+C, the notes warm-up is awaited IN FULL, so a cold book cannot lose
// endnotes. If that outlasts the click's clipboard activation, execCommand('copy')
// does nothing — the warm-up is cached by then, so a second click goes through
// instantly; the toast says so. The select-all is dropped afterwards either way.
async function pasteAllIntoWord(): Promise<void> {
  // The whole book is exported, and lines are fetched only on demand: fetch it now.
  try { await props.ensureAllLines() } catch { /* export what is loaded */ }
  selectAllInContainer()
  if (settingsStore.copyWithNotes) {
    try { await prepareForLines(props.lines.map((l) => l.id)) } catch { /* export degrades; proceed */ }
  }
  // Held so the caller's progress spinner lasts until Word has the paste.
  let pasting: Promise<unknown> = Promise.resolve()
  const copied = triggerCopy(() => { pasting = pasteIntoWord().catch(() => {}) })
  window.getSelection()?.removeAllRanges()
  await pasting
  if (!copied) showToast(t('bookView.exportReady'))
}

/**
 * The line ranges whose text the lines store must keep whatever its budget: the
 * mounted window, the pinned selection ends and the selected rows (a select-all
 * is the whole book, so a copy never reads an evicted line).
 */
function retainedLineRanges(): ReadonlyArray<readonly [number, number]> {
  const ranges: (readonly [number, number])[] = []
  const window = windowedScroller.value?.renderedRange
  if (window) ranges.push([window.start, window.end])
  for (const index of pinnedRowIndexes.value) ranges.push([index, index + 1])
  const selection = selectedRowIndexRange()
  if (selection) ranges.push([selection.firstRowIndex, selection.lastRowIndex + 1])
  return ranges
}

/** Lines whose text the store just released: their per-line annotation caches go with it. */
function onLinesTextEvicted(lineIds: number[]) {
  evictWordLinkAnchorsForLines(lineIds)
}

defineExpose({ pasteAllIntoWord, scrollToLine, scrollToLineId, focusScroller, captureScrollPos, readCurrentPosition, retainedLineRanges, onLinesTextEvicted })
</script>

<template>
  <div dir="rtl" class="lines-content">
    <ContextMenu ref="contextMenuRef" :items="contextMenuItems" />
    <BookViewErrorReportDialog
      v-if="errorReportTarget"
      :selected-text="errorReportTarget.selectedText"
      :original-line="errorReportTarget.originalLine"
      :selection-offset="errorReportTarget.selectionOffset"
      :book-title="bookTitle"
      :current-ref="buildSource(errorReportTarget.lineIndex >= 0 ? errorReportTarget.lineIndex : null)"
      :line-index="errorReportTarget.lineIndex"
      :book-id="bookId"
      :source="errorReportTarget.source"
      :await-source="awaitErrorReportSource"
      @close="closeErrorReport"
    />
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
      @close="closeNoteTooltip"
    />
    <BookViewScrollbarTocTooltip v-if="scrollbarTocTooltip" :data="scrollbarTocTooltip" />
    <WindowedScroller
      ref="windowedScroller"
      class="scroller"
      :count="lines.length"
      :initial-index="initialLineIndex ?? initialScrollIndex"
      :initial-offset-px="initialLineIndex == null ? initialScrollOffset : 0"
      :ensure-rows="ensureLines"
      :row-source-version="activeVersionId"
      :pinned-indexes="pinnedRowIndexes"
      :landmark-tiers="tocLandmarkTiers"
      :row-weights="lineCharacterCounts ?? undefined"
      tabindex="0"
      data-ctrlf-enabled
      :style="{ fontSize: `${fontPx}px` }"
      :data-teamim="bookHasTeamim ? 'true' : 'false'"
      @scroll="onScroll(); refreshScrollbarTocTooltip()"
      @opened="onScrollerOpened"
      @click="onMarkerClick"
      @contextmenu="contextMenuRef?.show($event)"
    >
      <template #row="{ index }">
        <!-- Never a placeholder: the scroller asked for these lines before mounting them. -->
        <div
          v-if="rowHasText(index)"
          class="line"
          :class="{
            selected: props.commentaryVisible && !multiSelectLineIdSet && selectedLineId === lines[index]?.id,
            'toc-section': props.commentaryVisible && !multiSelectLineIdSet && isInActiveSection(index),
            'multi-selected': props.commentaryVisible && isMultiSelected(index),
          }"
          :data-alt-toc="props.altTocLabelMap?.get(index)"
          v-html="lineContent(lines[index]!.content!, index, lines[index]!.id)"
          @click="onLineClick(index, $event)"
        />
      </template>
    </WindowedScroller>
  </div>
</template>

<style scoped>
.lines-content {
  height: 100%;
  position: relative;
}
/* `.scroller` lands on the WindowedScroller's viewport (attributes fall through). */
.scroller :deep(.windowed-scroller-content) {
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
.line::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  inset-inline-start: 4px;
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
/* Headings are always 200 units heavier than the body so they stay visually
   distinct at every slider stop. calc() is clamped to 100–900 by the browser;
   the slider tops out at 700, so the maximum produced here is 900. */
.line :deep(h1),
.line :deep(h2),
.line :deep(h3),
.line :deep(h4),
.line :deep(h5),
.line :deep(h6) {
  font-family: var(--header-font);
  font-weight: calc(var(--font-weight, 400) + 300);
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
/* Muted by opacity so the marks don't compete with the text; hover restores the
   affordance. All markers share the same color (--text-secondary fallback). */
.line :deep(.word-link-marker) {
  font-size: 0.6em;
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
