<script setup lang="ts">
import { computed, ref, watch, nextTick, onMounted, inject } from 'vue'
import { storeToRefs } from 'pinia'
import { cleanTextFlagsFor } from '@/utils/hebrewTextProcessing'
import WindowedScroller from '@/components/WindowedScroller.vue'
import type { WindowedScrollerApi, WindowedScrollerRowKind } from '@/components/windowedScrollerTypes'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import type { LineItem, LinesTable } from './linesTable'
import type { TocEntry } from '@/webview-host/queries.types'
import { lineIsWhollyBold } from './lineIsWhollyBold'
import { altTocBreakSources, isAltTocLabelVisible, type AltTocLineLabels } from '../toc/altTocTextLabels'
import type { BookViewLinesScrollProps } from './useBookViewLinesScroll'
import ContextMenu from '@/components/ContextMenu.vue'
import { useContextMenuLongPress, hasActiveTextSelection } from '@/composables/useContextMenuLongPress'
import { useScopedKeys } from '@/composables/useTextSelectionKeys'
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
import { useBookViewLinesBookmarkPoint } from './useBookViewLinesBookmarkPoint'
import BookViewNoteBubble from './BookViewNoteBubble.vue'
import BookViewAbbrevTooltip from './BookViewAbbrevTooltip.vue'
import { useBookViewAbbrevTooltip } from './useBookViewAbbrevTooltip'
import WordLinkTooltip from './WordLinkTooltip.vue'
import { useWordLinkTooltip } from './useWordLinkTooltip'
import { useLineNotes, useLineNotesTooltip } from './useLineNotes'
import { markerForNote } from './lineNotes'
import { useNoteTooltip } from './useNoteTooltip'
import BookViewScrollbarTocTooltip from './BookViewScrollbarTocTooltip.vue'
import { useBookViewScrollbarTocTooltip } from './useBookViewScrollbarTocTooltip'
import { useWordLinkAnchors } from './useWordLinkAnchors'
import { createLineDecorationsBatch } from './lineDecorationsBatch'
import { useCopyExportData } from './useCopyExportData'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { isExportRestricted } from '../exportRestriction'
import { exportToWord, pasteIntoWord } from '@/webview-host/bridge'
import { showToast } from '@/composables/useToast'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const emit = defineEmits<{
  scrolled: [firstVisible: number, firstFull: number, isUserScroll: boolean]
  /** The lines scroller stopped moving (WindowedScroller's moveend). */
  moveend: []
  lineSelected: [lineId: number, isShiftClick: boolean]
  'ctrl-f': []
  createBookmark: []
}>()
// The scroll-related props come from useBookViewLinesScroll's own interface —
// never re-declare them here. The hand-copied list diverged once: all of those
// props are optional, so a renamed prop compiled clean while Vue silently
// dropped the page's binding into $attrs and saves went out empty.
const props = defineProps<
  BookViewLinesScrollProps & {
    lines: LinesTable
    /**
     * Bumped on every write to `lines`, which is a shallow store: the row slot reads it
     * so the mounted rows re-render when their text changes in place (a version swap).
     */
    linesRevision?: number
    /** Resolves once the lines of [start, end) have their text — the scroller's row source. */
    ensureLines: (start: number, end: number) => Promise<void>
    /** Reads the lines of [start, end) for a copy or export, without keeping them. */
    readLines: (start: number, end: number) => Promise<LineItem[]>
    altTocLabelMap?: Map<number, AltTocLineLabels>
    searchQuery?: string
    /** The lines the in-book search results hold: only these are marked with `searchQuery`. */
    searchMatchLines?: { has(lineIndex: number): boolean }
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
    /** The library marks this book a commentary: clean text accepts plain dibbur hamatchil headings. */
    bookIsCommentary?: boolean
    /**
     * Every line's character count by lineIndex, from the library — the scrollbar's
     * static per-row height estimate. null while loading, or for a library without
     * the column; the bar then estimates from the rows it measures.
     */
    lineCharacterCounts?: ArrayLike<number> | null
    /** Pop this note of the opening line open on arrival (a search hit on a footnote, issue #13). */
    openNoteIndex?: number
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

const cleanTextFlags = computed(() => cleanTextFlagsFor(settingsStore.cleanTextFlags, !!props.bookIsCommentary))
const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)
const linesLayout = computed(() => settingsStore.linesLayout)

