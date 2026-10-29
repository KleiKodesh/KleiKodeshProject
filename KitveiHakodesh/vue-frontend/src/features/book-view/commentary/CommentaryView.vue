<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { useTimeoutFn } from '@vueuse/core'
import { useContextMenuLongPress } from '@/composables/useContextMenuLongPress'
import { useScopedKeys } from '../useTextSelectionKeys'
import { useScopedCopy, triggerCopy } from '@/composables/useLineCopy'
import WindowedScroller from '@/components/WindowedScroller.vue'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useZoomHandler } from '@/composables/useZoom'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import CommentaryHeader from './CommentaryHeader.vue'
import CommentaryHeaderNav from './CommentaryHeaderNav.vue'
import ContextMenu from '@/components/ContextMenu.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import type { CommentaryGroup, CommentaryLine } from './useCommentary'
import { commentaryGroupKey } from './useCommentary'
import type { CommentarySlot, PinnedCommentaryGroup } from '../bookViewTypes'
import { useCommentaryScroll } from './useCommentaryScroll'
import { useCommentaryCopy } from './useCommentaryCopy'
import { useCommentaryErrorReport } from '../errorReport/useCommentaryErrorReport'
import BookViewErrorReportDialog from '../errorReport/BookViewErrorReportDialog.vue'
import { useVirtualizedTextSelection } from '@/composables/useVirtualizedTextSelection'
import type { Highlight } from '../lines/useBookViewHighlights'
import type { Note } from '../lines/useBookViewNotes'
import BookViewNoteBubble from '../lines/BookViewNoteBubble.vue'
import WordLinkTooltip from '../lines/WordLinkTooltip.vue'
import { useWordLinkTooltip } from '../lines/useWordLinkTooltip'
import { useNoteTooltip } from '../lines/useNoteTooltip'
import type { WordLinkTarget } from '../lines/wordLinkAnchors'
import type { WordLinkTargetContent } from '../lines/wordLinkExport'
import BookViewAbbrevTooltip from '../lines/BookViewAbbrevTooltip.vue'
import BookViewScrollbarTocTooltip from '../lines/BookViewScrollbarTocTooltip.vue'
import { useBookViewScrollbarTocTooltip } from '../lines/useBookViewScrollbarTocTooltip'
import { useBookViewAbbrevTooltip } from '../lines/useBookViewAbbrevTooltip'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { pasteIntoWord } from '@/webview-host/bridge'
import { teamimFontStyle } from '@/utils/teamimFont'

const props = defineProps<{
  /**
     * Which commentary panel this is. Stamped on the root so Ctrl+F can tell them
     * apart. Named slotName, not slot: `slot` is a reserved attribute in templates.
     */
  slotName: CommentarySlot
  selectedLineId: number | null
  /**
   * The rows to draw, already filtered by this panel's check-tree and filter-tree
   * search (see filterVisibleGroups). Do not filter again here - the panel's search
   * scans this same list, and a second filter would desync their flat indices.
   */
  groups: CommentaryGroup[]
  loading: boolean
  // True when the commentary load failed (DB/bridge error) — show an error
  // message instead of the misleading "no commentaries for this line".
  loadError?: boolean
  // Hoisted annotation & render state — initialized in useBookView, survive v-if toggle
  getHighlightsForLine: (lineId: number) => Highlight[]
  // Promise-returning: both persist to the settings database before mutating the
  // reactive state that re-renders the lines, and the selection restore has to wait
  // for that (see restoreSelectionAfterAnnotation in useCommentaryCopy).
  applyHighlight: (
    lineId: number,
    startOffset: number,
    endOffset: number,
    colorArgb: number,
  ) => Promise<void>
  clearHighlight: (lineId: number, startOffset: number, endOffset: number) => Promise<void>
  getNotesForLine: (lineId: number) => Note[]
  scheduleNotesLoad: (lineIds: number[]) => void
  // Viewport-driven lazy load of word-level link anchors (link_anchor, schema v2+ DBs).
  scheduleWordLinkAnchorsLoad?: (lineIds: number[]) => void
  // Copy-with-notes needs the notes and citations of lines never scrolled into view,
  // plus the target lines the citations point at — see useCopyExportData.
  prepareExportData?: (lineIds: number[]) => Promise<void>
  prepareExportTargets?: (html: string) => Promise<void>
  resolveWordLinkTarget?: (target: WordLinkTarget) => WordLinkTargetContent | undefined
  /**
   * Resolves once these lines have their text. The windowed scroller asks for a
   * range's lines before mounting it, so a line is never on screen without its text.
   */
  ensureLineContents: (lineIds: number[]) => Promise<void>
  /**
   * Releases the text of lines far from the window, keeping the ones `keep` names
   * (see useCommentary's bounded text cache). Called after the window moves.
   */
  trimLineContents?: (panel: CommentarySlot, keep: ((lineId: number) => boolean) | null) => void
  createNote: (lineId: number, startOffset: number, endOffset: number, quote: string) => Promise<Note>
  updateNote: (note: Note, newText: string) => Promise<void>
  deleteNote: (note: Note) => Promise<void>
  commentaryFontPx: number
  renderContent: (content: string, flatIndex: number, lineId: number | undefined, searchQuery: string | undefined) => string
  /**
   * How many annotation offsets a line's model content spans. Comes from the renderer
   * because the offset space is the text after its diacritics and censor transforms.
   */
  annotationOffsetLengthOf: (content: string) => number
  setCurrentMark: (scroller: HTMLElement, flatIndex: number, occurrence: number) => void
  commentaryTocPaths: Map<string, string>
  searchQuery?: string
  currentMatchFlatIndex?: number
  currentMatchOccurrence?: number
  pinnedGroup?: PinnedCommentaryGroup | null
  filterVisible?: boolean
  // True when a saved scroll position exists for this panel — the restore path
  // then owns first positioning and the first-load pin scroll must not fire.
  hasSavedScrollPos?: boolean
}>()
const emit = defineEmits<{
  close: []
  'navigate-section': [direction: 'next' | 'prev', bookId: number]
  'open-book': [bookId: number, lineIndex: number]
  'toggle-filter-panel': []
  'toggle-search': []
  scroll: [scrollIndex: number, scrollOffset: number]
}>()

