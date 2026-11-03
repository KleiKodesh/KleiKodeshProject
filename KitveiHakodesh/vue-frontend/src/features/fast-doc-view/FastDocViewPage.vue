<script setup lang="ts">
import { computed, inject, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useDebounceFn, useElementSize, useEventListener, useStyleTag, useThrottleFn } from '@vueuse/core'
import WindowedScroller from '@/components/WindowedScroller.vue'
import SplitPane from '@/components/SplitPane.vue'
import SidePanel from '@/components/SidePanel.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useScopedKeys } from '@/composables/useTextSelectionKeys'
import { useVirtualizedTextSelection } from '@/composables/useVirtualizedTextSelection'
import { useZoomHandler, zoomIn, zoomOut, ZOOM_CONFIG } from '@/composables/useZoom'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useFastDocViewCommands } from '@/composables/fastDocViewCommands'
import { useTabStore } from '@/stores/tabStore'
import { fastDocViewApi } from '@/webview-host/fastDocViewApi'
import { cancelLocalFileConversion, openFileInDefaultApp, openUrlInSystemBrowser, restoreLocalFile } from '@/webview-host/bridge'
import { useI18n } from 'vue-i18n'
import { showToast } from '@/composables/useToast'
import { parseBookLink } from '@/utils/appDeepLink'
import { useHostSearchStore } from '@/stores/hostSearchStore'
import type { ConversionActionState } from '@/components/ConversionActionButton.vue'
import { useFastDocViewDocument } from './useFastDocViewDocument'
import { buildFastDocViewOutline, type FastDocViewOutlineNode } from './fastDocViewOutline'
import FastDocViewToolbar from './FastDocViewToolbar.vue'
import FastDocViewToc, { type FastDocViewTocState } from './FastDocViewToc.vue'
import ReadingSearchBar from '@/components/ReadingSearchBar.vue'
import { useWindowedRowLanding } from '@/composables/useWindowedRowLanding'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import { useFastDocViewSearch } from './useFastDocViewSearch'
import { highlightSearchMatches } from '@/utils/searchMatchHighlight'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import ContextMenu from '@/components/ContextMenu.vue'
import { useContextMenuLongPress } from '@/composables/useContextMenuLongPress'
import { useFastDocViewContextMenu } from './useFastDocViewContextMenu'
import { useSidePanelLayout } from '@/composables/useSidePanelLayout'
import WordLinkTooltip from '@/features/book-view/lines/WordLinkTooltip.vue'
import { useLinkPreviewTooltip } from '@/features/book-view/lines/useWordLinkTooltip'
import FastDocViewNotesPane, { fastDocViewNoteKey } from './FastDocViewNotesPane.vue'
import { useFastDocViewNotes, type FastDocViewIndexedNote } from './useFastDocViewNotes'

const paneNavigation = usePaneNavigation()
const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const tabId = paneNavigation.activeTabId
// Read once: AppPageView keys this page by tab and file, so another file remounts it.
const filePath = paneNavigation.activeTab.localFilePath ?? null

const windowedScroller = ref<InstanceType<typeof WindowedScroller> | null>(null)
const scrollerElement = ref<HTMLElement | null>(null)
const renderedRange = computed(() => windowedScroller.value?.renderedRange ?? { start: 0, end: 0 })

// The retained ranges are read only when blocks are stored, after setup has run.
const fastDocument = useFastDocViewDocument({
  api: fastDocViewApi,
  retainedRanges: () => {
    const ranges: (readonly [number, number])[] = [[renderedRange.value.start, renderedRange.value.end]]
    for (const index of pinnedRowIndexes.value) ranges.push([index, index + 1])
    const selection = selectedRowIndexRange()
    if (selection) ranges.push([selection.firstRowIndex, selection.lastRowIndex + 1])
    return ranges
  },
})

const { pinnedRowIndexes, selectedRowIndexRange } = useVirtualizedTextSelection(
  scrollerElement,
  () => `${fastDocument.contentVersion.value}:${fastDocument.blockCount.value}`,
)

// ── Position and zoom (per tab, in TabState) ─────────────────────────────────
const initialIndex = ref<number | undefined>(undefined)
const initialOffsetPx = ref(0)
const zoom = ref<number>(ZOOM_CONFIG.DEFAULT)
const stateReady = ref(false)

/**
 * Tab view state writes, one at a time: each reads the stored state and writes it back with its
 * fields, so two interleaved writers (position, zoom, notes layout) would drop each other's.
 */
let tabStateWrites: Promise<void> = Promise.resolve()
function patchTabState(patch: Record<string, unknown>): Promise<void> {
  tabStateWrites = tabStateWrites.then(async () => {
    const existing = (await tabStore.getTabViewState(tabId)) ?? {}
    tabStore.setTabViewState(tabId, { ...existing, ...patch })
  }).catch(() => {})
  return tabStateWrites
}

/** Saves where the reader is now (block + offset) into the tab's view state. */
async function savePositionNow() {
  const first = windowedScroller.value?.readFirstVisible(0)
  if (!first) return
  await patchTabState({
    fastDocViewBlockIndex: first.index,
    fastDocViewBlockOffset: Math.round(first.offsetWithinRow),
  })
}
const savePosition = useDebounceFn(savePositionNow, 300)