/**
 * Block-level markup a line may carry. Such a line cannot run on inside a
 * paragraph — an empty line is a paragraph break, a centred or tabular line a block
 * of its own — so flow mode gives it a row to itself (measured 2026-11-03: under
 * 0.3% of lines; headings are the TOC's and already blocks).
 */
const BLOCK_MARKUP =
  /<(?:h[1-6]|p|div|center|table|ol|ul|li|dl|dt|dd|blockquote|details|summary|img|hr|pre|section|article|figure|header|footer|nav|aside|form|fieldset|address)\b|display\s*:\s*(?:block|flex|grid|table)/i

/**
 * Flow mode: whether a mounted line stands as a block or flows on with its
 * neighbours. Reads the lines revision, so a text swap re-groups the rows.
 */
function rowKind(index: number): WindowedScrollerRowKind {
  void props.linesRevision
  const content = props.lines.contentAt(index)
  if (content == null || content.trim() === '' || BLOCK_MARKUP.test(content) || lineIsWhollyBold(content)) return 'block'
  return 'inline'
}

/**
 * This line's alt-TOC labels of one type (see toc/altTocTextLabels), joined, minus any
 * label the text already shows at the start of its line or the one before or after.
 * Break labels may come from a line or two below, climbing over the headings between.
 */
function shownAltTocLabels(index: number, type: keyof AltTocLineLabels): string | undefined {
  void props.linesRevision
  const map = props.altTocLabelMap
  if (!map?.size) return undefined
  const contentAt = (i: number) => (i >= 0 ? props.lines.contentAt(i) : null)
  const sources = type === 'breaks' ? altTocBreakSources(index, contentAt) : [index]
  const shown: string[] = []
  for (const source of sources) {
    const labels = map.get(source)?.[type]
    if (!labels?.length) continue
    const near = [source, source - 1, source + 1, index].map(contentAt)
    for (const label of labels) if (!isAltTocLabelVisible(label, near) && !shown.includes(label)) shown.push(label)
  }
  return shown.length ? shown.join(' · ') : undefined
}

/**
 * A flowing line's inline alt-TOC label, its own spaces made fixed-width (four-per-em,
 * about a plain space): the gaps around an inline label are word-spacing on plain
 * spaces, which must not widen the label's own words — see the `.line-inline` label styles.
 */
