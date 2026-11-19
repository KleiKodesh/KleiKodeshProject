<script setup lang="ts">
import { ref, computed, watch, nextTick, inject, onBeforeUnmount, shallowReactive } from 'vue'
import { useResizeObserver, watchDebounced } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useBookView } from './useBookView'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { showToast } from '@/composables/useToast'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { bookProtectionOf } from './bookProtection'
import type { BookBookmark } from '@/stores/bookBookmarks'
import BookViewToolbar from './BookViewToolbar.vue'
import SplitPane from '@/components/SplitPane.vue'
import BookViewLinesContent from './lines/BookViewLinesContent.vue'
import BookViewSearchBar from '@/components/ReadingSearchBar.vue'
import SidePanel from '@/components/SidePanel.vue'
import BookViewTocTree from './toc/BookViewTocTree.vue'
import CommentaryTreePanel from './commentary/CommentaryTreePanel.vue'
import CommentaryPanelHost from './commentary/CommentaryPanelHost.vue'
import DetachedWindow from '@/components/DetachedWindow.vue'
import BookViewHebrewBooksSideEdition from './BookViewHebrewBooksSideEdition.vue'
import { useUserEditionsStore } from '@/stores/userEditionsStore'
import { userItemAsEdition } from './useBookEditionOpen'
import { useHebrewBooksSideEditionMode } from './useHebrewBooksSideEditionMode'
import { useHebrewBooksSideEditionStore } from '@/stores/hebrewBooksSideEditionStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { cancelWordExport } from '@/webview-host/bridge'
import { useSidePanelLayout } from '@/composables/useSidePanelLayout'
import { slotForSearchMode, isSideCommentarySlot, SIDE_COMMENTARY_SLOTS, COMMENTARY_SLOTS } from './bookViewTypes'
import type { CommentarySlot, SideEditionPersistState } from './bookViewTypes'
import { useI18n } from 'vue-i18n'
import BookViewReadAloudPanel from './BookViewReadAloudPanel.vue'
import { useBookViewReadAloud } from './useBookViewReadAloud'
import { useBookViewReadAloudHighlight } from './useBookViewReadAloudHighlight'
import { provideBookWordLinkTargets } from './lines/bookWordLinkTargets'

const { t } = useI18n()

// One word-link targets query per book for the whole view (toolbar dropdown + lines).
provideBookWordLinkTargets()
const toolbarRef = ref<InstanceType<typeof BookViewToolbar> | null>(null)
const linesContentRef = ref<InstanceType<typeof BookViewLinesContent> | null>(null)
const searchBarRef = ref<InstanceType<typeof BookViewSearchBar> | null>(null)
const bottomHostRef = ref<InstanceType<typeof CommentaryPanelHost> | null>(null)
const sideHostRef = ref<InstanceType<typeof CommentaryPanelHost> | null>(null)
const sideLeftHostRef = ref<InstanceType<typeof CommentaryPanelHost> | null>(null)
// A panel shown in its own window is rendered by a different host (see "Commentary
// panels in their own window" below); while it exists it is that slot's panel.
type PanelHost = InstanceType<typeof CommentaryPanelHost>
const detachedHostRefs = shallowReactive<Record<CommentarySlot, PanelHost | null>>({
  bottom: null,
  side: null,
  'side-left': null,
})
// The TOC renders docked beside the text on a wide pane and as an overlay
// otherwise — never both, so persistence reads whichever is live.
const tocTreeDockedRef = ref<InstanceType<typeof BookViewTocTree> | null>(null)
const tocTreeOverlayRef = ref<InstanceType<typeof BookViewTocTree> | null>(null)
const bookViewRoot = ref<HTMLElement | null>(null)
const bookViewStore = useBookViewStore()
const booksDataStore = useBooksDataStore()
// Shown when a publisher-restricted book's export is attempted (Core's book protection).
const EXPORT_RESTRICTED_MESSAGE = t('bookView.exportRestricted')
const paneId = inject<1 | 2>('paneId', 1)

// Track the shell's own width instead of the viewport width so that in split
// view each pane responds to its own size, not the full window size.
const shellWidth = ref(window.innerWidth)
useResizeObserver(bookViewRoot, ([entry]) => { shellWidth.value = entry!.contentRect.width })
const isWideScreen = computed(() => shellWidth.value >= 650)
/** Share of the split row the book text keeps when both side columns are open. */
const MIN_LINES_FRACTION = 0.2
// The TOC / filter side panel: inline with a sash on a wide page, overlay on a narrow one
// (shared with the FastDocView page). sidePanelResizeArea wraps panel + sash + content-area.
const sidePanelResizeArea = ref<HTMLElement | null>(null)
const {
  isOverlay: sidePanelIsOverlay,
  panelStyle: sidePanelStyle,
  onDividerPointerDown: onSidePanelDividerPointerDown,
  onResizePointerMove: onSidePanelResizePointerMove,
  onResizePointerUp: onSidePanelResizePointerUp,
} = useSidePanelLayout({ pageElement: bookViewRoot, resizeArea: sidePanelResizeArea })