// ── Note bubble state ─────────────────────────────────────────────────────────

const activeBubbleNote = ref<Note | null>(null)
const activeBubbleAnchorRect = ref<DOMRect | null>(null)

function openNoteBubble(note: Note, markerEl: HTMLElement) {
  activeBubbleNote.value = note
  activeBubbleAnchorRect.value = markerEl.getBoundingClientRect()
}

function closeNoteBubble() {
  activeBubbleNote.value = null
  activeBubbleAnchorRect.value = null
}

// ── Flat item list ────────────────────────────────────────────────────────────

type FlatItem =
  | {
      type: 'header'
      bookId: number
      bookTitle: string
      connectionTypes: string[]
      sectionLabel?: string
      subSectionLabel?: string
      firstLineIndex?: number
      tocPath?: string
    }
  // The line object itself, not a copy of its text: the list is STRUCTURAL and is
  // rebuilt only when the groups or the TOC paths change. Text arriving in (or
  // released from) a line re-renders that row alone, not the whole list.
  | { type: 'line'; line: CommentaryLine }

// The windowed scroller mounts only the rows around the viewport, fetched before
// they are mounted. Its viewport is the scroll element every listener below uses;
// it exists only while the list does (the v-if chain in the template).
const windowedScroller = ref<InstanceType<typeof WindowedScroller> | null>(null)
const scroller = (): WindowedScrollerApi | null => windowedScroller.value
const scrollerEl = computed<HTMLElement | null>(() => windowedScroller.value?.scrollerElement ?? null)
const headerNavRef = ref<InstanceType<typeof CommentaryHeaderNav> | null>(null)

// Zoom handler scoped to this scroller — Ctrl+scroll and pinch affect only the
// commentary panel. Keyboard Ctrl+±/0 fires on whichever element has focus, so it
// also stays scoped to the commentary panel when the scroller is focused.
const _tabStore = useTabStore()
const _bookViewStore = useBookViewStore()
const _paneNavigation = usePaneNavigation()
const _tabId = _paneNavigation.activeTabId
const _bookId = _paneNavigation.activeTab.bookId!
const _commentaryZoom = computed({
  get: () => _bookViewStore.getCommentaryZoom(_tabId, _bookId, props.slotName),
  set: (value: number) => _bookViewStore.setCommentaryZoom(_tabId, _bookId, props.slotName, value),
})
useZoomHandler({ zoom: _commentaryZoom, target: scrollerEl, keyboard: true })

const visibleGroups = computed(() => props.groups)