// Zoom, the book view's model: the text (its lines) and the notes (its commentary) each keep
// their own per-tab zoom. Ctrl+wheel / trackpad pinch / touch pinch, and Ctrl+= / - / 0 with the
// focus inside a pane, zoom that pane only (useZoomHandler on each scroller); the toolbar buttons,
// and the keys with the focus elsewhere in the page, step both, each from its own value.
const notesZoom = ref<number>(ZOOM_CONFIG.DEFAULT)
async function saveZoom() {
  await patchTabState({
    fastDocViewZoom: zoom.value !== ZOOM_CONFIG.DEFAULT ? zoom.value : undefined,
    fastDocViewNotesZoom: notesZoom.value !== ZOOM_CONFIG.DEFAULT ? notesZoom.value : undefined,
  })
}
function setZoom(value: number) {
  zoom.value = value
  void saveZoom()
}
function setNotesZoom(value: number) {
  notesZoom.value = value
  void saveZoom()
}
function zoomBoth(step: (current: number) => number) {
  zoom.value = step(zoom.value)
  notesZoom.value = step(notesZoom.value)
  void saveZoom()
}
const zoomModel = computed({ get: () => zoom.value, set: setZoom })
useZoomHandler({ zoom: zoomModel, target: scrollerElement, keyboard: true })
const notesPaneRef = ref<{ scrollerElement: HTMLElement | null; renderedRange: () => { start: number; end: number } } | null>(null)
const notesScrollerElement = computed(() => notesPaneRef.value?.scrollerElement ?? null)
const notesZoomModel = computed({ get: () => notesZoom.value, set: setNotesZoom })
useZoomHandler({ zoom: notesZoomModel, target: notesScrollerElement, keyboard: true })
const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)

// Page text width: each block's column is as wide as its section's text area in Word (page
// width minus margins), scaled by how much larger than the document's own default size the
// text is shown - so lines break at the same words at any zoom. A narrow pane fills its width.
/** The document's default font size in px (the library stylesheet's .word-document size; Word's 12 pt without one). */
const documentFontPx = computed(() => {
  const match = /\.word-document\s*\{[^}]*font-size:\s*([\d.]+)pt/.exec(fastDocument.summary.value?.stylesheet ?? '')
  const points = match ? Number(match[1]) : 12
  return (points > 0 ? points : 12) * (96 / 72)
})
/** The column style of block `index`: its section's width (twips / 15 = CSS px at 96 dpi) at the text's scale. */
function blockColumnStyle(index: number): Record<string, string> | undefined {
  // The document's own text width is the only margin (plus the page's fixed gutter); the book
  // view's column width and padding never apply here. "Ignore document margins" (global): the
  // text fills the pane - for the text and, through this same function, the notes pane.
  if (settingsStore.fastDocViewIgnoreMargins) return undefined
  const twips = fastDocument.textWidthTwipsAt(index)
  if (twips == null) return undefined
  const widthPx = (twips / 15) * textScale.value
  return { '--fast-doc-view-column-max-width': `calc(${widthPx.toFixed(1)}px + 2 * var(--fast-doc-view-column-padding))` }
}
/**
 * How much larger than the document's own sizes its text is shown. The library writes Word's
 * absolute sizes (pt: font sizes, indents, spacing, table widths), so the block's text is laid out
 * at the document's sizes and CSS-zoomed as a whole: every size scales together (a 16 pt heading
 * stays 16/12 of 12 pt body) and, with the column scaled the same, lines break at the same words.
 */
const textScale = computed(() => fontPx.value / documentFontPx.value)
const blockTextStyle = computed(() => ({ zoom: String(textScale.value), fontSize: `${documentFontPx.value}px` }))

// The book view's text context menu, with the items a Word document can serve (useFastDocViewContextMenu).
const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const contextMenu = useFastDocViewContextMenu({ selectAllInContainer: () => selectAllInContainer(), paneNavigation })
const { selectAllInContainer } = useScopedKeys(scrollerElement, {
  onCtrlF: () => openSearch(),
  onCtrlV: () => contextMenu.onPasteIntoWord(),
  onCtrlShiftC: () => contextMenu.onSearchInRepository(),
})
useContextMenuLongPress(scrollerElement, (event) => contextMenuRef.value?.showAtPosition(event.clientX, event.clientY))