const {
  toolbarPosition, toolbarVisible,
  searchHighlightLineIndex, searchHighlightQuery, searchHighlightSnippet, searchHighlightOffset, searchHighlightTerms,
  openNoteIndex,
  searchVisible,
  selectedLineId, searchMode,
  activeTocEntryId, activeAltTocEntryId,
  tocVisible,
  sidePanelVisible, sidePanelToggleButtonEl,
  panels, anyCommentaryVisible, openCommentarySlots, commentaryPersistState, tocPersistState,
  tabId, bookId, lines, linesRevision, ensureLines, readLines, lineCharacterCounts, hasCommentaries, hasRelatedBooks, hasToc,
  bookHasTeamim,
  bookIsCommentary,
  versions, activeVersionId, selectVersion, versionPersistState,
  hebrewBooksEditions, hebrewBooksTalmudPdfPath,
  filterGroups, commentaryLoading, commentaryLoadError, ensureLineContents, trimLineContents,
  tocEntries, tocSearchTree, altTocPane, tocLoading, tocError, restoredTocState,
  altTocLabelMap, selectedSectionLineIds, manualSelectionLineIds,
  getHighlightsForLine, applyHighlight, clearHighlight,
  getNotesForLine, scheduleNotesLoad, createNote, updateNote, deleteNote,
  scheduleWordLinkAnchorsLoad,
  getCommentaryLineNotesForLine, scheduleLineNotesLoad,
  prepareCommentaryExportData, prepareCommentaryExportTargets, resolveCommentaryWordLinkTarget,
  commentaryTocPaths,
  currentScrollLineIndex,
  idbResolved, restoredSideEdition, initialLineIndex, initialScrollTop, initialScrollOffset,
  flashOpenLine,
  activeMatchCount, activeMatchIdx, contentSearch,
  onLinesScrolled, onLinesMoveEnd, onTocSelect, onAltTocSelect,
  onLineSelected, onNavigateSection, navigateToAdjacentTocSection,
  openBookTarget,
  openContentSearch, openCommentarySearch, toggleSearch,
  onQueryChange, onSearchNext, onSearchPrev, onModeChange,
  toggleTocPanel, closeSidePanel,
  getActiveTocEntry, getActiveAltTocEntry, getTocPath,
  bookTitle,
} = useBookView(
  () => toolbarRef.value,
  () => linesContentRef.value,
  () => searchBarRef.value,
  {
    bottom: () => (detachedHostRefs.bottom ?? bottomHostRef.value)?.view ?? null,
    side: () => (detachedHostRefs.side ?? sideHostRef.value)?.view ?? null,
    'side-left': () => (detachedHostRefs['side-left'] ?? sideLeftHostRef.value)?.view ?? null,
  },
  // Only one of the two ever renders (docked on a wide pane, overlay otherwise).
  () => tocTreeDockedRef.value ?? tocTreeOverlayRef.value ?? null,
)

// The side panels need a pane wide enough to sit beside the text. Rendering is
// gated on both flags so a pane that narrows never shows a cramped column, and
// the watcher below closes them so their toggles and the search bar agree.
// ── Commentary panels in their own window ───────────────────────────────────
// A detached panel leaves its place in the book view (the text takes the room) and
// is rendered inside a DetachedWindow at the end of the template. It is the same
// panel object, so its commentator, filter, search and place carry over both ways.
function isDetached(slot: CommentarySlot): boolean {
  return panels[slot].visible.value && panels[slot].detachment.isDetached.value
}
const detachedSlots = computed(() => COMMENTARY_SLOTS.filter(isDetached))
const detachedWindowTitle = computed(() => t('bookView.commentary.detachedTitle', { book: bookTitle }))

/** The window could not open (a browser's popup blocker): the panel stays in its place. */
function onDetachBlocked(slot: CommentarySlot) {
  panels[slot].detachment.attach()
  showToast(t('bookView.commentary.detachBlocked'), { variant: 'error' })
}

/** Open in its place inside the book view (not closed, not in its own window). */
function isOpenInPlace(slot: CommentarySlot): boolean {
  return panels[slot].visible.value && !panels[slot].detachment.isDetached.value
}
const bottomCommentaryOpen = computed(() => isOpenInPlace('bottom'))
const sideCommentaryOpen = computed(() => isOpenInPlace('side') && isWideScreen.value)
const sideLeftCommentaryOpen = computed(() => isOpenInPlace('side-left') && isWideScreen.value)

// Re-keys the lines scroller: opening or closing EITHER side column re-wraps the
// text, so both belong in the key (see the capture watcher below).
const sideColumnsKey = computed(() => `${sideCommentaryOpen.value}:${sideLeftCommentaryOpen.value}`)

// Everything a commentary panel needs that is NOT per panel. Bound with v-bind on
// every host so the shared list is written once. (A panel's line and its commentary
// are per panel: the host takes them from panel.source.)
const commentarySharedProps = computed(() => ({
  bookId,
  // The filter tree's rows. Shared: every panel filters the same book list, and
  // which of those rows are checked is per panel (treeState + scopeKey).
  filterGroups: filterGroups.value,
  getHighlightsForLine,
  applyHighlight,
  clearHighlight,
  getNotesForLine,
  scheduleNotesLoad,
  scheduleWordLinkAnchorsLoad,
  getLineNotesForLine: getCommentaryLineNotesForLine,
  scheduleLineNotesLoad,
  prepareExportData: prepareCommentaryExportData,
  prepareExportTargets: prepareCommentaryExportTargets,
  resolveWordLinkTarget: resolveCommentaryWordLinkTarget,
  createNote,
  updateNote,
  deleteNote,
  commentaryTocPaths: commentaryTocPaths.value,
}))

/** A new bookmark's default name: the TOC path of the section the line sits in. */
function bookmarkLabelForLine(lineIndex: number): string {
  const entry = getActiveTocEntry(lineIndex)
  return entry ? getTocPath(entry) : ''
}

/** Lands the bookmark's exact point - and reselects its text when it was made from a selection. */
function goToBookmark(bookmark: BookBookmark) {
  void linesContentRef.value?.scrollToBookmarkPoint(
    bookmark.lineId, bookmark.lineIndex, bookmark.letterOffset ?? 0, bookmark.selectionLetterCount ?? 0)
}

/** The lines' context menu: the toolbar's bookmarks dropdown opens on the new bookmark. */
function createBookmark() {
  void toolbarRef.value?.createBookmark()
}