const flatItems = computed<FlatItem[]>(() => {
  const tocPaths = props.commentaryTocPaths
  const items: FlatItem[] = []
  for (const g of visibleGroups.value) {
    items.push({
      type: 'header',
      bookId: g.bookId,
      bookTitle: g.bookTitle,
      connectionTypes: g.connectionTypes,
      sectionLabel: g.sectionLabel,
      subSectionLabel: g.subSectionLabel,
      firstLineIndex: g.lines[0]?.lineIndex,
      tocPath: tocPaths.get(commentaryGroupKey(g)),
    })
    for (const l of g.lines) items.push({ type: 'line', line: l })
  }
  return items
})

/** Row index of every group header, ascending — the bar's landmarks and the label's lookup. */
const headerIndexes = computed(() => {
  const indexes: number[] = []
  flatItems.value.forEach((item, index) => { if (item.type === 'header') indexes.push(index) })
  return indexes
})
const headerLandmarkTiers = computed(() => [headerIndexes.value])

/** The group a row belongs to: the last header at or above it. */
function headerAt(index: number): Extract<FlatItem, { type: 'header' }> | null {
  const headers = headerIndexes.value
  let low = 0
  let high = headers.length - 1
  let found = -1
  while (low <= high) {
    const mid = (low + high) >> 1
    if (headers[mid]! <= index) { found = mid; low = mid + 1 } else high = mid - 1
  }
  const item = found < 0 ? null : flatItems.value[headers[found]!]
  return item?.type === 'header' ? item : null
}

// ── Scroller ──────────────────────────────────────────────────────────────────

const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, {
  onCtrlF: () => emit('toggle-search'),
  // Ctrl+C is intercepted so it takes the same path as the menu's copy: the export
  // warm-up has to be awaited before the clipboard event fires (see prepareAndCopy).
  onCtrlC: () => onCommentaryCopy(),
  onCtrlV: () => triggerCopy(() => pasteIntoWord().catch(() => {})),
  onCtrlShiftC: () => onCommentarySearchInRepository(),
})

const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)

// The scrollbar is the scroller's own element beside its viewport, so a press on it
// never reaches the viewport's listeners: nothing to filter out here.
useContextMenuLongPress(scrollerEl, (event) => {
  contextMenuRef.value?.showAtPosition(event.clientX, event.clientY)
})

// Keeps the rows holding a selection's two ends mounted however far they scroll
// away, so a drag past the viewport edge cannot break the selection.
const {
  pinnedRowIndexes,
  selectedRowIndexRange,
  restoreSelectionFromPinnedRows,
} = useVirtualizedTextSelection(
  scrollerEl,
  // Which groups are shown, not just how many rows they add up to: a filter change
  // that swaps one group for another of equal length leaves the count untouched while
  // every row index comes to mean a different line.
  () => visibleGroups.value.map(commentaryGroupKey).join('|'),
)

/** Ids of the real lines (not headers, not the no-text rows) among flat items [start, end). */
function lineIdsIn(start: number, end: number): number[] {
  const ids: number[] = []
  const items = flatItems.value
  for (let index = Math.max(0, start); index < Math.min(end, items.length); index++) {
    const item = items[index]
    if (item?.type === 'line' && item.line.lineId > 0) ids.push(item.line.lineId)
  }
  return ids
}

/** The scroller's row source: the text of a range's lines, fetched before the range mounts. */
function ensureRows(start: number, end: number): Promise<void> {
  return props.ensureLineContents(lineIdsIn(start, end))
}

/** Every real line id the panel shows, in reading order: the select-all copy scope. */
function allLineIds(): number[] {
  return lineIdsIn(0, flatItems.value.length)
}

// After the window moves, text far from it is released — never a mounted or pinned
// row's. The goal a landing is fetching for is covered by the cache's own rule (its
// newest batches are never released).
watch(
  () => [windowedScroller.value?.renderedRange, pinnedRowIndexes.value] as const,
  ([range, pinned]) => {
    if (!range || !props.trimLineContents) return
    const keep = new Set(lineIdsIn(range.start, range.end))
    const items = flatItems.value
    for (const index of pinned) {
      const item = items[index]
      if (item?.type === 'line') keep.add(item.line.lineId)
    }
    props.trimLineContents(props.slotName, (lineId) => keep.has(lineId))
  },
  { flush: 'post' },
)
onBeforeUnmount(() => props.trimLineContents?.(props.slotName, null))