// In-document search (Ctrl+F): the book view's own search bar (ReadingSearchBar, floating over
// the text) and its behaviour; only the hit source differs - the service finds hit blocks (and how
// many matches each holds) in bounded steps. Matches are highlighted inside the rendered blocks only. Closing the bar keeps
// the query for the next open, as in the book view.
const searchVisible = ref(false)
const search = useFastDocViewSearch({
  api: fastDocViewApi,
  documentId: () => fastDocument.summary.value?.documentId,
  currentBlock: () => windowedScroller.value?.readFirstVisible(0)?.index ?? 0,
})
function openSearch() {
  searchVisible.value = true
}
function closeSearch() {
  searchVisible.value = false
  scrollerElement.value?.focus({ preventScroll: true })
}
// The reader moving the view (wheel, keyboard, a press on the text or its scrollbar) re-arms
// the search's first step, as a reader scroll does in the book view. Told by its source - the
// input that causes it - so the search's own landing scroll never counts, whatever its timing.
function onReaderInput() {
  followPinnedNote.value = null
  search.onReaderScroll()
  lastNavigatedHeadingId = null
}
useEventListener(scrollerElement, 'wheel', onReaderInput, { passive: true })
useEventListener(scrollerElement, 'pointerdown', onReaderInput)
// the scroller's drawn scrollbar sits beside the viewport
useEventListener(computed(() => (windowedScroller.value?.scrollbarElement as HTMLElement | null | undefined) ?? null), 'pointerdown', onReaderInput)
useEventListener(scrollerElement, 'keydown', (event: KeyboardEvent) => {
  if (['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' '].includes(event.key)) onReaderInput()
})
// The book view's own landing (shared useWindowedRowLanding): a match never rests under the
// floating search bar, and lands at the edge the step travels toward.
const { scrollToLine: landOnRow } = useWindowedRowLanding(
  () => windowedScroller.value as unknown as WindowedScrollerApi | null,
  () => searchVisible.value,
  () => {},
  () => {},
)
async function onSearchStep(direction: 'forward' | 'backward') {
  const match = search.step(direction)
  if (!match) return
  // The block's nth mark becomes the current one (setCurrentMark), as the book view's per-line occurrence.
  await landOnRow(match.block, { occurrence: match.occurrence, direction })
}
/** A block's HTML with the search query's matches marked (only while searching, only rendered blocks). */
function blockHtml(index: number): string {
  // The book view's clean-text (Ctrl+D, global setting) first, then the search marks, as its line renderer.
  const html = cleanHebrewText(fastDocument.blockAt(index)?.html ?? '', settingsStore.cleanTextFlags)
  return search.query.value.trim() && searchVisible.value ? highlightSearchMatches(html, search.query.value, { ignoreCase: true }) : html
}

onMounted(async () => {
  scrollerElement.value = windowedScroller.value?.scrollerElement ?? null
  const saved = await tabStore.getTabViewState(tabId)
  if (saved?.fastDocViewZoom != null) zoom.value = saved.fastDocViewZoom
  if (saved?.fastDocViewNotesZoom != null) notesZoom.value = saved.fastDocViewNotesZoom
  tocState.value = saved?.fastDocViewToc
  if (saved?.fastDocViewTocVisible) tocVisible.value = true
  if (saved?.fastDocViewNotesVisible != null) notesVisible.value = saved.fastDocViewNotesVisible
  if (saved?.fastDocViewNotesSync != null) notesSync.value = saved.fastDocViewNotesSync
  if (saved?.fastDocViewNotesPlace) notesPlace.value = saved.fastDocViewNotesPlace
  if (saved?.fastDocViewNotesFractions) notesFractions.value = { ...saved.fastDocViewNotesFractions }
  initialIndex.value = saved?.fastDocViewBlockIndex ?? 0
  initialOffsetPx.value = saved?.fastDocViewBlockOffset ?? 0
  stateReady.value = true
  if (filePath) await fastDocument.open(filePath, { reachIndex: initialIndex.value })
})

function onScrollerMounted(instance: unknown) {
  windowedScroller.value = instance as InstanceType<typeof WindowedScroller> | null
  scrollerElement.value = windowedScroller.value?.scrollerElement ?? null
}

// ── The library's stylesheet, scoped to this document's text and notes (the split pane) ───────
const contentRootId = `fast-doc-view-${tabId}`
const scopedStylesheet = computed(() => {
  const css = fastDocument.summary.value?.stylesheet
  return css ? `@scope ([data-fast-doc-view-root="${contentRootId}"]) {\n${css}\n}` : ''
})
useStyleTag(scopedStylesheet)

const isRightToLeft = computed(() => fastDocument.summary.value?.isRightToLeft ?? true)

// ── Side panel (TOC) and notes pane ──────────────────────────────────────────
const pageElement = ref<HTMLElement | null>(null)
// The TOC panel: inline with a sash on a wide page (0.1..0.5 of the row), overlay on a narrow one - as in the book view.
const sidePanelResizeArea = ref<HTMLElement | null>(null)
const {
  isOverlay: sidePanelIsOverlay,
  panelStyle: sidePanelStyle,
  onDividerPointerDown: onSidePanelDividerPointerDown,
  onResizePointerMove: onSidePanelResizePointerMove,
  onResizePointerUp: onSidePanelResizePointerUp,
} = useSidePanelLayout({ pageElement, resizeArea: sidePanelResizeArea })
const tocVisible = ref(false)
// Kept per tab, as the book view keeps its TOC panel open across tab switches and restarts.
watch(tocVisible, (visible) => void patchTabState({ fastDocViewTocVisible: visible || undefined }))
// The TOC's search text, expanded headings and scroll, kept per tab as the book view's
// TocPersistState: snapshotted on close and on leaving the tab (the panel is v-if'd away).
const tocRef = ref<InstanceType<typeof FastDocViewToc> | null>(null)
const tocState = ref<FastDocViewTocState | undefined>()
function snapshotTocState() {
  if (!tocRef.value) return
  tocState.value = tocRef.value.snapshotState()
  void patchTabState({ fastDocViewToc: tocState.value })
}
watch(tocVisible, (visible, wasVisible) => { if (wasVisible && !visible) snapshotTocState() }, { flush: 'sync' })
onBeforeUnmount(snapshotTocState)
const tocToggleButton = ref<HTMLElement | null>(null)
const outline = computed(() => buildFastDocViewOutline(fastDocument.headings.value))
/** The block at the top of the view, followed while scrolling (throttled) - the TOC's current heading. */
const firstVisibleIndex = ref(0)
const trackFirstVisible = useThrottleFn(() => {
  firstVisibleIndex.value = windowedScroller.value?.readFirstVisible(0)?.index ?? firstVisibleIndex.value
}, 150, true)
function onScrollerScroll() {
  void savePosition()
  void trackFirstVisible()
}
/** The last heading at or above the reader's position (headings are in block order). */
const activeOutlineNodeId = computed(() => {
  const nodes = outline.value
  let low = 0
  let high = nodes.length - 1
  let found: number | undefined
  while (low <= high) {
    const middle = (low + high) >> 1
    if (nodes[middle]!.blockIndex <= firstVisibleIndex.value) { found = nodes[middle]!.id; low = middle + 1 } else high = middle - 1
  }
  return found
})
function toggleToc(toggleButton?: HTMLElement | null) {
  if (toggleButton) tocToggleButton.value = toggleButton
  tocVisible.value = !tocVisible.value
}

// The book view's app-level keys, routed to this pane's page (useAppTitleBarShortcuts):
// Ctrl+K table of contents, Ctrl+J notes pane, Ctrl+F search, Ctrl+B toolbar (the same
// per-pane toolbar setting the book view uses).
const paneId = inject<1 | 2>('paneId', 1)
const bookViewStore = useBookViewStore()
const toolbarVisible = computed(() => bookViewStore.getToolbarVisible(paneId))
/** Which edge the toolbar is docked to: the book view's app-wide setting (dragging either toolbar moves both). */
const toolbarPosition = computed(() => bookViewStore.toolbarPosition)
useFastDocViewCommands(paneId, {
  toggleToc: () => toggleToc(),
  toggleNotes: () => { if (hasNotes.value) notesVisible.value = !notesVisible.value },
  openSearch: () => openSearch(),
  toggleToolbar: () => bookViewStore.toggleToolbar(paneId),
})

// Previous / next section (toolbar chevrons, Ctrl+Arrow): the book view's
// navigateToAdjacentTocSection over the headings - step from the current heading (or the one
// the last step landed on, until the reader moves the view), the previous one skipping headings
// at the same block, landed like the book view (force: the heading at the top).
let lastNavigatedHeadingId: number | null = null
function navigateToAdjacentHeading(direction: 'next' | 'previous') {
  const nodes = outline.value
  if (nodes.length === 0) return
  const effectiveId = lastNavigatedHeadingId ?? activeOutlineNodeId.value
  const currentIndex = nodes.findIndex((node) => node.id === effectiveId)
  let target: (typeof nodes)[number] | undefined
  if (direction === 'next') target = nodes[currentIndex + 1]
  else {
    const currentBlock = currentIndex >= 0 ? nodes[currentIndex]!.blockIndex : null
    for (let i = currentIndex - 1; i >= 0; i--) {
      if (currentBlock == null || nodes[i]!.blockIndex < currentBlock) { target = nodes[i]; break }
    }
  }
  if (!target) return
  lastNavigatedHeadingId = target.id
  void landOnRow(target.blockIndex, { force: true })
}
// Ctrl+= / - / 0 with the focus in neither scroller zoom both, as the book view's
// page shortcuts (a focused scroller's own handler zooms only its pane). The browser's page zoom is blocked.
useEventListener(window, 'keydown', (event: KeyboardEvent) => {
  if (!(event.ctrlKey || event.metaKey)) return
  const isZoomIn = event.code === 'Equal' || event.code === 'NumpadAdd'
  const isZoomOut = event.code === 'Minus' || event.code === 'NumpadSubtract'
  const isReset = event.code === 'Digit0' || event.code === 'Numpad0'
  if (!isZoomIn && !isZoomOut && !isReset) return
  // as the book view's: only split view off or this pane focused, and only for this pane's active tab
  if (bookViewStore.splitViewEnabled && bookViewStore.focusedPaneId !== paneId) return
  if (paneNavigation.activeTab.id !== tabId) return
  const focused = document.activeElement
  event.preventDefault()
  if (scrollerElement.value?.contains(focused) || notesScrollerElement.value?.contains(focused)) return
  zoomBoth(isZoomIn ? zoomIn : isZoomOut ? zoomOut : () => ZOOM_CONFIG.DEFAULT)
})
// Ctrl+D toggles clean text, as in the book view (the same global setting and toolbar button).
useEventListener(window, 'keydown', (event: KeyboardEvent) => {
  if (!(event.ctrlKey || event.metaKey) || event.shiftKey || event.altKey || event.code !== 'KeyD') return
  if (bookViewStore.splitViewEnabled && bookViewStore.focusedPaneId !== paneId) return
  if (paneNavigation.activeTab.id !== tabId) return
  event.preventDefault()
  settingsStore.toggleCleanText()
})
// Ctrl+Arrow, as in the book view (RTL: Ctrl+Left = next). On the text, outside inputs.
useEventListener(window, 'keydown', (event: KeyboardEvent) => {
  if (!(event.ctrlKey || event.metaKey) || event.shiftKey || event.altKey) return
  if (event.code !== 'ArrowLeft' && event.code !== 'ArrowRight') return
  if (!pageElement.value?.contains(document.activeElement) || (document.activeElement as HTMLElement | null)?.closest('input, textarea')) return
  if (outline.value.length === 0) return
  event.preventDefault()
  navigateToAdjacentHeading((event.code === 'ArrowLeft') === isRightToLeft.value ? 'next' : 'previous')
})
function onOutlineSelect(node: FastDocViewOutlineNode) {
  // The overlay stays open on a click, as the book view's (an outside click closes it).
  void windowedScroller.value?.scrollToIndex(node.blockIndex)
}

const hasNotes = computed(() => {
  const summary = fastDocument.summary.value
  return !!summary && summary.footnoteCount + summary.endnoteCount > 0
})
// Closed by default, as the book view's commentary panel (useBookViewCommentaryPanel: ref(false)).
const notesVisible = ref(false)
/** Keep the notes pane in sync with the text (the book view's sync-commentaries toggle); on by default here. */
const notesSync = ref(true)
/**
 * Where the notes pane sits, picked in the notes button's dropdown (bottom / right / left). The side
 * places need a wide page, as the book view's side commentary slots (BookViewPage isWideScreen,
 * 650 px); on a narrow one the pane falls back to the bottom without losing the choice.
 */
type NotesPlace = 'bottom' | 'right' | 'left'
const notesPlace = ref<NotesPlace>('bottom')
const { width: pageWidth } = useElementSize(pageElement)
const canPlaceNotesBeside = computed(() => pageWidth.value >= 650)
const effectiveNotesPlace = computed<NotesPlace>(() => (canPlaceNotesBeside.value ? notesPlace.value : 'bottom'))
/** The notes split button's main half: opens / closes the pane in the chosen place. */
function toggleNotes() {
  if (!hasNotes.value) return
  notesVisible.value = !notesVisible.value
}
/** The notes split button's dropdown: moves the pane there and shows it. */
function setNotesPlace(place: NotesPlace) {
  if (!hasNotes.value) return
  notesPlace.value = place
  notesVisible.value = true
}
/** The text pane's share of the split, per place (height below, width beside) - as the book view keeps one per slot. */
const notesFractions = ref<Partial<Record<NotesPlace, number>>>({})
const notesFraction = computed<number>({
  // never dragged in this place: the notes take a quarter of the page (the text the rest)
  get: () => notesFractions.value[effectiveNotesPlace.value] ?? 0.75,
  set: (value) => { notesFractions.value = { ...notesFractions.value, [effectiveNotesPlace.value]: value } },
})
/** The notes pane's text size: the commentary's (commentary font setting at the notes' own zoom), as in the book view. */
const notesFontPx = computed(() => (notesZoom.value / 100) * (settingsStore.commentaryFontSize / 100) * 15)
async function saveNotesLayout() {
  await patchTabState({
    fastDocViewNotesVisible: notesVisible.value,
    fastDocViewNotesSync: notesSync.value,
    fastDocViewNotesPlace: notesPlace.value,
    fastDocViewNotesFractions: { ...notesFractions.value },
  })
}
// Debounced: dragging the split updates the fraction on every pointer move.
const saveNotesLayoutSoon = useDebounceFn(saveNotesLayout, 300)
watch([notesVisible, notesFractions, notesSync, notesPlace], () => void saveNotesLayoutSoon())
// "Open as PDF": the same file through the existing Word -> PDF conversion (an explicit
// choice; FastDocView is the default). The tab becomes a PDF (or HTML fallback) tab.
const { t } = useI18n()
/** idle -> busy (converting; the button cancels) -> done (a check; the click opens the result). */
const pdfState = ref<ConversionActionState>('idle')
let convertedPdf: { url: string; kind?: 'pdf' | 'html' } | null = null
/** Only the Word family converts to PDF (Word itself does the conversion); not .txt. */
const canOpenAsPdf = !!filePath && !filePath.toLowerCase().endsWith('.txt')
/** Bumped by a cancel: the conversion that was running no longer opens or reports anything. */
let openAsPdfGeneration = 0
async function openAsPdf() {
  if (!filePath || pdfState.value !== 'idle') return
  pdfState.value = 'busy'
  showToast(t('fastDocView.convertingToast'))
  const generation = ++openAsPdfGeneration
  const restored = await restoreLocalFile(filePath)
  if (generation !== openAsPdfGeneration) return
  if (!restored?.url) {
    pdfState.value = 'idle'
    showToast(t('fastDocView.openAsPdfFailed'), { variant: 'error', duration: 6000 })
    return
  }
  convertedPdf = { url: restored.url, kind: restored.kind }
  pdfState.value = 'done'
}
/** Opens the converted PDF in this tab: a navigation, so Back returns here at the same block. */
async function openConvertedPdf() {
  if (!filePath || !convertedPdf) return
  const fileName = filePath.substring(Math.max(filePath.lastIndexOf('\\'), filePath.lastIndexOf('/')) + 1)
  await savePositionNow()
  tabStore.updateTab(tabId, {
    route: convertedPdf.kind === 'html' ? '/html-view' : '/pdf-view',
    localFileName: fileName,
    localFilePath: filePath,
    localFileVirtualUrl: convertedPdf.url,
    isOtzariaAddin: false,
  })
}
/** Shell-opens the file with the program registered for its type (hosted: in the app's own process). */
async function openInDefaultApp() {
  if (!filePath) return
  if (!(await openFileInDefaultApp(filePath))) showToast(t('fastDocView.openInDefaultAppFailed'), { variant: 'error', duration: 6000 })
}
// The file changed on disk (the document reopened): a PDF converted from the old file is stale.
watch(() => fastDocument.contentVersion.value, () => {
  if (pdfState.value !== 'done') return
  convertedPdf = null
  pdfState.value = 'idle'
})
function cancelOpenAsPdf() {
  if (!filePath || pdfState.value !== 'busy') return
  openAsPdfGeneration++
  pdfState.value = 'idle'
  cancelLocalFileConversion(filePath)
}

// Dev-only test hook (stripped from production builds, like devTestHook.ts): plain data for
// Playwright checks, and the two actions they drive. The last mounted page owns it.
if (import.meta.env.DEV) {
  const hook = {
    state: () => {
      const summary = fastDocument.summary.value
      const first = windowedScroller.value?.readFirstVisible(0)
      return {
        documentId: summary?.documentId ?? null,
        blockCount: fastDocument.blockCount.value,
        knownBlockCount: summary?.knownBlockCount ?? 0,
        isIndexComplete: fastDocument.isIndexComplete.value,
        footnoteCount: summary?.footnoteCount ?? 0,
        endnoteCount: summary?.endnoteCount ?? 0,
        headingCount: fastDocument.headings.value.length,
        hasWeights: fastDocument.rowWeights.value != null,
        firstVisible: first ? { index: first.index, offsetWithinRow: first.offsetWithinRow } : null,
        renderedRange: { start: renderedRange.value.start, end: renderedRange.value.end },
        cachedBlocks: fastDocument.cachedBlockCount(),
        zoom: zoom.value,
        notesZoom: notesZoom.value,
        notesPaneOpen: notesVisible.value && hasNotes.value,
        search: {
          visible: searchVisible.value,
          isSearching: search.isSearching.value,
          matchCount: search.matchCount.value,
          currentMatch: search.currentMatch.value,
          current: search.matches.value[search.currentMatch.value] ?? null,
        },
      }
    },
    scrollToBlock: (index: number) => windowedScroller.value?.scrollToIndex(index),
    setZoom,
    setNotesZoom,
    setNotesPlace,
    reopen: () => fastDocument.reopen(),
  }
  const target = window as unknown as { __fastDocViewTest?: typeof hook }
  onMounted(() => { target.__fastDocViewTest = hook })
  onBeforeUnmount(() => { if (target.__fastDocViewTest === hook) delete target.__fastDocViewTest })
}

// The notes pane's own rows: every note in body order (bounded cache near its window only).
const fastNotes = useFastDocViewNotes({
  api: fastDocViewApi,
  summary: fastDocument.summary,
  retainedRanges: () => {
    const range = notesPaneRef.value?.renderedRange()
    return range ? [[range.start, range.end]] : []
  },
})
/** A note whose number jumped the text to its reference: sync keeps it (not the block's first note) until the reader moves the text. */
const followPinnedNote = ref<number | null>(null)
/** Sync on: the first note at or after the first block in view. */
const notesFollowIndex = computed<number | null>(() => {
  if (!notesSync.value) return null
  if (followPinnedNote.value != null) return followPinnedNote.value
  const index = fastNotes.firstNoteAtOrAfter(firstVisibleIndex.value)
  return index < fastNotes.noteCount.value ? index : null
})

// Footnotes linked both ways. A reference in the text (<sup class="note-reference"
// data-note-kind data-note-number>, rendered by the library) opens the notes pane and lands its
// note; a note's number jumps the text to the block holding its reference and flashes it.
const landNoteIndex = ref<number | null>(null)
const noteLandingNonce = ref(0)
let noteLookupGeneration = 0
function referenceKey(element: HTMLElement): string {
  return fastDocViewNoteKey(element.dataset.noteKind ?? '', Number(element.dataset.noteNumber ?? 0), (element.textContent ?? '').trim())
}
/** Opens the notes pane on the note a reference in the text points at (loaded and landed there). */
function openNoteFromReference(element: HTMLElement) {
  const row = element.closest<HTMLElement>('[data-index]')
  if (!row) return
  const blockIndex = Number(row.dataset.index)
  const occurrence = [...row.querySelectorAll<HTMLElement>('.note-reference[data-note-kind]')].indexOf(element)
  const key = referenceKey(element)
  notesVisible.value = true
  // Only the latest click lands: an earlier click's slower lookup must not land over it.
  const request = ++noteLookupGeneration
  const documentId = fastDocument.summary.value?.documentId
  void fastNotes.findNote(blockIndex, Math.max(0, occurrence), (note) => fastDocViewNoteKey(note.kind, note.number, note.label) === key).then((index) => {
    if (index == null || request !== noteLookupGeneration || fastDocument.summary.value?.documentId !== documentId) return
    landNoteIndex.value = index
    noteLandingNonce.value++
  })
}
/** The cleaned HTML of the note a reference in the text points at - from its own block's notes, no service call. */
function noteHtmlForReference(element: HTMLElement): string | null {
  const row = element.closest<HTMLElement>('[data-index]')
  const key = referenceKey(element)
  const html = fastDocument.blockAt(Number(row?.dataset.index ?? -1))?.notes.find((note) => fastDocViewNoteKey(note.kind, note.number, note.label) === key)?.html
  return html == null ? null : cleanHebrewText(html, settingsStore.cleanTextFlags)
}
function onContentClick(event: MouseEvent) {
  onLinkClick(event)
}
// A reference previews its note in the book view's word-link tooltip, with its whole lifecycle
// (useLinkPreviewTooltip): hover delay, a grace period to reach the panel, stays open while the
// pointer is inside, a selection drag or a context menu is under way; closes on leave, Esc, the
// close button, a text scroll, a window resize or a click. Its toolbar (text size, close),
// selection, copy, scrolling and resize are the panel's own. A plain click on the reference is
// the book view's "open": the note lands in the notes pane.
const {
  linkPreview: noteTooltip,
  closeLinkPreview: closeNoteTooltip,
  keepOpen: keepNoteTooltipOpen,
  releaseOpen: releaseNoteTooltip,
  beginSelection: beginNoteTooltipSelection,
} = useLinkPreviewTooltip(scrollerElement, {
  selector: '.note-reference[data-note-kind]',
  linkKey: (element) => referenceKey(element as HTMLElement),
  preview: async (element) => {
    const html = noteHtmlForReference(element as HTMLElement)
    return html == null ? null : { bookTitle: '', tocPath: '', html }
  },
  activate: (element) => openNoteFromReference(element as HTMLElement),
})
const REFERENCE_FLASH_MS = 2000

/** Briefly marks an element the reader was jumped to (a note reference, a bookmark target). */
function flash(element: Element | null | undefined) {
  if (!element) return
  element.scrollIntoView({ block: 'nearest' })
  element.classList.add('note-reference--current')
  setTimeout(() => element.classList.remove('note-reference--current'), REFERENCE_FLASH_MS)
}

// Links in the document. Internal ones (a TOC field entry, a cross-reference, w:hyperlink
// w:anchor) point at "#bookmark-<name>": the block is asked of the index and reached by a
// windowed jump - the browser never follows the # (the target may not be mounted). Book
// links (otzaria:// zayit:// kitveihakodeshapp://) open like a host deep link. Web and mail
// links keep their default (they open outside the app).
const hostSearchStore = useHostSearchStore()
function onLinkClick(event: MouseEvent): boolean {
  const anchor = (event.target as HTMLElement | null)?.closest<HTMLAnchorElement>('a[href]')
  const href = anchor?.getAttribute('href') ?? ''
  if (!anchor || !href) return false
  if (href.startsWith('#bookmark-')) {
    event.preventDefault()
    void goToBookmark(href)
    return true
  }
  const bookLink = parseBookLink(href)
  if (bookLink) {
    event.preventDefault()
    void hostSearchStore.openBookLink(bookLink)
    return true
  }
  if (href.startsWith('#')) return false
  event.preventDefault()
  // An app link the app cannot place (no index): never hand it to the shell.
  if (anchor.dataset.appLink) showToast(t('fastDocView.linkNotSupported'))
  else void openUrlInSystemBrowser(href)
  return true
}
async function goToBookmark(href: string) {
  const documentId = fastDocument.summary.value?.documentId
  if (!documentId) return
  let name: string
  try { name = decodeURIComponent(href.slice('#bookmark-'.length)) } catch { name = href.slice('#bookmark-'.length) }
  const blockIndex = await fastDocViewApi.bookmark(documentId, name).catch(() => null)
  if (blockIndex == null) {
    showToast(t('fastDocView.bookmarkNotFound'))
    return
  }
  // An index still building found it past the blocks the scroller knows: wait for the count to reach it.
  if (blockIndex >= fastDocument.blockCount.value) {
    await new Promise<void>((resolve) => {
      const timer = setTimeout(() => { stop(); resolve() }, 15000)
      const stop = watch(fastDocument.blockCount, (count) => {
        if (count > blockIndex) { clearTimeout(timer); stop(); resolve() }
      })
    })
  }
  await windowedScroller.value?.scrollToIndex(blockIndex)
  const row = windowedScroller.value?.rowElement(blockIndex) as HTMLElement | null | undefined
  flash([...(row?.querySelectorAll<HTMLElement>('.bookmark[id]') ?? [])].find((element) => element.id === href.slice(1)) ?? row)
}
async function goToNoteReference(note: FastDocViewIndexedNote) {
  // Sync on: the text's jump must not move the notes off the note the reader came from.
  followPinnedNote.value = note.index
  await windowedScroller.value?.scrollToIndex(note.blockIndex)
  const row = windowedScroller.value?.rowElement(note.blockIndex) as HTMLElement | null | undefined
  const references = [...(row?.querySelectorAll<HTMLElement>(`.note-reference[data-note-kind="${note.kind}"]`) ?? [])]
  const reference = references.find((element) => Number(element.dataset.noteNumber) === note.number && (element.textContent ?? '').trim() === note.label) ?? references[0]
  flash(reference)
}
</script>

<template>
  <div ref="pageElement" class="fast-doc-view">
    <!-- The toolbar docks to the edge the reader dragged it to (the book view's toolbar position
         setting): one toolbar element, placed by the frame's orientation class. -->
    <div class="fast-doc-view-frame" :class="`toolbar-at-${toolbarPosition}`">
    <FastDocViewToolbar
      v-if="toolbarVisible"
      class="fast-doc-view-frame-toolbar"
      :toc-visible="tocVisible"
      :notes-visible="notesVisible && hasNotes"
      :notes-sync="notesSync"
      :notes-place="notesPlace"
      :can-place-notes-beside="canPlaceNotesBeside"
      :has-headings="outline.length > 0"
      :has-notes="hasNotes"
      :zoom="zoom"
      :notes-zoom="notesZoom"
      @toggle-toc="toggleToc"
      @toggle-notes="toggleNotes"
      @set-notes-place="setNotesPlace"
      :ignore-margins="settingsStore.fastDocViewIgnoreMargins"
      @toggle-ignore-margins="settingsStore.fastDocViewIgnoreMargins = !settingsStore.fastDocViewIgnoreMargins"
      @toggle-notes-sync="notesSync = !notesSync"
      @zoom-in="zoomBoth(zoomIn)"
      @zoom-out="zoomBoth(zoomOut)"
      :pdf-state="pdfState"
      :can-open-as-pdf="canOpenAsPdf"
      @open-as-pdf="openAsPdf"
      @cancel-open-as-pdf="cancelOpenAsPdf"
      @open-converted-pdf="openConvertedPdf"
      @open-in-default-app="openInDefaultApp"
      :search-visible="searchVisible"
      @toggle-search="searchVisible ? closeSearch() : openSearch()"
      @navigate-to-next-section="navigateToAdjacentHeading('next')"
      @navigate-to-previous-section="navigateToAdjacentHeading('previous')"
    />
    <div
      ref="sidePanelResizeArea"
      class="fast-doc-view-body"
      @pointermove="onSidePanelResizePointerMove"
      @pointerup="onSidePanelResizePointerUp"
    >
      <SidePanel
        v-if="tocVisible"
        :toggle-button-el="tocToggleButton"
        :is-overlay="sidePanelIsOverlay"
        class="fast-doc-view-toc"
        :style="sidePanelIsOverlay ? undefined : sidePanelStyle"
        @close="tocVisible = false"
      >
        <FastDocViewToc ref="tocRef" :saved-state="tocState" :outline="outline" :active-node-id="activeOutlineNodeId" :is-right-to-left="isRightToLeft" @select="onOutlineSelect" />
      </SidePanel>
      <div v-if="tocVisible && !sidePanelIsOverlay" class="sash sash-v" @pointerdown="onSidePanelDividerPointerDown" />
      <div class="fast-doc-view-main">
        <!-- the book view's floating search bar (same component, same behaviour) -->
        <ReadingSearchBar
          :visible="searchVisible"
          toolbar-position="top"
          :match-count="search.query.value.trim() && !search.isSearching.value && search.matchCount.value === 0 ? 0 : search.matchCount.value || -1"
          :current-match="search.currentMatch.value"
          :commentary-visible="false"
          :open-commentary-slots="[]"
          mode="content"
          :query="search.query.value"
          @close="closeSearch"
          @query-change="search.query.value = $event"
          @next="onSearchStep('forward')"
          @prev="onSearchStep('backward')"
        />
        <MessageBanner
          v-if="fastDocument.indexBuildFailed.value"
          severity="warning"
          :message="$t('fastDocView.indexBuildFailed')"
          :dismissible="true"
        />
        <MessageBanner
          v-if="!filePath"
          severity="info"
          :message="$t('fastDocView.noFileOpen')"
          :dismissible="false"
        />
        <MessageBanner
          v-else-if="fastDocument.error.value"
          severity="error"
          :message="$t('fastDocView.loadError')"
          :details="String(fastDocument.error.value)"
          :dismissible="false"
        />
        <LoadingAnimation v-else-if="fastDocument.isLoading.value || !stateReady" />
        <SplitPane v-else-if="fastDocument.summary.value" class="fast-doc-view-notes-split" :data-fast-doc-view-root="contentRootId" v-model="notesFraction" :bottom-visible="notesVisible && hasNotes" :second-place="effectiveNotesPlace">
          <template #top>
            <div
              class="fast-doc-view-content"
              :dir="isRightToLeft ? 'rtl' : 'ltr'"
              @click="onContentClick"
            >
              <ContextMenu ref="contextMenuRef" :items="contextMenu.items" />
              <WordLinkTooltip
                v-if="noteTooltip"
                :key="`note-${noteTooltip.id}`"
                :data="noteTooltip"
                @pointer-enter="keepNoteTooltipOpen"
                @pointer-leave="releaseNoteTooltip"
                @select-start="beginNoteTooltipSelection"
                @close="closeNoteTooltip"
              />
              <WindowedScroller
                :ref="onScrollerMounted"
                class="fast-doc-view-scroller"
                :count="fastDocument.blockCount.value"
                :initial-index="initialIndex"
                :initial-offset-px="initialOffsetPx"
                :ensure-rows="fastDocument.ensureBlocks"
                :row-source-version="fastDocument.contentVersion.value"
                :pinned-indexes="pinnedRowIndexes"
                :landmark-tiers="fastDocument.landmarkTiers.value"
                :row-weights="fastDocument.rowWeights.value"
                tabindex="0"
                @scroll="onScrollerScroll"
                @contextmenu="contextMenuRef?.show($event)"
              >
                <template #row="{ index }">
                  <div
                    v-if="fastDocument.blockAt(index)"
                    class="reading-column-line fast-doc-view-block"
                    :style="blockColumnStyle(index)"
                  >
                    <div class="fast-doc-view-block-text word-document" :style="blockTextStyle" v-html="blockHtml(index)" />
                  </div>
                </template>
              </WindowedScroller>
            </div>
          </template>
          <template #bottom>
            <FastDocViewNotesPane
              ref="notesPaneRef"
              :note-count="fastNotes.noteCount.value"
              :ensure-notes="fastNotes.ensureNotes"
              :note-at="fastNotes.noteAt"
              :row-source-version="fastNotes.contentVersion.value"
              :column-style="blockColumnStyle"
              :is-right-to-left="isRightToLeft"
              :land-index="landNoteIndex"
              :landing-nonce="noteLandingNonce"
              :follow-index="notesFollowIndex"
              :font-px="notesFontPx"
              @close="notesVisible = false"
              @go-to-reference="goToNoteReference"
            />
          </template>
        </SplitPane>
      </div>
    </div>
    </div>
  </div>
</template>

<style scoped>
.fast-doc-view {
  position: relative;
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}
/* The frame places the toolbar at its edge (physical sides, whatever the page direction). */
.fast-doc-view-frame {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}
.fast-doc-view-frame.toolbar-at-bottom .fast-doc-view-frame-toolbar {
  order: 1;
}
.fast-doc-view-frame.toolbar-at-left,
.fast-doc-view-frame.toolbar-at-right {
  flex-direction: row;
}
.fast-doc-view-frame.toolbar-at-right:dir(ltr) .fast-doc-view-frame-toolbar,
.fast-doc-view-frame.toolbar-at-left:dir(rtl) .fast-doc-view-frame-toolbar {
  order: 1;
}
.fast-doc-view-body {
  position: relative;
  display: flex;
  flex: 1;
  min-height: 0;
  min-width: 0;
}
.fast-doc-view-toc {
  min-width: 0;
}
.fast-doc-view-main {
  /* the search bar floats inside it, as in the book view's content-area */
  position: relative;
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.fast-doc-view-content {
  height: 100%;
  position: relative;
}
/* The column's only margins are the document's own (its page text width, blockColumnStyle) plus
   this small fixed gutter, so text never touches the pane's edge on a narrow window. Not the book
   view's reading column (readingColumn.css padding, --lines-content-max-width from its settings).
   On the page, so the notes pane's entries take the same column as the document. */
.fast-doc-view {
  --fast-doc-view-column-padding: 12px;
}
.fast-doc-view-block {
  padding-inline: var(--fast-doc-view-column-padding);
  max-width: var(--fast-doc-view-column-max-width, none);
}
/* The library's stylesheet (scoped above) styles everything inside `.word-document`: list labels as
   inline-blocks reaching their tab stop, paragraph margins, tables, pictures. Its text colour, family
   and base leading are the page's (theme, reading font, line height setting). */
.fast-doc-view-block-text.word-document {
  /* a floating picture stays inside its own block (rows are laid out one by one, windowed) */
  display: flow-root;
  color: inherit;
  font-family: inherit;
  line-height: inherit;
}
.fast-doc-view-scroller :deep(.windowed-scroller-content) {
  padding-block-end: 24px;
}
/* An empty paragraph keeps one line, as in Word (and an empty block is one line too): a
   zero-height row could never be scrolled into view, so the document's last rows - often
   empty - looked cut off at the end. */
/* note references in the text: clickable (they open their note in the notes pane) */
.fast-doc-view-block :deep(.note-reference) {
  cursor: pointer;
  color: var(--accent-color);
}
.fast-doc-view-content :deep(.note-reference--current) {
  background: color-mix(in srgb, var(--accent-color) 25%, transparent);
  border-radius: 2px;
}
.fast-doc-view-block-text:empty::before,
.fast-doc-view-block :deep(:is(p, h1, h2, h3, h4, h5, h6):empty::before) {
  content: '\200b';
}
/* A text file's line can be an empty run (<div class="text-line"><b></b></div>), which
   :empty does not catch: a zero-width space at its end gives it a line's height and adds
   nothing to a line that has text. */
.fast-doc-view-block :deep(.text-line::after) {
  content: '\200b';
}
/* The separator between the text and its notes: hairline + soft shadow cast onto the text
   (user-approved). The notes start 6px under it. The app sash's 8px grab band, hover / drag
   accent and cursor are unchanged. */
.fast-doc-view-notes-split > :deep(.sash-h) {
  height: 2px;
}
.fast-doc-view-notes-split > :deep(.sash-v) {
  width: 2px;
}
/* a border-coloured hairline plus a soft shadow falling from the notes onto the text */
.fast-doc-view-notes-split > :deep(.sash-h)::after {
  height: 1px;
  background: var(--border-color);
  box-shadow: 0 -2px 6px color-mix(in srgb, var(--text-primary) 14%, transparent);
  z-index: 1;
}
.fast-doc-view-notes-split > :deep(.sash-v)::after {
  width: 1px;
  background: var(--border-color);
  box-shadow: 0 0 6px color-mix(in srgb, var(--text-primary) 14%, transparent);
  z-index: 1;
}
.fast-doc-view-notes-split > :deep(.sash-h:hover)::after,
.fast-doc-view-notes-split > :deep(.sash-h:active)::after {
  height: 4px;
  background: color-mix(in srgb, var(--accent-color) 50%, transparent);
}
.fast-doc-view-notes-split > :deep(.sash-v:hover)::after,
.fast-doc-view-notes-split > :deep(.sash-v:active)::after {
  width: 4px;
  background: color-mix(in srgb, var(--accent-color) 50%, transparent);
}
</style>