// ── Read aloud ── The toolbar button opens the panel and starts reading from the selection
// or the top line in view; closing the panel stops. A book or version change stops too.
const readAloud = useBookViewReadAloud()
const readAloudOpen = ref(false)
const { word: readAloudWord } = useBookViewReadAloudHighlight(
  readAloud,
  () => linesContentRef.value?.scrollerApi() ?? null,
  (lineIndex) => linesContentRef.value?.scrollToLine(lineIndex) ?? Promise.resolve(false),
)
function startReadAloud() {
  if (bookId == null) return
  readAloud.start(bookId, activeVersionId.value, currentScrollLineIndex.value, lines.value.length)
}
function toggleReadAloud() {
  readAloudOpen.value = !readAloudOpen.value
  if (readAloudOpen.value) startReadAloud()
  else readAloud.stop()
}
function closeReadAloud() {
  readAloudOpen.value = false
  readAloud.stop()
}
watch(activeVersionId, () => readAloud.stop())

// All toolbar props that do not depend on its position, so the three placements
// (top / side / bottom) each spread one object instead of repeating the list.
const toolbarProps = computed(() => ({
  searchVisible: searchVisible.value,
  tocVisible: tocVisible.value,
  hasToc: hasToc.value,
  hasCommentaries: hasCommentaries.value,
  hasRelatedBooks: hasRelatedBooks.value,
  tabId,
  bookId,
  exportRestricted: exportRestricted.value,
  exportingToWord: exportingToWord.value,
  versions: versions.value,
  activeVersionId: activeVersionId.value,
  hebrewBooksEditions: hebrewBooksEditions.value,
  hebrewBooksTalmudPdfPath: hebrewBooksTalmudPdfPath.value,
  currentScrollLineIndex: currentScrollLineIndex.value,
  lines: lines.value,
  bookmarkLabelForLine,
  readBookmarkPoint: () => linesContentRef.value?.readBookmarkPoint() ?? null,
  bottomCommentaryVisible: panels.bottom.visible.value,
  sideCommentaryVisible: sideCommentaryOpen.value || isDetached('side'),
  sideLeftCommentaryVisible: sideLeftCommentaryOpen.value || isDetached('side-left'),
  canUseSidePanel: isWideScreen.value,
  readAloudOpen: readAloudOpen.value,
}))

// The search bar shows one query at a time: the content query, or the query of
// whichever commentary panel it is currently aimed at.
const activeSearchQuery = computed(() => {
  const slot = slotForSearchMode(searchMode.value)
  return slot ? panels[slot].search.query.value : contentSearch.query.value
})

// ── The bottom panel's filter tree ──────────────────────────────────────────
// A dropdown, exactly like the side panels' (see CommentaryPanelHost) - it floats
// over the content and never reflows the layout. The one difference is its height:
// it fills the whole book-view body rather than stopping at the bottom panel. That
// is why it is rendered here and not in the host: SplitPane and .side-lines both
// clip, so a dropdown mounted inside the bottom panel could only be as tall as
// that panel.
const bottomFilterButtonEl = computed(
  () => bottomHostRef.value?.view?.getFilterButtonEl?.() ?? null,
)

/** Clicking a book in the bottom tree scrolls the bottom panel to it. */
function scrollBottomTreeSelectionIntoView(targetBookId: number) {
  bottomHostRef.value?.view?.scrollToGroup(targetBookId)
}

const bottomFilterRef = ref<HTMLElement | null>(null)
const { justClosed: bottomFilterJustClosed } = useDropdownClose(
  bottomFilterRef,
  () => panels.bottom.closeFilter(),
  {
    toggleButton: bottomFilterButtonEl,
    // The tree holds a search input, and focus moving into a WebView iframe must
    // not shut it mid-typing (same reasoning as the side dropdowns).
    closeOnBlur: false,
  },
)

function onToggleBottomFilter() {
  if (bottomFilterJustClosed.value) return
  panels.bottom.toggleFilter()
}

// Horizontal placement: aligned to the start edge of its own PANEL, like every
// other panel's dropdown. A side panel's needs no measuring - its column is the
// panel, so `inset-inline-start: 0` already means the panel's edge. This one is
// positioned against .content-area, which spans the whole body, so it has to
// measure where the bottom panel's own start edge falls (an open side column
// pushes it inward).
const contentAreaRef = ref<HTMLElement | null>(null)
const bottomFilterInlineStart = ref(0)

function measureBottomFilterOffset() {
  const panel = bottomHostRef.value?.$el as HTMLElement | undefined
  const container = contentAreaRef.value
  if (!panel?.getBoundingClientRect || !container) return
  const panelRect = panel.getBoundingClientRect()
  const containerRect = container.getBoundingClientRect()
  // RTL: inline-start is the physical RIGHT edge, so the offset is measured from
  // the container's right edge to the panel's right edge.
  // +6px so the dropdown is inset from the panel edge rather than flush to it,
  // leaving all four corners free to round (see .bottom-filter-dropdown).
  bottomFilterInlineStart.value = Math.max(0, containerRect.right - panelRect.right) + 6
}

// Measured when it opens, and again whenever anything moves the button sideways: a
// side column opening or closing, one of their dividers being dragged, or the pane
// resizing. The bottom panel's own divider only moves it vertically, which no
// longer matters now that the dropdown spans the full height.
watch(
  [
    () => panels.bottom.filterOpen.value,
    sideColumnsKey,
    () => panels.side.fraction.value,
    () => panels['side-left'].fraction.value,
    shellWidth,
  ],
  async ([open]) => {
    if (!open) return
    await nextTick()
    measureBottomFilterOffset()
  },
  { immediate: true },
)

// ── Divider drag: the side commentary columns ────────────────────────────────
// One handler pair for both columns; the dragging slot decides which edge the
// fraction is measured from (RTL: 'side' hugs the right edge, 'side-left' the left).
const splitContainer = ref<HTMLElement | null>(null)
const draggingSideSlot = ref<CommentarySlot | null>(null)