// ── Notes and word links — loaded for the mounted rows ────────────────────────
// The data layers live in useBookView (hoisted). Only the rows the scroller has
// mounted ask for theirs.

watch(
  () => windowedScroller.value?.renderedRange,
  (range) => {
    if (!range) return
    const ids = lineIdsIn(range.start, range.end)
    props.scheduleNotesLoad(ids)
    props.scheduleWordLinkAnchorsLoad?.(ids)
  },
  { immediate: true },
)

// ── Rendering ─────────────────────────────────────────────────────────────────

// Apply .current class via DOM toggle — no re-render needed when only the
// active occurrence changes within an already-rendered commentary line.
watch(
  () => [props.currentMatchFlatIndex, props.currentMatchOccurrence] as const,
  ([flatIndex, occurrence]) => {
    if (!scrollerEl.value) return
    nextTick(() => {
      if (!scrollerEl.value) return
      props.setCurrentMark(scrollerEl.value, flatIndex ?? -1, occurrence ?? 0)
    })
  },
)

// ── Scroll ────────────────────────────────────────────────────────────────────

const {
  activeHeader,
  activePinnedGroup,
  activePinnedGroupForCapture,
  consumeExplicitPick,
  cancelPositioning,
  markUserAdjusted,
  isPositioning,
  openingTarget,
  scrollerOpened,
  onScroll: handleScroll,
  scrollToGroup,
  scrollToFlatIndex,
  captureScrollPos,
  restoreCommentaryScrollPos,
  claimRestoreIntent,
  topVisibleFlatIndex,
  setupGroupReloadScroll,
} = useCommentaryScroll(
  () => flatItems.value,
  () => visibleGroups.value,
  () => headerIndexes.value,
  scroller,
  props.slotName,
)

setupGroupReloadScroll(
  () => props.groups,
  () => props.pinnedGroup,
  () => props.loading,
  () => props.hasSavedScrollPos ?? false,
  // The anchor an owed pin scroll belongs to: one line tap reloads the list twice,
  // and only a genuinely different line makes an owed scroll stale.
  () => props.selectedLineId,
)

// ── Context menu ──────────────────────────────────────────────────────────────

// Whole-document copy source for "בחר הכל" → copy. Built from the model (every
// visible group's lines), NOT the DOM, so a copy-all is not truncated to the
// mounted window's lines. Each line is a <div class="line" data-line-id> block with
// note markers rendered in; the copy builder strips markers when notes are off and
// flattens blocks when copyJoinLines is on.
function buildAllCommentaryContentHtml(): string {
  const parts: string[] = []
  // flatIndex must track the real flatItems indexing (one header entry per group,
  // then one entry per line) so renderContent's per-flatIndex cache is reused rather
  // than fought — both iterate visibleGroups in the same order.
  let flatIndex = 0
  for (const g of visibleGroups.value) {
    // Header line so the copied text keeps its per-commentary structure.
    parts.push(`<div class="line">${g.bookTitle}</div>`)
    flatIndex++
    for (const l of g.lines) {
      const rendered = props.renderContent(l.content, flatIndex, l.lineId, props.searchQuery)
      parts.push(`<div class="line" data-line-id="${l.lineId}">${rendered}</div>`)
      flatIndex++
    }
  }
  return parts.join('')
}

/**
 * The same rendered markup as buildAllCommentaryContentHtml, but only for the flat
 * items in [firstIndex, lastIndex] — the rows a selection spans. Lets a copy include
 * the selected rows that scrolled out of the virtualized window (and so are not in
 * the DOM to be serialized), headers included, in their true reading order.
 */
function buildCommentaryContentHtmlForRange(firstIndex: number, lastIndex: number): string {
  const parts: string[] = []
  let flatIndex = 0
  for (const g of visibleGroups.value) {
    if (flatIndex >= firstIndex && flatIndex <= lastIndex) {
      parts.push(`<div class="line">${g.bookTitle}</div>`)
    }
    flatIndex++
    for (const l of g.lines) {
      if (flatIndex > lastIndex) return parts.join('')
      if (flatIndex >= firstIndex) {
        const rendered = props.renderContent(l.content, flatIndex, l.lineId, props.searchQuery)
        parts.push(`<div class="line" data-line-id="${l.lineId}">${rendered}</div>`)
      }
      flatIndex++
    }
  }
  return parts.join('')
}