function inlineAltTocLabel(index: number): string | undefined {
  return shownAltTocLabels(index, 'inlines')?.replace(/ /g, ' ')
}

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
    const id = props.lines.idAt(index)
    if (id > 0) ids.push(id)
  }
  return ids
}
useZoomHandler({ zoom, target: scrollerEl, keyboard: true })
const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, {
  canSelectAll: () => !exportRestricted.value,
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

const { onScroll, onScrollerOpened, captureScrollPos, readCurrentPosition, cancelOpeningFollowUps } =
  useBookViewLinesScroll(
  scroller,
  props,
  { tabStore, bookViewStore, autoSelectTopLine, zoom, tabId, bookId },
  (event, firstVisible, firstFull, isUserScroll) =>
    emit(event, firstVisible, firstFull, isUserScroll),
)

// Word-level link anchors (link_anchor, schema v2+ DBs) — lazy, viewport-driven,
// a no-op on DBs without the table.
// Anchors and inline notes of a window come in ONE service call (lineDecorationsBatch).
const lineDecorations = createLineDecorationsBatch()
const { getWordLinkAnchorsForLine, loadWordLinkAnchorsForLines, evictWordLinkAnchorsForLines } = useWordLinkAnchors(renderedLineIds, lineDecorations.anchorsFor)
// Inline notes (line_note, Otzaria schema 7+) - lazy, viewport-driven, a no-op on DBs without the table.
const { getLineNotesForLine, evictLineNotesForLines } = useLineNotes(renderedLineIds, lineDecorations.notesFor)

const { lineContent, forgetLines: forgetRenderedLines } = useBookViewLineRenderer(settingsStore, cleanTextFlags, () => ({
  activeVersionId: props.activeVersionId,
  searchQuery: props.searchQuery,
  searchMatchLines: props.searchMatchLines,
  searchHighlightLineIndex: props.searchHighlightLineIndex,
  searchHighlightQuery: props.searchHighlightQuery,
  searchHighlightSnippet: props.searchHighlightSnippet,
  searchHighlightTerms: props.searchHighlightTerms,
  getHighlightsForLine,
  getNotesForLine,
  getWordLinkAnchorsForLine,
  getLineNotesForLine,
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
// Publisher-restricted books (the toolbar hides export to Word for them) get no
// select-all either: a whole-book copy is the same export by another route.
const exportRestricted = computed(() =>
  isExportRestricted(booksDataStore.allBooksMap.get(bookId)?.title || bookTitle),
)
// What copy-with-notes needs beyond the rendered markup: the notes and citations of
// lines that were never scrolled into view, plus the target lines those citations
// point at (they become endnotes). Warmed by the copy actions, never on render.
const { prepareForLines, prepareForRenderedHtml, resolveWordLinkTarget } = useCopyExportData({
  loadNotes: loadNotesForLines,
  loadWordLinkAnchors: loadWordLinkAnchorsForLines,
  getWordLinkAnchorsForLine,
  getBookTitle: (targetBookId) => booksDataStore.allBooksMap.get(targetBookId)?.title ?? '',
})
const { items: contextMenuItems, buildFormattedHtml, wholeBookReady, onCopy, onPasteIntoWord, onSearchInRepository, buildSource } = useBookViewLineCopyMenu({
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
  cleanTextFlags: () => cleanTextFlags.value,
  onHighlight,
  onClearHighlight,
  onAddNote,
  onCopyLineLink: copyLineLink,
  onReportError: openErrorReport,
  onCreateBookmark: () => emit('createBookmark'),
  prepareForLines,
  prepareForRenderedHtml,
  resolveWordLinkTarget,
  readLines: (start, end) => props.readLines(start, end),
})
const visibleContextMenuItems = computed(() =>
  exportRestricted.value
    ? contextMenuItems.filter((item) => !('action' in item) || item.action !== selectAllInContainer)
    : contextMenuItems,
)
useScopedCopy(
  scrollerEl,
  () => [],
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

// Inline notes (footnotes): hover preview of a marker's note (useLineNotesTooltip).
const {
  lineNoteTooltip,
  closeLineNoteTooltip,
  keepLineNoteTooltipOpen,
  releaseLineNoteTooltip,
  beginLineNoteTooltipSelection,
  openLineNoteTooltip,
} = useLineNotesTooltip(
  scrollerEl,
  (marker) => {
    const row = marker.closest<HTMLElement>('[data-index]')
    return row ? { row, lineId: props.lines.idAt(Number(row.dataset.index)) } : null
  },
  getLineNotesForLine,
  // The line a search result opened: its matched words are marked in its notes too.
  (lineId) =>
    props.searchHighlightLineIndex != null && props.lines.idAt(props.searchHighlightLineIndex) === lineId
      ? props.searchHighlightTerms
      : undefined,
)

// A search hit on a footnote (issue #13) opens its source line (initialLineIndex) with that
// note popped open: once the line's notes have loaded and its row shows their markers, the
// note's marker opens its preview as a hover would. One-shot.
if (props.openNoteIndex != null && props.initialLineIndex != null) {
  const noteIndex = props.openNoteIndex
  const lineIndex = props.initialLineIndex
  let done = false
  let stop: (() => void) | null = null
  stop = watch(
    () => {
      void props.linesRevision
      const lineId = lineIndex < props.lines.length ? props.lines.idAt(lineIndex) : 0
      return lineId ? getLineNotesForLine(lineId) : []
    },
    async (notes) => {
      if (done || !notes.length) return
      done = true
      stop?.()
      await nextTick()
      requestAnimationFrame(() => {
        const row = scroller()?.rowElement(lineIndex)
        const marker = row ? markerForNote(row, notes, noteIndex) : null
        if (marker) openLineNoteTooltip(marker)
      })
    },
    { immediate: true },
  )
  if (done) stop()
}

// Hovering the scrollbar shows the reader's current book and TOC path — reaching
// for the scrollbar is when "where am I?" gets asked.
const { scrollbarTocTooltip, onScrollerMove, onScrollerMoveEnd } = useBookViewScrollbarTocTooltip(scrollbarElement, {
  getCurrentLineIndex: () => readCurrentPosition()?.lineIndex ?? null,
  getTargetLineIndex: (clientY) => scroller()?.barTargetAt(clientY) ?? null,
  getThumbRect: () => scroller()?.thumbRect() ?? null,
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

const { scrollToLine, scrollToLineId, lineIndexForId, currentScrollGeneration } = useBookViewLinesNavigation(
  scroller,
  () => props.lines,
  () => props.searchBarVisible ?? false,
  cancelOpeningFollowUps,
)

const { readBookmarkPoint, scrollToBookmarkPoint } = useBookViewLinesBookmarkPoint(
  scroller,
  () => props.searchBarVisible ?? false,
  scrollToLine,
  lineIndexForId,
  currentScrollGeneration,
)

/** Whether the row at `index` has its text — read in the row slot, with the lines revision so a text change under the same index re-renders the row. */
function rowHasText(index: number): boolean {
  void props.linesRevision
  return props.lines.contentAt(index) != null
}

function onLineClick(index: number, event: MouseEvent) {
  // A drag to select text ends with a `click` event too. Don't hijack it to change
  // the commentary line — the user only wants to select text. A plain click leaves
  // the selection collapsed; a drag-select leaves real (non-empty) text selected.
  if (hasActiveTextSelection()) return
  const line = props.lines.at(index)
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
  return set.has(props.lines.idAt(lineIndex))
}

function isMultiSelected(lineIndex: number): boolean {
  const set = multiSelectLineIdSet.value
  if (!set) return false
  return set.has(props.lines.idAt(lineIndex))
}

function focusScroller() {
  scrollerEl.value?.focus({ preventScroll: true })
}

// Focus the lines view once it has loaded (this component only mounts once the
// saved position has been read, so its start row is known at setup), so the arrow keys / PageUp-PageDown drive the
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
/**
 * Above this much HTML on the clipboard the export goes to Word as a file (exportToWord) instead of
 * a paste. Measured 2026-10-29 (Chromium copy event -> OS clipboard -> Word 16 Selection.Paste of
 * CF_HTML, a private Word instance): the clipboard always held the whole payload (10 MB in, 10 MB
 * on the clipboard - nothing is clipped there); Word pasted 1 / 3 / 6 / 8 MB whole (every paragraph
 * arrived; 5 / 10 / 32 / 24 s) and died mid-paste at 10 MB (RPC 0x800706BE). 6 MB keeps a margin.
 */
const PASTE_INTO_WORD_MAX_HTML_CHARS = 6_000_000

async function pasteAllIntoWord(requestId?: string, isCancelled: () => boolean = () => false): Promise<void> {
  // The whole book is exported, and lines are kept only near the reader: read it now.
  let rows: LineItem[] = []
  try { rows = await wholeBookReady() } catch { /* export what is loaded */ }
  // Cancelled while the book loaded: nothing reached Word yet, so nothing is sent.
  if (isCancelled()) return
  selectAllInContainer()
  if (settingsStore.copyWithNotes) {
    try { await prepareForLines(rows.map((l) => l.id)) } catch { /* export degrades; proceed */ }
  }
  if (isCancelled()) {
    window.getSelection()?.removeAllRanges()
    return
  }
  // Held so the caller's progress spinner lasts until Word has the paste.
  let pasting: Promise<unknown> = Promise.resolve()
  const copied = triggerCopy((html) => {
    if (html.length > PASTE_INTO_WORD_MAX_HTML_CHARS) {
      showToast(t('bookView.exportLargeAsFile'))
      pasting = exportToWord(html, bookTitle, requestId)
        .then((result) => { if (result.error) showToast(result.error, { variant: 'error' }) })
        .catch(() => {})
    } else {
      pasting = pasteIntoWord(requestId).catch(() => {})
    }
  })
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
  forgetRenderedLines(lineIds)
  evictWordLinkAnchorsForLines(lineIds)
  evictLineNotesForLines(lineIds)
}

defineExpose({ scrollerElement: () => scrollerEl.value, pasteAllIntoWord, scrollToLine, scrollToLineId, readBookmarkPoint, scrollToBookmarkPoint, focusScroller, captureScrollPos, readCurrentPosition, retainedLineRanges, onLinesTextEvicted })
</script>

<template>
  <div dir="rtl" class="lines-content">
    <ContextMenu ref="contextMenuRef" :items="visibleContextMenuItems" />
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
      v-if="lineNoteTooltip"
      :key="`line-note-${lineNoteTooltip.id}`"
      :data="lineNoteTooltip"
      @pointer-enter="keepLineNoteTooltipOpen"
      @pointer-leave="releaseLineNoteTooltip"
      @select-start="beginLineNoteTooltipSelection"
      @close="closeLineNoteTooltip"
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
      :mode="linesLayout"
      :row-kind="rowKind"
      paragraph-class="reading-column-line"
      tabindex="0"
      data-ctrlf-enabled
      :style="{ fontSize: `${fontPx}px` }"
      :data-teamim="bookHasTeamim ? 'true' : 'false'"
      @scroll="onScroll()"
      @move="onScrollerMove"
      @opened="onScrollerOpened"
      @moveend="onScrollerMoveEnd(); emit('moveend')"
      @click="onMarkerClick"
      @contextmenu="contextMenuRef?.show($event)"
    >
      <template #row="{ index, kind }">
        <!-- A parent alt-TOC entry (a parasha, "hilchot X"): its own line above the text.
             Drawn by CSS from the attribute, so it holds no text to copy or search. -->
        <span
          v-if="rowHasText(index) && shownAltTocLabels(index, 'breaks')"
          class="alt-toc-break-label"
          :class="{ 'reading-column-line': kind !== 'inline' }"
          :data-alt-toc="shownAltTocLabels(index, 'breaks')"
          aria-hidden="true"
        />
        <!-- Never a placeholder: the scroller asked for these lines before mounting them. -->
        <!-- An inline row flows inside the scroller's paragraph, which carries the column look. -->
        <component
          :is="kind === 'inline' ? 'span' : 'div'"
          v-if="rowHasText(index)"
          class="line"
          :class="{
            'reading-column-line': kind !== 'inline',
            'line-inline': kind === 'inline',
            selected: props.commentaryVisible && !multiSelectLineIdSet && selectedLineId === lines.idAt(index),
            'toc-section': props.commentaryVisible && !multiSelectLineIdSet && isInActiveSection(index),
            'multi-selected': props.commentaryVisible && isMultiSelected(index),
            'before-inline-label': kind === 'inline' && inlineAltTocLabel(index + 1) != null,
          }"
          :data-alt-toc="kind === 'inline' ? inlineAltTocLabel(index) : shownAltTocLabels(index, 'inlines')"
          v-html="lineContent(lines.contentAt(index)!, index, lines.idAt(index))"
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
/* Typography, exact line spacing, wide-pane padding, headings and search marks come
   from the shared reading-column stylesheet (assets/styles/readingColumn.css) through
   the row's `reading-column-line` class. Only the book view's own decorations live here. */
/* Books whose text carries cantillation marks get their own body font — set on the
   scroller so each pane in a split view follows its own book, not a global flag. */
.scroller[data-teamim='true'] .line {
  font-family: var(--teamim-text-font);
}
/* Markers keep their zero-height box even in exact line-spacing mode.
   The attribute is matched on a bare `html[...]` ancestor, NOT via `:global()`:
   Vue's scoped-CSS compiler replaces the whole selector with the `:global()`
   argument and discards the rest. */
html[data-fixed-line-height='true'] .line :deep(.user-note-marker),
html[data-fixed-line-height='true'] .line :deep(.line-note-marker),
html[data-fixed-line-height='true'] .line :deep(.word-link-marker) {
  line-height: 1;
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
/* Flowing lines have no row edge for the accent bar: the selection and the active
   section tint the text's own background instead, and the alt-TOC label sits in
   the running text. */
.line-inline::after {
  content: none;
}
.line-inline.toc-section {
  background: color-mix(in srgb, var(--accent-color) 6%, transparent);
}
.line-inline.selected {
  background: color-mix(in srgb, var(--accent-color) 14%, transparent);
}
/* Inside running text the label is a marker between two sentences: a clear gap on
   both sides, so it reads as a break point, not as a word. The gaps are PLAIN SPACES
   widened by word-spacing, never margins: a collapsible space at a line's edge is
   dropped, so a label that wraps to a new line starts flush, with no indent. The gap
   before it ends the previous line's text; the gap after is the label's own trailing
   space (its inner spaces are fixed-width, see inlineAltTocLabel). */
.line.line-inline[data-alt-toc]::before {
  display: inline;
  content: attr(data-alt-toc) ' ';
  word-spacing: 0.6em;
}
.line.line-inline.before-inline-label::after {
  content: ' ';
  position: static;
  width: auto;
  background: none;
  opacity: 1;
  transition: none;
  word-spacing: 1em;
}
/* A label on a paragraph's FIRST line marks a section start (it follows a heading,
   or a TOC line) — on its own line above the text, as in rows mode. Only a label
   inside running text (an aliyah, a daf marker) stays inline. */
:deep(.windowed-scroller-paragraph > .windowed-scroller-row:first-child) > .line.line-inline[data-alt-toc]::before {
  display: block;
  content: attr(data-alt-toc);
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
/* Outside a paragraph the label is a row of its own, in the same reading column as the
   line below it (width, margins); inside one the paragraph already is that column. */
.alt-toc-break-label.reading-column-line {
  display: block;
}
.line[data-alt-toc]::before,
.alt-toc-break-label::before {
  content: attr(data-alt-toc);
  display: block;
  font-size: 0.85rem;
  font-weight: 600;
  opacity: 0.35;
  padding-block-end: 2px;
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
/* Inline notes (lineNotes.ts): the text's own footnote markers preview their note on hover;
   notes without a marker get one empty end-of-line marker, its glyph drawn here. */
.line :deep(sup.footnote-marker),
.line :deep(.line-note-marker) {
  color: var(--accent-color);
  cursor: pointer;
}
.line :deep(.line-note-marker) {
  font-size: 0.72em;
  vertical-align: super;
  line-height: 1;
  font-style: normal;
}
.line :deep(.line-note-marker)::before {
  content: '*';
}
.line :deep(sup.footnote-marker:hover),
.line :deep(.line-note-marker:hover) {
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