function onSplitDividerPointerDown(e: PointerEvent, slot: CommentarySlot) {
  draggingSideSlot.value = slot
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
}
function onSplitPointerMove(e: PointerEvent) {
  const slot = draggingSideSlot.value
  if (!slot || !splitContainer.value) return
  const rect = splitContainer.value.getBoundingClientRect()
  const distance = slot === 'side' ? rect.right - e.clientX : e.clientX - rect.left
  // With both columns open the text keeps at least MIN_LINES_FRACTION of the row,
  // so dragging one column wide can never squeeze the text out between them.
  const otherSlot: CommentarySlot = slot === 'side' ? 'side-left' : 'side'
  const otherOpen = otherSlot === 'side' ? sideCommentaryOpen.value : sideLeftCommentaryOpen.value
  const otherFraction = sideColumnFraction(otherSlot)
  const max = otherOpen ? 1 - MIN_LINES_FRACTION - otherFraction : 0.9
  const fraction = Math.min(max, Math.max(0.1, distance / rect.width))
  if (isSideEditionIn(slot)) sideEditionFraction.value = fraction
  else panels[slot].fraction.value = fraction
}
function onSplitPointerUp() {
  draggingSideSlot.value = null
}

// ── Divider drag: TOC / filter side panel ────────────────────────────────────
// (useSidePanelLayout above: drag, fraction 0.1..0.5, overlay below 520 px.)

// ── Panel toggles ────────────────────────────────────────────────────────────

function toggleCommentaryPanel(slot: CommentarySlot) {
  // A side panel needs a wide pane to OPEN in; closing one (its own window included) never does.
  if (isSideCommentarySlot(slot) && !isWideScreen.value && !panels[slot].visible.value) return
  panels[slot].visible.value = !panels[slot].visible.value
}

// The publisher's limits for this book, from the catalog row (Core's book protection).
const bookProtection = computed(() => bookProtectionOf(bookId))
const exportRestricted = computed(() => bookProtection.value?.allowsEditableExport === false)