const { contextMenuItems, buildFormattedHtml: buildCommentaryFormattedHtml, onCopy: onCommentaryCopy, onPasteIntoWord: onCommentaryPasteIntoWord, onSearchInRepository: onCommentarySearchInRepository } = useCommentaryCopy(
  () => {
    const pinned = activePinnedGroup.value
    return pinned
      ? visibleGroups.value.find(
          (g) =>
            g.bookId === pinned.bookId &&
            (g.sectionLabel ?? '') === pinned.sectionLabel &&
            (g.subSectionLabel ?? '') === pinned.subSectionLabel,
        ) ?? null
      : null
  },
  (group) => props.commentaryTocPaths.get(commentaryGroupKey(group)),
  selectAllInContainer,
  scrollerEl,
  (lineId, startOffset, endOffset, colorArgb) =>
    props.applyHighlight(lineId, startOffset, endOffset, colorArgb),
  (lineId, startOffset, endOffset) => props.clearHighlight(lineId, startOffset, endOffset),
  // Returns the write's promise so the selection restore runs after the note has
  // landed and re-rendered its line, not before.
  (lineId, startOffset, endOffset, quote) =>
    props.createNote(lineId, startOffset, endOffset, quote).then((note) => {
      void nextTick(() => {
        const marker = scrollerEl.value?.querySelector(
          `[data-note-id="${note.id}"]`,
        ) as HTMLElement | null
        if (marker) openNoteBubble(note, marker)
      })
    }),
  props.getNotesForLine,
  isSelectAll,
  buildAllCommentaryContentHtml,
  props.prepareExportData && props.prepareExportTargets && props.resolveWordLinkTarget
    ? {
        getAllLineIds: allLineIds,
        prepareForLines: props.prepareExportData,
        prepareForRenderedHtml: props.prepareExportTargets,
        resolveWordLinkTarget: props.resolveWordLinkTarget,
      }
    : undefined,
  {
    selectedRowIndexRange,
    buildContentHtmlForRange: buildCommentaryContentHtmlForRange,
    restoreSelectionFromPinnedRows,
    lineAtRowIndex: (rowIndex) => {
      const item = flatItems.value[rowIndex]
      if (!item || item.type !== 'line') return null
      return { lineId: item.line.lineId, offsetLength: props.annotationOffsetLengthOf(item.line.content) }
    },
    // The rows a selection spans beyond the window, and the whole document for a
    // select-all, are built from the model — which only holds the text the scroller
    // has fetched. Fetch the rest before the copy reads it.
    ensureContentsForRange: (firstIndex, lastIndex) => props.ensureLineContents(lineIdsIn(firstIndex, lastIndex + 1)),
    ensureAllContents: () => props.ensureLineContents(allLineIds()),
  },
  () => openCommentaryErrorReport(),
)
// A commentary line belongs to the commentary book its group came from, never to the
// base book this pane is attached to — so the report's identity is looked up from the
// groups rather than taken from the surrounding view.
const {
  target: commentaryErrorReportTarget,
  openErrorReport: openCommentaryErrorReport,
  closeErrorReport: closeCommentaryErrorReport,
  awaitSourceResolution: awaitCommentaryErrorReportSource,
} = useCommentaryErrorReport({
  scrollerEl,
  findLine: (lineId) => {
    for (const group of visibleGroups.value) {
      const line = group.lines.find((l) => l.lineId === lineId)
      if (line) {
        return {
          bookId: group.bookId,
          bookTitle: group.bookTitle,
          // The group's own path already reads "book · section · category"; the copy
          // menu flattens that separator for a citation, so the report does too.
          reference: (group.path ?? group.bookTitle).split(' · ').join(' '),
          line: { lineId: line.lineId, lineIndex: line.lineIndex, content: line.content },
        }
      }
    }
    return null
  },
})

useScopedCopy(
  scrollerEl,
  () => visibleGroups.value.flatMap((g) => g.lines.map((l) => l.content)),
  isSelectAll,
  buildCommentaryFormattedHtml,
)

function onScroll() {
  refreshScrollbarTocTooltip()
  handleScroll((scrollIndex, scrollOffset) => {
    emit('scroll', scrollIndex, scrollOffset)
  })
}

function onMarkerClick(event: MouseEvent) {
  const marker = (event.target as HTMLElement).closest('[data-note-id]') as HTMLElement | null
  if (!marker) return
  const noteId = parseInt(marker.dataset['noteId'] ?? '', 10)
  if (isNaN(noteId)) return
  event.stopPropagation()
  const lineId = parseInt(
    (marker.closest('[data-line-id]') as HTMLElement | null)?.dataset['lineId'] ?? '',
    10,
  )
  const found = props.getNotesForLine(lineId).find((n) => n.id === noteId)
  if (found) openNoteBubble(found, marker)
}

const activeBookId = computed(() => activePinnedGroup.value?.bookId ?? null)

defineExpose({
  scrollToGroup,
  scrollToFlatIndex,
  topVisibleFlatIndex,
  activePinnedGroup,
  activePinnedGroupForCapture,
  consumeExplicitPick,
  cancelPositioning,
  activeBookId,
  captureScrollPos,
  restoreCommentaryScrollPos,
  claimRestoreIntent,
  getFilterButtonEl: () => headerNavRef.value?.filterBtnRef ?? null,
})

const activeTocPath = computed(() =>
  activePinnedGroup.value
    ? props.commentaryTocPaths.get(commentaryGroupKey(activePinnedGroup.value))
    : undefined,
)

// Delayed loading spinner — the panel's original LoadingAnimation, mounted only
// after loading has been in flight for a while. Fast (warm-cache) loads finish
// inside the delay and never render or flash it, so it costs nothing on the
// common path; only genuinely slow loads show the animation.
const LOADING_SPINNER_DELAY_MS = 300
const showLoadingSpinner = ref(false)
const spinnerDelay = useTimeoutFn(
  () => { showLoadingSpinner.value = true },
  LOADING_SPINNER_DELAY_MS,
  { immediate: false },
)
watch(() => props.loading || isPositioning.value, (busy) => {
  spinnerDelay.stop()
  showLoadingSpinner.value = false
  if (busy) spinnerDelay.start()
}, { immediate: true })

// ── Word-level links (hover preview + click-through) ─────────────────────────

const booksDataStore = useBooksDataStore()
const {
  wordLinkTooltip,
  closeWordLinkTooltip,
  keepOpen: keepWordLinkTooltipOpen,
  releaseOpen: releaseWordLinkTooltip,
  beginSelection: beginWordLinkTooltipSelection,
} = useWordLinkTooltip(scrollerEl, {
  getBookTitle: (targetBookId) => booksDataStore.allBooksMap.get(targetBookId)?.title ?? '',
  onNavigate: (target) => emit('open-book', target.bookId, target.lineIndex),
})

// User-note hover preview — the marker carries its text in data-note-text, so this
// replaces the native title tooltip the marker used to have.
const { noteTooltip, closeNoteTooltip } = useNoteTooltip(scrollerEl)

const { abbrevTooltip } = useBookViewAbbrevTooltip(scrollerEl)

// Hovering the scrollbar names the commentary and section there, as the lines view
// does: on the thumb where the reader is, elsewhere where a drag there would land.
const scrollbarElement = computed(() => windowedScroller.value?.scrollbarElement ?? null)
const { scrollbarTocTooltip, refreshScrollbarTocTooltip } = useBookViewScrollbarTocTooltip(scrollbarElement, {
  getCurrentLineIndex: () => null,
  getTargetLineIndex: (clientY) => scroller()?.barTargetAt(clientY) ?? null,
  viewportElement: scrollerEl,
  getBookTitle: () => '',
  getLabelAt: (index) => {
    const header = headerAt(index)
    if (!header) return { bookTitle: '', path: '' }
    const path = header.tocPath || [header.sectionLabel, header.subSectionLabel].filter(Boolean).join(' ')
    return { bookTitle: header.bookTitle, path }
  },
})
</script>