// Same path as the copy menu's paste-into-Word, over the whole book (select-all), so
// it lands at the Word cursor with the active copy flags instead of in a new document.
//
// Checked here (the button stays visible, dimmed), so no entry point to the export
// can get past the restriction.
// Drives the toolbar button's spinner; also blocks a second click mid-export.
const exportingToWord = ref(false)
async function onExportToWord() {
  if (exportRestricted.value) {
    showToast(EXPORT_RESTRICTED_MESSAGE, { variant: 'error' })
    return
  }
  // A cancelled export may still be finishing (a paste Word already began): one at a time.
  if (exportingToWord.value || exportInFlight) return
  exportingToWord.value = true
  exportInFlight = true
  const generation = ++exportGeneration
  try {
    // One id for the whole export: the paste, or its fallback to a file for a large book.
    // Not crypto.randomUUID: it exists only in a secure context, which the host page may not be.
    exportRequestId = newExportRequestId()
    const result = await linesContentRef.value?.pasteAllIntoWord(exportRequestId, () => generation !== exportGeneration)
    if (result?.failedHtml) exportFailedHtml.value = result.failedHtml
  } catch {
    showToast(t('bookView.exportFailed'), { variant: 'error' })
  } finally {
    exportInFlight = false
    if (generation === exportGeneration) exportingToWord.value = false
  }
}
/** The book's HTML after Word could not take it: drives the "save as HTML file" offer. */
const exportFailedHtml = ref<string | null>(null)
/** Saved through a blob link (works outside a secure context, unlike the file-picker API). */
function saveFailedExportAsHtml() {
  const html = exportFailedHtml.value
  exportFailedHtml.value = null
  if (!html) return
  const doc = html.startsWith('<html') ? html : `<!doctype html><html><head><meta charset="utf-8"></head><body dir="rtl">${html}</body></html>`
  const url = URL.createObjectURL(new Blob([doc], { type: 'text/html;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = `${(bookTitle || 'book').replace(/[\\/:*?"<>|]/g, '_')}.html`
  a.click()
  setTimeout(() => URL.revokeObjectURL(url), 60_000)
}
function newExportRequestId(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('')
}
/** Bumped by a cancel: the export that was running no longer drives the button. */
let exportGeneration = 0
let exportRequestId: string | null = null
let exportInFlight = false
/** The animated X: cancels the running export (paste or file) - the service / host ends only a
 *  Word instance that export started - and the pending call resolves as cancelled. */
function onCancelExportToWord() {
  exportGeneration++
  exportingToWord.value = false
  if (exportRequestId) void cancelWordExport(exportRequestId)
  exportRequestId = null
}

// Opening or closing EITHER side commentary column re-wraps the text, so the lines
// pane is re-keyed: a NEW scroller is created, and its start row is the position
// captured from the old one just before the swap. Without the capture the new
// instance would be given initialLineIndex/initialScrollTop as session restore left
// them and open at a stale place (or at the top when nothing was saved).
watch(sideColumnsKey, () => {
  // Session restore opens the side panel BEFORE idbResolved flips, when no lines
  // pane exists yet: nothing to capture, and the restored values are exactly what
  // the pane should be created with. The same holds while a pane exists but has no
  // rows yet (captureScrollPos is null).
  if (!idbResolved.value) return
  const pos = linesContentRef.value?.captureScrollPos?.()
  if (!pos) return
  initialLineIndex.value = undefined
  initialScrollTop.value = pos.scrollIndex
  initialScrollOffset.value = pos.scrollOffset
})

// A pane too narrow for the side columns closes them rather than leaving one open
// but unrendered, so their toggle buttons and the search bar's mode menu stay
// truthful. Watching visible TOO (not just width changes) matters for session
// restore on an already-narrow shell: isWideScreen never changes there, but restore
// flips visible true - without the clamp that panel stays logically open while never
// rendering (phantom search mode, held backfill gate, no way to close it).
// Debounced, and re-checks the live width: the host's fullscreen toggle passes
// through WindowState.Normal for a moment, and that transient narrow dip must
// not destroy panel state. Rendering is gated on isWideScreen directly, so a
// genuinely narrow pane still hides the columns instantly.
watchDebounced(
  // isDetached too: a side panel coming back from its own window to a narrow pane has no place.
  [() => SIDE_COMMENTARY_SLOTS.map((slot) => panels[slot].visible.value && !isDetached(slot)), isWideScreen],
  () => {
    if (isWideScreen.value) return
    // A panel in its own window does not depend on this pane's width.
    for (const slot of SIDE_COMMENTARY_SLOTS) if (!isDetached(slot)) panels[slot].visible.value = false
  },
  { debounce: 300 },
)

// Both columns opened at their saved fractions can together leave the text too
// little room. Shrink them proportionally the moment that happens - the drag
// clamp only covers the column being dragged.
watch([sideCommentaryOpen, sideLeftCommentaryOpen], ([rightOpen, leftOpen]) => {
  if (!rightOpen || !leftOpen) return
  // A column showing the edition has its own width (sideColumnFraction), so scale that one.
  const total = sideColumnFraction('side') + sideColumnFraction('side-left')
  const max = 1 - MIN_LINES_FRACTION
  if (total <= max) return
  const scale = max / total
  for (const slot of ['side', 'side-left'] as const) {
    if (isSideEditionIn(slot)) sideEditionFraction.value = sideColumnFraction(slot) * scale
    else panels[slot].fraction.value *= scale
  }
})

watch(() => bookViewStore.openSearchSignal, (signal) => { if (signal.paneId === paneId) openContentSearch() })
watch(() => bookViewStore.toggleCommentaryPanelSignal, (signal) => {
  if (signal.paneId === paneId) toggleCommentaryPanel(signal.slot)
})
watch(() => bookViewStore.toggleTocPanelSignal, (signal) => { if (signal.paneId === paneId) toggleTocPanel() })

// ── HebrewBooks edition side by side ─────────────────────────────────────────
// With the editions toggle on (and room for it - see useHebrewBooksSideEditionMode), the
// side column the reader chose (left or right) shows a scanned edition instead of
// commentary: the one picked in the editions dropdown, else the book's best. Picking one
// opens that column.
const paneNavigation = usePaneNavigation()
const sideEditionStore = useHebrewBooksSideEditionStore()
const { opensSideBySide: editionOpensSideBySide, slot: editionSlot } = useHebrewBooksSideEditionMode(paneId)

// The reader's own HebrewBooks items can show side by side too; their own PDFs cannot
// (the side panel is a HebrewBooks scan).
const userEditionsStore = useUserEditionsStore()
if (bookId) void userEditionsStore.load(bookId)
const sideCandidateEditions = computed(() => [
  ...userEditionsStore.editionsOf(bookId ?? 0).filter((e) => e.kind === 'hb' && e.hbId).map(userItemAsEdition),
  ...hebrewBooksEditions.value,
])

const sideEdition = computed(() => {
  const editions = sideCandidateEditions.value
  if (!editionOpensSideBySide.value || !editions.length) return null
  const pickedId = sideEditionStore.getPickedEditionId(tabId, bookId)
  return editions.find((e) => e.id === pickedId) ?? editions.find((e) => e.isBest) ?? editions[0]!
})
type SideSlot = 'side' | 'side-left'
function isSideSlotOpen(slot: CommentarySlot): boolean {
  return slot === 'side' ? sideCommentaryOpen.value : slot === 'side-left' ? sideLeftCommentaryOpen.value : false
}
const sideEditionShown = computed(() => isSideSlotOpen(editionSlot.value) && sideEdition.value != null)
/** Whether this side column shows the edition (not commentary) right now. */
function isSideEditionIn(slot: CommentarySlot): boolean {
  return sideEditionShown.value && editionSlot.value === slot
}

// A scanned page needs room to be readable, so the edition gets its own width - half
// and half with the text by default - instead of the commentary column's saved one.
// With the other side's commentary open too, it splits what that column leaves.
const sideEditionFraction = ref<number | null>(null)
const sideEditionDefaultFraction = computed(() => {
  const otherSlot: SideSlot = editionSlot.value === 'side' ? 'side-left' : 'side'
  return isSideSlotOpen(otherSlot) ? (1 - panels[otherSlot].fraction.value) / 2 : 0.5
})
/** A side column's width as a fraction of the row - the edition's own while it shows one. */
function sideColumnFraction(slot: SideSlot): number {
  return isSideEditionIn(slot) ? (sideEditionFraction.value ?? sideEditionDefaultFraction.value) : panels[slot].fraction.value
}

watch(
  () => (sideEditionShown.value ? sideEdition.value!.id : null),
  (shownId) => sideEditionStore.setShownEditionId(paneId, shownId),
  { immediate: true },
)
// Dragging the edition's divider moves the bottom filter button sideways too (see the
// measure watch above, which runs before sideEditionFraction exists).
watch(sideEditionFraction, async () => {
  if (!panels.bottom.filterOpen.value) return
  await nextTick()
  measureBottomFilterOffset()
})
// Session restore (tab, last-read, back/forward) hands back the saved pick and width.
watch(restoredSideEdition, (saved) => {
  if (!saved) return
  if (saved.editionId != null) sideEditionStore.restorePickedEdition(tabId, bookId, saved.editionId)
  sideEditionFraction.value = saved.fraction
}, { immediate: true })

/** The pick and the edition column's width, for persistence alongside the position. */
function sideEditionPersistState(): SideEditionPersistState {
  return { editionId: sideEditionStore.getPickedEditionId(tabId, bookId), fraction: sideEditionFraction.value }
}

watch(() => sideEditionStore.pickSignal, (signal) => {
  if (signal.paneId !== paneId || !isWideScreen.value) return
  // The edition takes that side column, so a commentary detached from it comes back first.
  panels[editionSlot.value].detachment.attach()
  panels[editionSlot.value].visible.value = true
})
// Switching sides while an edition is on screen moves it: the new side opens, and the side
// it leaves closes (it was open only to show the edition).
watch(editionSlot, (newSlot, oldSlot) => {
  // Only a column open in place can be showing the edition; a commentary in its own window is not one.
  if (!isWideScreen.value || !isOpenInPlace(oldSlot) || !sideEdition.value) return
  panels[oldSlot].visible.value = false
  panels[newSlot].detachment.attach()
  panels[newSlot].visible.value = true
})
onBeforeUnmount(() => {
  sideEditionStore.setShownEditionId(paneId, null)
})

// The edition's column is not commentary while an edition is shown, so the search bar must
// not offer (or stay on) searching it - its host is not mounted.
// Nor a panel in its own window: that one has a find bar of its own there.
const searchableCommentarySlots = computed(() =>
  openCommentarySlots.value.filter(
    (slot) => !isDetached(slot) && !(sideEditionShown.value && slot === editionSlot.value),
  ),
)

function getSideEditionOutlineTarget(): string | undefined {
  return paneNavigation.activeTab.tocPath
}
</script>

<template>
  <div class="book-view" ref="bookViewRoot">
    <!-- Word could not take the export: offer the same content as an HTML file. -->
    <ConfirmDialog
      v-if="exportFailedHtml"
      :title="t('bookView.exportFailedTitle')"
      :desc="t('bookView.exportFailedDesc')"
      :confirm-label="t('bookView.exportSaveAsHtml')"
      @confirm="saveFailedExportAsHtml"
      @cancel="exportFailedHtml = null"
    />
    <!-- Top toolbar -->
    <BookViewToolbar
      v-if="toolbarVisible && toolbarPosition === 'top'"
      ref="toolbarRef"
      v-bind="toolbarProps"
      @toggle-bottom-commentary="toggleCommentaryPanel('bottom')"
      @toggle-side-commentary="toggleCommentaryPanel('side')"
      @toggle-side-left-commentary="toggleCommentaryPanel('side-left')"
      @toggle-search="toggleSearch"
      @toggle-toc="toggleTocPanel"
      @export-to-word="onExportToWord"
      @cancel-export-to-word="onCancelExportToWord"
      @navigate-to-next-section="navigateToAdjacentTocSection('next')"
      @go-to-bookmark="goToBookmark"
      @navigate-to-previous-section="navigateToAdjacentTocSection('previous')"
      @select-version="selectVersion"
      @toggle-read-aloud="toggleReadAloud"
    />
    <!-- Middle row: side toolbar + main area (RTL: first child = physical right) -->
    <!-- dir=rtl in both UI languages: left/right settings and the side/side-left commentary slots name PHYSICAL sides, laid out as in Hebrew. -->
    <div class="body-row" dir="rtl">
      <BookViewToolbar
        v-if="toolbarVisible && (toolbarPosition === 'right' || toolbarPosition === 'left')"
        ref="toolbarRef"
        v-bind="toolbarProps"
        :class="toolbarPosition === 'left' ? 'toolbar-order-end' : ''"
        @toggle-bottom-commentary="toggleCommentaryPanel('bottom')"
        @toggle-side-commentary="toggleCommentaryPanel('side')"
        @toggle-side-left-commentary="toggleCommentaryPanel('side-left')"
        @toggle-search="toggleSearch"
        @toggle-toc="toggleTocPanel"
        @export-to-word="onExportToWord"
        @cancel-export-to-word="onCancelExportToWord"
        @navigate-to-next-section="navigateToAdjacentTocSection('next')"
        @go-to-bookmark="goToBookmark"
        @navigate-to-previous-section="navigateToAdjacentTocSection('previous')"
        @select-version="selectVersion"
        @toggle-read-aloud="toggleReadAloud"
      />

      <!--
        main-area: flex row containing the inline side panel (when wide) + content-area.
        When the side panel is in inline mode this wrapper also captures pointer events
        for the divider drag — its bounding rect is used to compute sidePanelFraction.
      -->
      <div
        ref="sidePanelResizeArea"
        class="main-area"
        @pointermove="onSidePanelResizePointerMove"
        @pointerup="onSidePanelResizePointerUp"
      >
        <!--
          Inline side panel — rendered as a sibling of content-area inside main-area.
          RTL: first child in a flex row = physical right, so the panel appears on the right.
        -->
        <template v-if="sidePanelVisible && !sidePanelIsOverlay">
          <SidePanel
            :toggle-button-el="sidePanelToggleButtonEl"
            :is-overlay="false"
            :style="sidePanelStyle"
            @close="closeSidePanel"
          >
            <BookViewTocTree
              ref="tocTreeDockedRef"
              :active-toc-entry-id="activeTocEntryId"
              :active-alt-toc-entry-id="activeAltTocEntryId"
              :toc-entries="tocEntries"
              :toc-search-tree="tocSearchTree"
              :alt-toc-pane="altTocPane"
              :initial-state="restoredTocState"
              :loading="tocLoading"
              :error="tocError"
              @select="onTocSelect"
              @alt-select="onAltTocSelect"
            />
          </SidePanel>
          <div class="sash sash-v" @pointerdown="onSidePanelDividerPointerDown" />
        </template>

        <!-- content-area: always fills remaining horizontal space -->
        <div ref="contentAreaRef" class="content-area">
          <BookViewReadAloudPanel v-if="readAloudOpen" :read-aloud="readAloud" @play="startReadAloud" @close="closeReadAloud" />
          <!--
            One nested layout, always: each side commentary is a column beside the
            text, the bottom commentary a row beneath it. All three are independent
            panels, so any combination can be open and no branch excludes another.
            RTL: first child is physically right, last child physically left.
          -->
          <div
            ref="splitContainer"
            class="side-by-side"
            @pointermove="onSplitPointerMove"
            @pointerup="onSplitPointerUp"
          >
            <template v-if="sideCommentaryOpen">
              <div class="side-commentary" :style="{ width: `${sideColumnFraction('side') * 100}%` }">
                <BookViewHebrewBooksSideEdition
                  v-if="isSideEditionIn('side')"
                  :edition="sideEdition!"
                  :get-outline-target="getSideEditionOutlineTarget"
                />
                <CommentaryPanelHost
                  v-else
                  ref="sideHostRef"
                  :panel="panels.side"
                  v-bind="commentarySharedProps"
                  :search-active="searchVisible && searchMode === 'commentary-side'"
                  @close="panels.side.visible.value = false"
                  @navigate-section="(direction, id) => onNavigateSection('side', direction, id)"
                  @toggle-filter-panel="panels.side.toggleFilter()"
                  @toggle-search="openCommentarySearch('side')"
                  @open-book="openBookTarget"
                />
              </div>
              <div class="sash sash-v" @pointerdown="onSplitDividerPointerDown($event, 'side')" />
            </template>

            <div class="side-lines">
              <SplitPane v-model="panels.bottom.fraction.value" :bottom-visible="bottomCommentaryOpen">
                <template #top>
                  <!-- Created only once the saved position has been read: a windowed
                       scroller takes its start row as an argument of its creation,
                       it is never opened at the top and moved afterwards. -->
                  <BookViewLinesContent
                    v-if="idbResolved"
                    :key="sideColumnsKey"
                    ref="linesContentRef"
                    @create-bookmark="createBookmark"
                    :lines="lines"
                    :lines-revision="linesRevision"
                    :ensure-lines="ensureLines"
                    :read-lines="readLines"
                    :read-aloud-word="readAloudWord"
                    :line-character-counts="lineCharacterCounts"
                    :alt-toc-label-map="altTocLabelMap"
                    :selected-line-id="selectedLineId"
                    :commentary-visible="anyCommentaryVisible"
                    :commentary-persist-state="commentaryPersistState"
                    :toc-persist-state="tocPersistState"
                    :version-persist-state="versionPersistState"
                    :side-edition-persist-state="sideEditionPersistState"
                    :active-version-id="activeVersionId"
                    :initial-line-index="initialLineIndex"
                    :initial-scroll-index="initialScrollTop"
                    :initial-scroll-offset="initialScrollOffset"
                    :flash-line-on-open="flashOpenLine"
                    :search-highlight-line-index="searchHighlightLineIndex"
                    :search-highlight-query="searchHighlightQuery"
                    :search-highlight-snippet="searchHighlightSnippet"
                    :search-highlight-offset="searchHighlightOffset"
                    :search-highlight-terms="searchHighlightTerms"
                    :open-note-index="openNoteIndex"
                    :search-bar-visible="searchVisible"
                    :search-query="searchVisible && searchMode === 'content' ? contentSearch.highlightQuery.value : ''"
                    :search-match-lines="contentSearch.matches.value"
                    :current-match-line-index="searchVisible && searchMode === 'content' ? contentSearch.currentMatchLineIndex.value : undefined"
                    :current-match-occurrence="searchVisible && searchMode === 'content' ? contentSearch.currentMatchOccurrence.value : undefined"
                    :get-active-toc-entry="getActiveTocEntry"
                    :get-active-alt-toc-entry="getActiveAltTocEntry"
                    :get-toc-path="getTocPath"
                    :toc-entries="tocEntries"
                    :selected-section-line-ids="selectedSectionLineIds"
                    :multi-select-line-ids="manualSelectionLineIds"
                    :book-has-teamim="bookHasTeamim"
                    :book-is-commentary="bookIsCommentary"
                    @scrolled="onLinesScrolled"
                    @moveend="onLinesMoveEnd"
                    @line-selected="onLineSelected"
                    @ctrl-f="openContentSearch"
                  />
                </template>
                <template #bottom>
                  <CommentaryPanelHost
                    ref="bottomHostRef"
                    :panel="panels.bottom"
                    v-bind="commentarySharedProps"
                    :search-active="searchVisible && searchMode === 'commentary-bottom'"
                    @close="panels.bottom.visible.value = false"
                    @navigate-section="(direction, id) => onNavigateSection('bottom', direction, id)"
                    @toggle-filter-panel="onToggleBottomFilter()"
                    @toggle-search="openCommentarySearch('bottom')"
                    @open-book="openBookTarget"
                  />
                </template>
              </SplitPane>
            </div>

            <!-- RTL: last child sits physically left, opposite the 'side' column. -->
            <template v-if="sideLeftCommentaryOpen">
              <div
                class="sash sash-v"
                @pointerdown="onSplitDividerPointerDown($event, 'side-left')"
              />
              <div
                class="side-commentary"
                :style="{ width: `${sideColumnFraction('side-left') * 100}%` }"
              >
                <BookViewHebrewBooksSideEdition
                  v-if="isSideEditionIn('side-left')"
                  :edition="sideEdition!"
                  :get-outline-target="getSideEditionOutlineTarget"
                />
                <CommentaryPanelHost
                  v-else
                  ref="sideLeftHostRef"
                  :panel="panels['side-left']"
                  v-bind="commentarySharedProps"
                  :search-active="searchVisible && searchMode === 'commentary-side-left'"
                  @close="panels['side-left'].visible.value = false"
                  @navigate-section="(direction, id) => onNavigateSection('side-left', direction, id)"
                  @toggle-filter-panel="panels['side-left'].toggleFilter()"
                  @toggle-search="openCommentarySearch('side-left')"
                  @open-book="openBookTarget"
                />
              </div>
            </template>
          </div>

          <!-- Search bar (floats inside content-area) -->
          <BookViewSearchBar
            ref="searchBarRef"
            :visible="searchVisible"
            :toolbar-position="toolbarPosition"
            :match-count="activeMatchCount"
            :current-match="activeMatchIdx"
            :commentary-visible="searchableCommentarySlots.length > 0"
            :open-commentary-slots="searchableCommentarySlots"
            :mode="searchMode"
            :query="activeSearchQuery"
            @close="searchVisible = false"
            @query-change="onQueryChange"
            @next="onSearchNext"
            @prev="onSearchPrev"
            @mode-change="onModeChange"
          />

          <!-- Overlay side panel: only rendered when screen is too narrow for inline mode -->
          <SidePanel
            v-if="sidePanelVisible && sidePanelIsOverlay"
            :toggle-button-el="sidePanelToggleButtonEl"
            :is-overlay="true"
            @close="closeSidePanel"
          >
            <BookViewTocTree
              ref="tocTreeOverlayRef"
              :active-toc-entry-id="activeTocEntryId"
              :active-alt-toc-entry-id="activeAltTocEntryId"
              :toc-entries="tocEntries"
              :toc-search-tree="tocSearchTree"
              :alt-toc-pane="altTocPane"
              :initial-state="restoredTocState"
              :loading="tocLoading"
              :error="tocError"
              @select="onTocSelect"
              @alt-select="onAltTocSelect"
            />
          </SidePanel>

          <!--
            The bottom panel's filter dropdown. Same behaviour as a side panel's,
            but anchored here so it can run the full height of the body instead of
            being clipped to the bottom panel.
          -->
          <div
            v-if="panels.bottom.filterOpen.value && bottomCommentaryOpen"
            ref="bottomFilterRef"
            class="bottom-filter-dropdown"
            :style="{
              insetInlineStart: `${bottomFilterInlineStart}px`,
              '--bottom-filter-offset': `${bottomFilterInlineStart}px`,
            }"
          >
            <CommentaryTreePanel
              :groups="filterGroups"
              :tree-state="panels.bottom.treeState"
              :scope-key="panels.bottom.scopeKey"
              :scroll-to-book="scrollBottomTreeSelectionIntoView"
              @close="panels.bottom.closeFilter()"
            />
          </div>
        </div><!-- end .content-area -->
      </div><!-- end .main-area -->
    </div><!-- end .body-row -->

    <!-- Bottom toolbar -->
    <BookViewToolbar
      v-if="toolbarVisible && toolbarPosition === 'bottom'"
      ref="toolbarRef"
      v-bind="toolbarProps"
      @toggle-bottom-commentary="toggleCommentaryPanel('bottom')"
      @toggle-side-commentary="toggleCommentaryPanel('side')"
      @toggle-side-left-commentary="toggleCommentaryPanel('side-left')"
      @toggle-search="toggleSearch"
      @toggle-toc="toggleTocPanel"
      @export-to-word="onExportToWord"
      @cancel-export-to-word="onCancelExportToWord"
      @navigate-to-next-section="navigateToAdjacentTocSection('next')"
      @go-to-bookmark="goToBookmark"
      @navigate-to-previous-section="navigateToAdjacentTocSection('previous')"
      @select-version="selectVersion"
      @toggle-read-aloud="toggleReadAloud"
    />

    <!-- Commentary panels the reader moved to their own window. -->
    <DetachedWindow
      v-for="slot in detachedSlots"
      :key="slot"
      :name="`commentary-${paneId}-${slot}`"
      :title="detachedWindowTitle"
      @closed="panels[slot].detachment.attach()"
      @blocked="onDetachBlocked(slot)"
    >
      <CommentaryPanelHost
        :ref="(host) => { detachedHostRefs[slot] = host as PanelHost | null }"
        :panel="panels[slot]"
        v-bind="commentarySharedProps"
        :search-active="false"
        @close="panels[slot].visible.value = false"
        @navigate-section="(direction, id) => onNavigateSection(slot, direction, id)"
        @open-book="openBookTarget"
      />
    </DetachedWindow>
  </div>
</template>

<style scoped>
.book-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}

.body-row {
  display: flex;
  flex-direction: row;
  flex: 1;
  min-height: 0;
}

/* Pushes the left-position toolbar to the physical left end of body-row */
.toolbar-order-end {
  order: 1;
}

/* main-area: flex row containing inline side panel (when visible) + content-area.
   Also serves as the pointer-capture container for side panel drag resizing. */
.main-area {
  flex: 1;
  display: flex;
  flex-direction: row;
  min-height: 0;
  min-width: 0;
}

.content-area {
  position: relative;
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
  min-width: 0;
}

/* ── Side panel resize divider (inline mode) ───────────────────────────────── */
/* ── The bottom panel's filter dropdown ───────────────────────────────────── */
/* Floats over .content-area (which is position:relative), so it reflows nothing.
   Unlike a side panel's dropdown it is anchored to the body, not to its own panel,
   which is the whole point: it runs the full height of the book view instead of
   being trapped in the bottom panel's height. inset-inline-start is set inline,
   measured from the filter button.
   Width comes from the tree itself (CommentaryTreePanel is width: fit-content). */
.bottom-filter-dropdown {
  position: absolute;
  /* Inset from the body's edges so all four corners are free to round. */
  top: 6px;
  bottom: 6px;
  z-index: 60;
  display: flex;
  /* Whatever is left of the pane past the offset, so an inward-pushed button
     cannot make the dropdown overflow the far edge. */
  max-width: calc(100% - var(--bottom-filter-offset, 0px) - 12px);
  overflow: hidden;
  background: var(--bg-secondary);
  /* The app's floating-panel chrome (FullTextSearchOptionsPanel, BookViewNoteBubble):
     1px border, 8px radius on all four corners, soft shadow. */
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgb(0 0 0 / 24%);
  --tree-bg: var(--bg-secondary);
}

/* ── Side commentary column + text column ─────────────────────────────────── */
.side-by-side {
  display: flex;
  flex-direction: row;
  flex: 1;
  overflow: hidden;
  min-height: 0;
}

.side-commentary {
  flex-shrink: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.side-lines {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

/* VS Code's sash model, as in SplitPane: a hairline at rest, a wide invisible grab
   band (::before), and a visible line (::after) that thickens and takes the accent
   colour only while hovered or dragged. The element is 1px and transparent so the
   ::after can grow without shifting the layout. */
</style>