<template>
  <div class="commentary-view" :data-commentary-slot="slotName">
    <!--
      Nothing may sit between <template> and this root div - a comment there
      fragments the root and points $el at the comment node, silently breaking
      every $el.contains(focus) consumer (that regression double-zoomed the
      panels once). data-commentary-slot itself exists so Ctrl+F and text
      selection can tell the two panels apart; both carry .commentary-view.
    -->
    <ContextMenu ref="contextMenuRef" :items="contextMenuItems" />
    <BookViewErrorReportDialog
      v-if="commentaryErrorReportTarget"
      :selected-text="commentaryErrorReportTarget.selectedText"
      :original-line="commentaryErrorReportTarget.originalLine"
      :selection-offset="commentaryErrorReportTarget.selectionOffset"
      :book-title="commentaryErrorReportTarget.bookTitle"
      :current-ref="commentaryErrorReportTarget.reference"
      :line-index="commentaryErrorReportTarget.lineIndex"
      :book-id="commentaryErrorReportTarget.bookId"
      :source="commentaryErrorReportTarget.source"
      :await-source="awaitCommentaryErrorReportSource"
      @close="closeCommentaryErrorReport"
    />
    <BookViewNoteBubble
      v-if="activeBubbleNote && activeBubbleAnchorRect"
      :note="activeBubbleNote"
      :anchor-rect="activeBubbleAnchorRect"
      :update-note="props.updateNote"
      :delete-note="props.deleteNote"
      @close="closeNoteBubble"
      @deleted="closeNoteBubble"
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
    <BookViewAbbrevTooltip
      v-if="abbrevTooltip"
      :key="abbrevTooltip.id"
      :data="abbrevTooltip"
    />
    <BookViewScrollbarTocTooltip v-if="scrollbarTocTooltip" :data="scrollbarTocTooltip" />
    <div class="body">
      <div class="content-col" :style="{ fontSize: `${commentaryFontPx}px` }">
        <CommentaryHeaderNav
          ref="headerNavRef"
          class="sticky-nav"
          :groups="visibleGroups"
          :scroll-to-group="scrollToGroup"
          :active-pinned-group="activePinnedGroup"
          :filter-visible="props.filterVisible"
          :active-toc-path="activeTocPath"
          @navigate-section="(d, id) => emit('navigate-section', d, id)"
          @toggle-filter="emit('toggle-filter-panel')"
          @toggle-search="emit('toggle-search')"
          @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)"
          @close="emit('close')"
        />
        <div v-if="props.loading" class="state-overlay">
          <LoadingAnimation v-if="showLoadingSpinner" />
        </div>
        <div v-else-if="!flatItems.length" class="state-overlay">
          <div class="hint-container">
            <div class="hint-title">{{
              props.loadError
                ? $t('bookView.commentary.loadFailed')
                : props.selectedLineId == null ? $t('bookView.commentary.pickLine') : $t('bookView.commentary.noneForLine')
            }}</div>
            <div v-if="props.selectedLineId == null" class="hint-instructions">
              <div class="hint-row">{{ $t('bookView.commentary.hintClick') }}</div>
              <div class="hint-row">{{ $t('bookView.commentary.hintRange') }}</div>
              <div class="hint-row">{{ $t('bookView.commentary.hintHeading') }}</div>
            </div>
          </div>
        </div>
        <div v-else class="scroller-host" dir="rtl">
          <WindowedScroller
            ref="windowedScroller"
            class="scroller"
            :count="flatItems.length"
            :initial-index="openingTarget?.index"
            :initial-offset-px="openingTarget?.offsetPx"
            :ensure-rows="ensureRows"
            :pinned-indexes="pinnedRowIndexes"
            :landmark-tiers="headerLandmarkTiers"
            tabindex="0"
            data-ctrlf-enabled
            @opened="scrollerOpened"
            @scroll="onScroll"
            @click="onMarkerClick"
            @contextmenu="contextMenuRef?.show($event)"
            @wheel.passive="markUserAdjusted"
            @touchstart.passive="markUserAdjusted"
            @pointerdown.passive="markUserAdjusted"
            @keydown="markUserAdjusted"
          >
            <template #row="{ index }">
              <CommentaryHeader
                v-if="flatItems[index]?.type === 'header'"
                :book-id="(flatItems[index] as any).bookId"
                :book-title="(flatItems[index] as any).bookTitle"
                :first-line-index="(flatItems[index] as any).firstLineIndex"
                :section-label="(flatItems[index] as any).sectionLabel"
                :sub-section-label="(flatItems[index] as any).subSectionLabel"
                :own-toc-path="(flatItems[index] as any).tocPath"
                @navigate-section="(d, id) => emit('navigate-section', d, id)"
                @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)"
              />
              <!-- Never a placeholder: the scroller fetched these lines' text before mounting them. -->
              <div
                v-else-if="flatItems[index]"
                class="line"
                :class="{ 'line-no-text': (flatItems[index] as any).line.lineId === -1 }"
                :data-line-id="(flatItems[index] as any).line.lineId"
                :style="teamimFontStyle((flatItems[index] as any).line.content)"
                v-html="renderContent((flatItems[index] as any).line.content, index, (flatItems[index] as any).line.lineId, props.searchQuery)"
              />
            </template>
          </WindowedScroller>
        </div>
        <!--
          Positioning mask. While a goal (a restore, a pinned-group scroll) waits for
          its target - its group still arriving, its rows being fetched - the panel
          would otherwise show whatever it opened on. Cover it until the goal lands.
          Deliberately OUTSIDE the v-if chain above, as an absolute overlay: putting
          it in the chain would unmount the scroller, and then there is nothing left
          to position. The spinner keeps its usual delay so fast loads never flash it.
        -->
        <div v-if="!props.loading && isPositioning && flatItems.length" class="positioning-mask">
          <LoadingAnimation v-if="showLoadingSpinner" />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.commentary-view {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  /* Named container so the reading column widens its side padding based on the
     commentary panel's OWN width, not the whole pane (it's usually a narrower
     side/split panel). */
  container: commentary-view / inline-size;
}
.body {
  flex: 1;
  display: flex;
  flex-direction: row;
  min-height: 0;
  position: relative;
  /* Side padding for everything in the reading column — headers and lines share
     the ONE value so their start edges line up. Custom properties inherit past
     scoped-style boundaries, so CommentaryHeader picks this up too. Declared on
     .body (not .commentary-view) because an element never matches a container
     query on its OWN container. */
  --commentary-pad-inline: 12px;
}
/* On a wide commentary panel give the text a bit more breathing room from the
   edges (רווח נוסף מהצדדים במסך רחב), matching the lines view. */
@container commentary-view (min-width: 600px) {
  .body {
    --commentary-pad-inline: 22px;
  }
}
.content-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.sticky-nav {
  flex-shrink: 0;
  height: 32px;
  font-size: 13px;
}
.state-overlay {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}
/* Covers the scroller (not the nav) while the positioner works. Opaque: its whole
   job is to hide the content jumping underneath. Absolute against .body, which is
   position:relative. */
.positioning-mask {
  position: absolute;
  top: 32px;
  inset-inline-end: 0;
  inset-inline-start: 0;
  bottom: 0;
  z-index: 5;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--bg-primary);
  /* Events pass through to the scroller so the reader can still interrupt: the
     positioner cancels its goal on wheel/pointer/key, which drops this mask the
     same frame. With events blocked here, positioning was uninterruptible. */
  pointer-events: none;
}
.hint {
  font-size: 13px;
  color: var(--text-secondary);
}
.hint-container {
  display: flex;
  flex-direction: column;
  gap: 14px;
  align-items: center;
  padding: 0 24px;
  max-width: 340px;
}
.hint-title {
  font-size: 13px;
  color: var(--text-secondary);
  font-weight: 500;
  text-align: center;
}
.hint-instructions {
  display: flex;
  flex-direction: column;
  gap: 0;
  width: 100%;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  overflow: hidden;
}
.hint-row {
  display: flex;
  align-items: center;
  padding: 8px 12px;
  font-size: 11.5px;
  color: var(--text-secondary);
  line-height: 1.4;
  border-block-end: 1px solid var(--border-color);
}
.hint-row:last-child {
  border-block-end: none;
}
/* The scroller's root fills this; its viewport (`.scroller`, attributes fall
   through) scrolls, with the drawn scrollbar beside it. */
.scroller-host {
  flex: 1;
  min-height: 0;
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
/* Exact line spacing — see the long note in BookViewLinesContent.vue, including
   why the attribute is matched via `html[...]` and never `:global()`. Same opt-in
   attribute drives the commentary column, so the two panes stay in step. */
html[data-fixed-line-height='true'] .line {
  line-height: calc(var(--commentary-line-height, 1.7) * 1em);
}
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
/* The commentary column renders the same superscript markers as the lines view,
   so they need the same zero-height box here. */
html[data-fixed-line-height='true'] .line :deep(.user-note-marker),
html[data-fixed-line-height='true'] .line :deep(.word-link-marker) {
  line-height: 1;
}
/* Headings stay heavier than the body at every slider stop — see the matching rule
   in BookViewLinesContent.vue for why this is 800 and not bold. */
.line :deep(h1),
.line :deep(h2),
.line :deep(h3),
.line :deep(h4),
.line :deep(h5),
.line :deep(h6) {
  font-family: var(--commentary-header-font);
  font-weight: 800;
}
.line-no-text {
  color: var(--text-secondary);
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
