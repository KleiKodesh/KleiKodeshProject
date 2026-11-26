<script setup lang="ts">
import { computed, nextTick, ref, watch, onMounted, onBeforeUnmount } from 'vue'
import { storeToRefs } from 'pinia'
import ReadingSearchBar from '@/components/ReadingSearchBar.vue'
import PdfFindOptions from './PdfFindOptions.vue'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import PdfReaderToolbar from './PdfReaderToolbar.vue'
import { usePdfScroller } from './usePdfScroller'
import { disposeThumbnailEngine, thumbnailEngineFor } from './pdfThumbnailEngine'
import { usePdfZoomInput } from './usePdfZoomInput'
import { usePdfHandTool } from './usePdfHandTool'
import { usePdfPresentation } from './usePdfPresentation'
import PdfPropertiesDialog from './PdfPropertiesDialog.vue'
import PdfPrintChoiceDialog from './PdfPrintChoiceDialog.vue'
import AlertDialog from '@/components/AlertDialog.vue'
import PdfOrganizerWindow from '@/features/pdf-organizer/PdfOrganizerWindow.vue'
import PdfEditToolbar from './PdfEditToolbar.vue'
import PdfEditDialogs from './PdfEditDialogs.vue'
import { usePdfEditing } from './usePdfEditing'
import { usePdfNotes } from './usePdfNotes'
import PdfNotesOverlay from './PdfNotesOverlay.vue'
import SidePanel from '@/components/SidePanel.vue'
import { useSidePanelLayout } from '@/composables/useSidePanelLayout'
import { disposeLocalFileHost, openUrlInSystemBrowser, restoreLocalFile } from '@/webview-host/bridge'
import { useTabStore } from '@/stores/tabStore'
import type { PdfOutlineEntry as BridgeOutlineEntry } from '@/stores/bookViewStore'
import { usePdfSave } from './usePdfSave'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import PdfReaderToc from './PdfReaderToc.vue'
import PdfReaderSideModes, { type PdfSideMode } from './PdfReaderSideModes.vue'
import PdfReaderBookmarks from './PdfReaderBookmarks.vue'
import PdfReaderComments from './PdfReaderComments.vue'
import PdfReaderThumbnails from './PdfReaderThumbnails.vue'
import { usePdfComments, type PdfComment } from './usePdfComments'
import { pdfBookmarksKey, type BookBookmark } from '@/stores/bookBookmarks'
import { usePdfOutline, type PdfOutlineNode } from './usePdfOutline'
import { usePdfOutlineEdit } from './usePdfOutlineEdit'
import { usePdfReaderOcr } from './usePdfReaderOcr'
import PdfReaderOcrBar from './PdfReaderOcrBar.vue'
import PdfOcrResultPopup from './PdfOcrResultPopup.vue'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import { t } from '@/i18n'
import { showToast } from '@/composables/useToast'
import ContextMenu from '@/components/ContextMenu.vue'
import SelectionToolbar from '@/components/SelectionToolbar.vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useHostSearchStore } from '@/stores/hostSearchStore'
import { AnnotationEditorType } from './usePdfEditing'
import { usePdfReaderOutput } from './usePdfReaderOutput'
import { usePdfReaderContextMenu } from './usePdfReaderContextMenu'
import { usePdfReaderShortcuts } from './usePdfReaderShortcuts'
import { FindState, ScrollMode, SpreadMode, type ScalePreset } from './usePdfScroller'
import { usePdfExternalLinks } from './usePdfExternalLinks'
import PdfPasswordDialog from './PdfPasswordDialog.vue'
import { usePdfSelectionActions } from './usePdfSelectionActions'
import type { OcrScript } from './pdfOcrTypes'
import { findOutlineItem, type OutlineItemLike } from './findOutlineItem'

/** The new PDF view: PDF.js's headless scroller with our own toolbar and the book view's search bar. */
const props = defineProps<{
  url: string
  fileName?: string | null
  filePath?: string | null
  /** Opens at the outline entry matching this breadcrumb path (a section, "A · B"), when given. */
  outlineTarget?: string | null
  /** The /pdf-view tab showing this view: registers it for the title bar and the close / leave guards. */
  tabId?: string | null
}>()

const container = ref<HTMLDivElement | null>(null)
const pdf = usePdfScroller(container)
// Page thumbnails (panel and organizer) share one engine per document: it renders behind this
// view's pages and takes their drawn canvases; a closed document's thumbnails are freed.
let detachThumbnails: (() => void) | null = null
watch([pdf.pdfDocument, pdf.viewer], ([doc, viewer], before) => {
  detachThumbnails?.()
  detachThumbnails = null
  if (before && before[0] && before[0] !== doc) disposeThumbnailEngine(before[0])
  if (doc && viewer) detachThumbnails = thumbnailEngineFor(doc).attachViewer(viewer, pdf.eventBus)
}, { immediate: true })
onBeforeUnmount(() => {
  detachThumbnails?.()
  disposeThumbnailEngine(pdf.pdfDocument.value)
})
const root = ref<HTMLDivElement | null>(null)
usePdfZoomInput(root, container, pdf)
const handTool = usePdfHandTool(container)
// Links in the pages: web links to the system browser, never this page.
usePdfExternalLinks(container)
const bodyEl = ref<HTMLDivElement | null>(null)
const presentation = usePdfPresentation(bodyEl, pdf)
pdf.viewHistory.setSuspended(() => presentation.active.value)
const showProperties = ref(false)
// OCR: drag a box over the pages to read its text (script / force choices in the shared pdfOcrStore).
const ocr = usePdfReaderOcr(container, pdf)
const pdfOcrStore = usePdfOcrStore()
// Editing bar: shown by the toolbar's edit toggle; hiding it leaves the active editor.
const editing = usePdfEditing(pdf)
const editBarVisible = ref(false)
// Notes: speech bubbles on the pages, saved in the file as sticky notes (usePdfNotes).
const notes = usePdfNotes(pdf, container)
/** "Add note" (main toolbar, Alt+N, the more menu): note mode on / off - a click on a page places one. */
function onAddNoteButton() {
  notes.toggleNoteMode()
}
function toggleEditBar() {
  editBarVisible.value = !editBarVisible.value
  if (!editBarVisible.value) editing.stopEditing()
}
// The floating "highlight" button on a selection switches PDF.js to the highlight editor:
// show the editing bar with it, so the mode is visible and can be left (Esc / the bar).
watch(editing.mode, (mode) => {
  if (mode === AnnotationEditorType.NONE || editBarVisible.value) return
  editBarVisible.value = true
})

// The book view's toolbar setting: position, and shown / hidden (Ctrl+B, the title bar's toolbar button).
// Toolbar shown/hidden follows the reading toolbars' toggle (Ctrl+B, F9); its EDGE is the PDF viewer's own.
const { toolbarVisible } = storeToRefs(useBookViewStore())
const { pdfToolbarPosition: toolbarPosition } = storeToRefs(useSettingsStore())

// Table of contents: the PDF's outline in the book view's TOC side panel (inline with a sash
// on a wide page, overlay on a narrow one - shared useSidePanelLayout + SidePanel).
const outline = usePdfOutline(pdf.pdfDocument, pdf.pageNumber)
// Outline editing; outlineEdit.buildSaveOutline() is what the save passes as saveDocument({ outline }).
const outlineEdit = usePdfOutlineEdit(outline, pdf.pdfDocument, pdf.pageNumber)
const tocVisible = ref(false)
// The page's width at its zoom with nothing beside it. A fit preset (page width, auto, fit page)
// re-fits to whatever room is left, so it is only read while the panel is not inline; a free
// zoom keeps its size, so it is read always.
const PAGE_SIDE_MARGIN = 16
const fullPageWidth = ref(0)
pdf.eventBus.on('scalechanging', () => {
  if (sidePanelIsOverlay.value || !tocVisible.value || pdf.preset.value === null) {
    const view = pdf.viewer.value?.getPageView(pdf.pageNumber.value - 1) as { width?: number } | undefined
    if (view?.width) fullPageWidth.value = view.width
  }
})
pdf.eventBus.on('pagesloaded', () => {
  const view = pdf.viewer.value?.getPageView(pdf.pageNumber.value - 1) as { width?: number } | undefined
  if (view?.width) fullPageWidth.value = view.width
})
const tocToggleButton = ref<HTMLElement | null>(null)
const sidePanelResizeArea = ref<HTMLElement | null>(null)
const {
  isOverlay: sidePanelIsOverlay,
  panelStyle: sidePanelStyle,
  onDividerPointerDown: onSidePanelDividerPointerDown,
  onResizePointerMove: onSidePanelResizePointerMove,
  onResizePointerUp: onSidePanelResizePointerUp,
} = useSidePanelLayout({
  pageElement: root,
  resizeArea: sidePanelResizeArea,
  // Push the pages aside only when the page still shows at its full width beside the panel;
  // otherwise the panel floats over them.
  inlineWhen: (width, panelWidth) => width - panelWidth - 6 >= fullPageWidth.value + 2 * PAGE_SIDE_MARGIN,
})
// The panel's mode (outline, bookmarks, comments, thumbnails); kept while the tab lives.
// Until the reader picks a mode, a document without an outline opens on its thumbnails.
const sideMode = ref<PdfSideMode>('outline')
let sideModeChosen = false
const onSideModeChosen = () => { sideModeChosen = true }
const hasOutline = () => outline.nodes.value.length > 0 || outlineEdit.canEdit.value
watch(tocVisible, (open) => {
  if (open && !sideModeChosen) sideMode.value = hasOutline() ? 'outline' : 'thumbnails'
})
// The panel's open state and mode are part of the file's saved view (usePdfViewHistory).
pdf.viewHistory.onRestoreSidePanel((open, mode) => {
  if (mode === 'outline' || mode === 'bookmarks' || mode === 'comments' || mode === 'thumbnails') {
    sideMode.value = mode
    sideModeChosen = true
  }
  tocVisible.value = open
})
watch([tocVisible, sideMode], ([open, mode]) => pdf.viewHistory.saveSidePanel(open, mode), { flush: 'sync' })
const comments = usePdfComments(pdf, computed(() => tocVisible.value && sideMode.value === 'comments'), notes)
const bookmarksOwner = computed(() => pdfBookmarksKey(props.filePath || props.url))
/** The top of the view: page and the y there in PDF units (PDF.js's view location). */
let viewTop: { pageNumber: number; top?: number } | null = null
pdf.eventBus.on('updateviewarea', (e: { location?: { pageNumber: number; top?: number } }) => {
  if (e.location) viewTop = { pageNumber: e.location.pageNumber, top: e.location.top }
})
const readViewTop = () => viewTop ?? (pdf.pdfDocument.value ? { pageNumber: pdf.pageNumber.value } : null)
/** Scroll so PDF point (left, top) on the page is at the top-left of the view, keeping the zoom. */
function goToPoint(pageNumber: number, left: number | null, top: number | null) {
  const v = pdf.viewer.value
  if (!v) return
  pdf.jumpHistory.pushCurrentPosition()
  if (top == null) pdf.goToPage(pageNumber)
  else v.scrollPageIntoView({ pageNumber, destArray: [null, { name: 'XYZ' }, left, top, null] })
}
function closePanelIfOverlay() {
  if (sidePanelIsOverlay.value) tocVisible.value = false
}
function onBookmarkSelect(bookmark: BookBookmark) {
  goToPoint(bookmark.lineId, null, bookmark.pdfTop ?? null)
  closePanelIfOverlay()
}
async function onCommentSelect(c: PdfComment) {
  // A little above the annotation, so it is not flush against the top edge.
  goToPoint(c.pageIndex + 1, null, Math.max(c.rect[1] ?? 0, c.rect[3] ?? 0) + 20)
  closePanelIfOverlay()
  // A note: its card, pinned, once its page is drawn.
  if (c.noteKey) return notes.pin(c.noteKey)
  // Then its popup, pinned (PDF.js's comment list): an editor's through PDF.js, a file
  // annotation's through its comment button on the page.
  const managers = editing.managers
  await managers.waitForEditorsRendered(c.pageIndex + 1)
  if (c.uid) return managers.selectComment(c.pageIndex, c.uid)
  const page = container.value?.querySelector(`.page[data-page-number="${c.pageIndex + 1}"]`)
  const annotation = page?.querySelector(`.annotationLayer [data-annotation-id="${CSS.escape(c.key)}"]`)
  const button = annotation?.nextElementSibling
  if (button?.classList.contains('annotationCommentButton')) (button as HTMLElement).click()
}
function onThumbnailSelect(n: number) {
  pdf.jumpToPage(n)
}
function toggleToc(toggleButton?: HTMLElement | null) {
  if (toggleButton) tocToggleButton.value = toggleButton
  tocVisible.value = !tocVisible.value
}
function onOutlineSelect(node: PdfOutlineNode) {
  if (!goToOutlineNode(node)) return
  if (sidePanelIsOverlay.value) tocVisible.value = false
}
/** Shows an outline entry's place; false when it has none. */
function goToOutlineNode(node: PdfOutlineNode): boolean {
  if (node.dest) void pdf.goToDestination(node.dest)
  else if (node.url) void openUrlInSystemBrowser(node.url)
  else if (node.pageIndex != null) {
    pdf.jumpHistory.pushCurrentPosition()
    pdf.goToPage(node.pageIndex + 1)
  } else return false
  return true
}
/**
 * Ctrl+Left / Ctrl+Right: the next / previous outline entry from the current one, as the book
 * view's section keys. Entries that only open a web link are skipped.
 */
function goToAdjacentSection(direction: 'next' | 'previous') {
  const rows = outline.nodes.value.filter((node) => node.dest != null || node.pageIndex != null)
  if (!rows.length) return
  const current = rows.findIndex((node) => node.id === outline.activeNodeId.value)
  const target = direction === 'next'
    ? rows[current + 1]
    : current > 0 ? rows[current - 1] : undefined
  if (target) goToOutlineNode(target)
}

// One-shot jump to `outlineTarget` once the first document's initial view is set: it wins over
// the saved place (the saved zoom and modes still apply).
if (props.outlineTarget) {
  const target = props.outlineTarget
  pdf.eventBus.on('documentinit', () => {
    const doc = pdf.pdfDocument.value
    void doc?.getOutline().then((items) => {
      const item = items && doc === pdf.pdfDocument.value ? findOutlineItem(items as unknown as OutlineItemLike[], target) : null
      // The tab opens here: not a jump to come back from.
      if (item?.dest) void pdf.goToDestination(item.dest).then(() => pdf.jumpHistory.reset())
    })
  }, { once: true })
}
onMounted(() => void pdf.open(props.url, undefined, !!props.outlineTarget))
// A Save As re-points the tab at the new file (new url) while this view already shows its bytes.
let adoptedUrl: string | null = null
watch(() => props.url, (url) => {
  if (url === adoptedUrl) return
  void pdf.open(url)
})

// Organize pages: its own window (features/pdf-organizer), on this tab's document; a save reloads it here.
const organizerOpen = ref(false)
const organizerWindow = ref<InstanceType<typeof PdfOrganizerWindow> | null>(null)
function toggleOrganizer() {
  if (organizerOpen.value) organizerWindow.value?.requestClose()
  else organizerOpen.value = true
}
function onOrganizerSaved(bytes: Uint8Array) {
  void pdf.open(bytes, pdf.viewState())
}

// Search: the book view's ReadingSearchBar, driving PDF.js's find controller.
const searchVisible = ref(false)
const query = ref('')
function onQueryChange(q: string) {
  query.value = q
  if (q.trim()) pdf.find(q)
  else pdf.clearFind()
}
function closeSearch() {
  searchVisible.value = false
  pdf.clearFind()
  container.value?.focus({ preventScroll: true })
}
function toggleSearch() {
  if (searchVisible.value) closeSearch()
  else searchVisible.value = true
}
/** Ctrl+F: open the bar (or refocus it) with the text selected in the pages, when there is one. */
function openSearchWithSelection() {
  const selected = contextMenu.selectedText().trim().replace(/\s+/g, ' ')
  if (selected && selected.length <= 200 && selected !== query.value) onQueryChange(selected)
  if (searchVisible.value) root.value?.querySelector<HTMLInputElement>('.search-input')?.select()
  searchVisible.value = true
}
/** F3 / Shift+F3: the next / previous match of the current query. */
function findAgain(previous: boolean): boolean {
  if (!searchVisible.value || !query.value.trim()) return false
  pdf.find(query.value, true, previous)
  return true
}
/** Why the document did not open, in the reader's words (pdfLoadErrors). */
const loadFailureText = computed(() => {
  const f = pdf.loadFailure.value
  if (!f) return ''
  if (f.kind === 'invalid') return t('pdfReader.load.invalid')
  if (f.kind === 'missing') return t('pdfReader.load.missing')
  if (f.kind === 'response') return t('pdfReader.load.response', { status: f.status ?? '' })
  if (f.kind === 'password') return t('pdfReader.load.passwordCancelled')
  return t('pdfReader.load.unknown', { message: f.message })
})
/** PDF.js's find state as a note in the search bar: nothing found, or the search wrapped. */
const findStatus = computed<{ text: string; danger: boolean } | null>(() => {
  if (!query.value.trim()) return null
  if (pdf.findState.value === FindState.NOT_FOUND) return { text: t('pdfReader.findState.notFound'), danger: true }
  if (pdf.findState.value === FindState.WRAPPED) {
    return { text: t(pdf.findWrappedPrevious.value ? 'pdfReader.findState.wrappedTop' : 'pdfReader.findState.wrappedBottom'), danger: false }
  }
  return null
})

// Right-click / long-press menu, print, save and the keyboard - the old PDF viewer's.
const paneNavigation = usePaneNavigation()
const output = usePdfReaderOutput(pdf, () => props.fileName ?? null)

// Save (in place) / Save As, and the document's unsaved state (usePdfSave).
const tabStore = useTabStore()
const bookViewStore = useBookViewStore()
const saving = usePdfSave(pdf, outlineEdit, notes, {
  filePath: () => props.filePath ?? null,
  fileName: () => props.fileName ?? null,
  url: () => props.url ?? null,
  onSavedAs: async (filePath, fileName) => {
    if (!props.tabId) return
    const old = props.filePath
    const restored = await restoreLocalFile(filePath)
    adoptedUrl = restored?.url ?? null
    tabStore.updateTabWithoutHistory(props.tabId, {
      localFilePath: filePath,
      localFileName: fileName,
      title: fileName.replace(/\.pdf$/i, ''),
      localFileHbBookId: undefined,
      localFileHbBookTitle: undefined,
      ...(restored?.url ? { localFileVirtualUrl: restored.url } : {}),
    })
    if (old && old !== filePath) disposeLocalFileHost(old)
  },
})

// The title bar's section path (tab.tocPath) and its outline dropdowns, from this view's outline.
const OUTLINE_SEPARATOR = ' · '
const outlineEntries = computed<BridgeOutlineEntry[]>(() => {
  const pathById = new Map<number, string>()
  return outline.nodes.value.map((node) => {
    const parentPath = node.parentId == null ? '' : (pathById.get(node.parentId as number) ?? '')
    const fullPath = parentPath ? parentPath + OUTLINE_SEPARATOR + node.text : node.text
    pathById.set(node.id as number, fullPath)
    return { id: node.id as number, text: node.text, fullPath, parentPath }
  })
})
if (props.tabId) {
  const tabId = props.tabId
  // No outline: "page X of Y" (the page label when the document has them), as the old viewer.
  watch([outline.activeNodeId, outlineEntries, pdf.pageNumber, pdf.pageCount, pdf.pageLabels], ([id]) => {
    const tab = tabStore.tabs.find((t) => t.id === tabId)
    if (!tab || tab.route !== '/pdf-view') return
    const tocPath = outlineEntries.value.length
      ? outlineEntries.value.find((e) => e.id === id)?.fullPath
      : pdf.pageCount.value
        ? t('pdfReader.pageOfTitle', { page: pdf.pageLabel(pdf.pageNumber.value), count: pdf.pageCount.value })
        : undefined
    if (tab.tocPath !== tocPath) tabStore.updateTabWithoutHistory(tabId, { tocPath })
  })
  bookViewStore.registerPdfBridge(tabId, {
    get outlineEntries() { return outlineEntries.value },
    navigateToEntry: (entry) => {
      const node = outline.nodes.value.find((n) => n.id === entry.id)
      if (node) onOutlineSelect(node)
    },
    hasUnsavedChanges: () => saving.dirty.value,
    save: () => (props.filePath ? saving.save() : saving.saveAs()),
  })
  onBeforeUnmount(() => bookViewStore.unregisterPdfBridge(tabId))
}

// Print: only the file on disk, through the app host's built-in PDF print (system dialog, full quality).
// Edits not saved in the file: the user saves first or prints the file as it is on disk.
// No host (dev browser) or no file on disk: a message - there is no in-page print.
const printChoiceOpen = ref(false)
// Print the FILE natively, the same way in the app (WebView2) and in the dev browser: the file's
// bytes (as loaded / last saved - no unsaved marks) in a hidden frame, printed by the browser's own
// PDF engine with its print dialog. A blob address is same-origin, so the page may tell it to print
// (the file's own served address is another origin in the app).
const canPrintFile = () => !!pdf.pdfDocument.value
// Captured during setup: the window this viewer is drawn in (a detached window prints there).
const printHostWindow = useHostWindow()
async function printFile() {
  const doc = pdf.pdfDocument.value
  if (!doc) return showToast(t('pdfReader.printNoFile'))
  const win = printHostWindow
  const bytes = await doc.getData()
  const blobUrl = win.URL.createObjectURL(new win.Blob([bytes as Uint8Array<ArrayBuffer>], { type: 'application/pdf' }))
  const frame = win.document.createElement('iframe')
  frame.style.cssText = 'position:fixed;width:0;height:0;border:0;visibility:hidden'
  frame.src = blobUrl
  const cleanup = () => {
    frame.remove()
    win.URL.revokeObjectURL(blobUrl)
  }
  frame.addEventListener('load', () => {
    try {
      frame.contentWindow?.focus()
      frame.contentWindow?.print()
    } catch {
      showToast(t('pdfReader.printFailed'))
      return cleanup()
    }
    // Removed once the reader is back (the print dialog blocks until then).
    win.addEventListener('focus', cleanup, { once: true })
  }, { once: true })
  win.document.body.appendChild(frame)
}
async function printDocument() {
  const doc = pdf.pdfDocument.value
  if (!doc) return
  if (!canPrintFile()) return printFile()
  if (doc.annotationStorage.size > 0 || saving.dirty.value) printChoiceOpen.value = true
  else await printFile()
}
async function onPrintChoice(choice: 'saveAndPrint' | 'withoutChanges' | 'cancel') {
  printChoiceOpen.value = false
  if (choice === 'saveAndPrint') {
    if (await saving.save()) await printFile()
  } else if (choice === 'withoutChanges') void printFile()
}
const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
// Highlight mode in a detached window (usePdfSelectionActions).
usePdfSelectionActions({
  container,
  eventBus: pdf.eventBus,
  highlightMode: () => editing.mode.value === AnnotationEditorType.HIGHLIGHT,
})
const contextMenu = usePdfReaderContextMenu(container, contextMenuRef, {
  isBlocked: () => ocr.isActive.value || editing.mode.value !== AnnotationEditorType.NONE,
  copyPageAsImage: () => void output.copyPageAsImage(),
  searchInLibrary: (q) => paneNavigation.openBookTarget({ route: '/search', title: t('bookView.copy.searchTabTitle', { query: q }), searchQuery: q }),
  searchInCatalog: (q) => useHostSearchStore().openCatalogSearch(q),
  noteAtSelection: notes.noteAtSelection,
})
usePdfReaderShortcuts(root, container, pdf, {
  undoRedo: (redo) => {
    const pdfJsHasKeys = editing.mode.value !== AnnotationEditorType.NONE && root.value?.ownerDocument === document
    if (pdfJsHasKeys) return false
    if (redo) editing.redo()
    else editing.undo()
    return true
  },
  inPresentation: () => presentation.active.value,
  openSearch: openSearchWithSelection,
  findAgain,
  closeSearch: () => (searchVisible.value ? (closeSearch(), true) : false),
  toggleToc: () => { if (pdf.pdfDocument.value) toggleToc() },
  presentation: () => void presentation.start(),
  print: () => void printDocument(),
  save: () => void (saving.canSave.value ? saving.save() : props.filePath ? undefined : saving.saveAs()),
  saveAs: () => void saving.saveAs(),
  setHandTool: (on) => { if (handTool.enabled.value !== on) handTool.toggle() },
  selectedText: contextMenu.selectedText,
  copyIntoWord: () => contextMenu.copyIntoWord(),
  searchSelection: () => contextMenu.searchSelection(),
  toggleEdit: toggleEditBar,
  closeEditBar: () => (editBarVisible.value && editing.mode.value === AnnotationEditorType.NONE ? (toggleEditBar(), true) : false),
  editVisible: () => editBarVisible.value,
  editTool: (tool) => {
    if (tool === 'image') editing.addImage()
    else if (tool === 'note') onAddNoteButton()
    else editing.setMode({ highlight: AnnotationEditorType.HIGHLIGHT, text: AnnotationEditorType.FREETEXT, draw: AnnotationEditorType.INK, signature: AnnotationEditorType.SIGNATURE }[tool])
  },
  toggleOcr: () => ocr.toggle(),
  ocrActive: () => ocr.isActive.value,
  cycleOcrScript: () => pdfOcrStore.setScript(cycle<OcrScript>(['hebrew', 'rashi', 'mixed', 'english'], pdfOcrStore.script)),
  toggleOcrForce: () => pdfOcrStore.toggleSkipExistingText(),
  searchVisible: () => searchVisible.value,
  toggleFindOption: (option) => {
    pdf.findOptions.value = { ...pdf.findOptions.value, [option]: !pdf.findOptions.value[option] }
    if (query.value.trim()) pdf.find(query.value)
  },
  // The toolbar dropdowns' order (PdfReaderToolbar).
  cycleFit: () => pdf.setPreset(cycle<ScalePreset>(['page-width', 'auto', 'page-actual', 'page-fit'], pdf.preset.value)),
  cycleScroll: () => pdf.setScrollMode(cycle([ScrollMode.VERTICAL, ScrollMode.PAGE, ScrollMode.HORIZONTAL, ScrollMode.WRAPPED], pdf.scrollMode.value)),
  cycleSpread: () => pdf.setSpreadMode(cycle([SpreadMode.NONE, SpreadMode.ODD, SpreadMode.EVEN], pdf.spreadMode.value)),
  toggleOrganizer: () => { if (pdf.pdfDocument.value) toggleOrganizer() },
  properties: () => { if (pdf.pdfDocument.value) showProperties.value = true },
  addBookmark: () => {
    if (!pdf.pdfDocument.value) return
    showSideMode('bookmarks')
    void nextTick(() => bookmarksPanel.value?.add())
  },
  showSideMode,
  adjacentSection: goToAdjacentSection,
})
/** The option after `current` in `options` (the first when `current` is not among them). */
function cycle<V>(options: V[], current: V | null): V {
  return options[(options.indexOf(current as V) + 1) % options.length] as V
}
const bookmarksPanel = ref<InstanceType<typeof PdfReaderBookmarks> | null>(null)
/** Open the side panel on a mode (Alt+1..4); the mode counts as the reader's choice. */
function showSideMode(mode: PdfSideMode) {
  if (!pdf.pdfDocument.value) return
  sideMode.value = mode
  onSideModeChosen()
  tocVisible.value = true
}
</script>

<template>
  <div ref="root" class="pdf-reader" :class="`toolbar-at-${toolbarPosition}`" data-local-history>
    <PdfReaderToolbar
      v-if="toolbarVisible"
      :page-number="pdf.pageNumber.value"
      :page-count="pdf.pageCount.value"
      :page-label="pdf.pageLabels.value ? pdf.pageLabel(pdf.pageNumber.value) : null"
      :scale="pdf.scale.value"
      :preset="pdf.preset.value"
      :search-visible="searchVisible"
      :hand-tool="handTool.enabled.value"
      :edit-visible="editBarVisible"
      :note-active="notes.noteMode.value"
      :ocr-active="ocr.isActive.value"
      :scroll-mode="pdf.scrollMode.value"
      :spread-mode="pdf.spreadMode.value"
      @go-to-page="pdf.goToPage"
      @jump-to-page="pdf.jumpToPage"
      @go-to-page-text="pdf.goToPageLabel"
      @zoom-in="pdf.zoomIn"
      @zoom-out="pdf.zoomOut"
      @set-zoom="pdf.setScale($event / 100)"
      @set-preset="pdf.setPreset"
      @rotate="pdf.rotate"
      @toggle-search="toggleSearch"
      @toggle-hand-tool="handTool.toggle"
      @set-scroll-mode="pdf.setScrollMode"
      @set-spread-mode="pdf.setSpreadMode"
      @presentation="presentation.start"
      @properties="showProperties = true"
      @print="printDocument"
      @toggle-edit="toggleEditBar"
      @add-note="onAddNoteButton"
      @toggle-ocr="ocr.toggle"
      :organizer-open="organizerOpen"
      @toggle-organizer="toggleOrganizer"
      :toc-visible="tocVisible"
      :has-outline="pdf.pdfDocument.value != null"
      @toggle-toc="toggleToc"
      :can-save="saving.canSave.value"
      :dirty="saving.dirty.value"
      @save="saving.save"
      @save-as="saving.saveAs"
    />
    <div
      ref="sidePanelResizeArea"
      class="pdf-reader-row"
      @pointermove="onSidePanelResizePointerMove"
      @pointerup="onSidePanelResizePointerUp"
    >
    <SidePanel
      v-if="tocVisible"
      :toggle-button-el="tocToggleButton"
      :is-overlay="sidePanelIsOverlay"
      class="pdf-reader-toc"
      :style="sidePanelIsOverlay ? undefined : sidePanelStyle"
      @close="tocVisible = false"
    >
      <div class="pdf-side">
        <PdfReaderSideModes v-model="sideMode" @update:model-value="onSideModeChosen" />
        <template v-if="sideMode === 'outline'">
          <PdfReaderToc v-if="hasOutline()" :outline="outline.nodes.value" :active-node-id="outline.activeNodeId.value" :edit="outlineEdit" @select="onOutlineSelect" />
          <div v-else class="pdf-side-empty">{{ $t('pdfReader.panel.noOutline') }}</div>
        </template>
        <PdfReaderBookmarks
          v-else-if="sideMode === 'bookmarks'"
          ref="bookmarksPanel"
          :owner="bookmarksOwner"
          :page-number="pdf.pageNumber.value"
          :read-location="readViewTop"
          :label-of="pdf.pageLabel"
          @select="onBookmarkSelect"
        />
        <PdfReaderComments
          v-else-if="sideMode === 'comments'"
          :comments="comments.comments.value"
          :loading="comments.loading.value"
          :label-of="pdf.pageLabel"
          @select="onCommentSelect"
        />
        <PdfReaderThumbnails
          v-else-if="pdf.pdfDocument.value"
          :document="pdf.pdfDocument.value"
          :page-number="pdf.pageNumber.value"
          :rotation="pdf.rotation.value"
          :label-of="pdf.pageLabel"
          @select="onThumbnailSelect"
        />
      </div>
    </SidePanel>
    <div v-if="tocVisible && !sidePanelIsOverlay" class="sash sash-v" @pointerdown="onSidePanelDividerPointerDown" />
    <div ref="bodyEl" class="pdf-reader-body" tabindex="-1">
      <PdfEditToolbar v-if="editBarVisible" :editing="editing" @close="toggleEditBar" />
      <PdfEditDialogs :managers="pdf.editManagers" :scroller="pdf.viewer.value?.container ?? null" />
      <PdfNotesOverlay :notes="notes" />
      <ReadingSearchBar
        history-key="pdf-reader-search"
        :visible="searchVisible"
        toolbar-position="top"
        :match-count="query.trim() ? pdf.matchCount.value : -1"
        :current-match="Math.max(0, pdf.currentMatch.value - 1)"
        :status-message="findStatus?.text ?? null"
        :status-danger="findStatus?.danger ?? false"
        :commentary-visible="false"
        :open-commentary-slots="[]"
        mode="content"
        :query="query"
        @close="closeSearch"
        @query-change="onQueryChange"
        @next="pdf.find(query, true, false)"
        @prev="pdf.find(query, true, true)"
      >
        <template #options>
          <PdfFindOptions v-model="pdf.findOptions.value" @update:model-value="query.trim() && pdf.find(query)" />
        </template>
      </ReadingSearchBar>
      <!-- PDFViewer needs an absolutely positioned scroll container holding a .pdfViewer element. -->
      <div
        ref="container"
        class="pdf-reader-scroller"
        :class="{ 'hand-tool': handTool.enabled.value, grabbing: handTool.dragging.value, 'ocr-mode': ocr.isActive.value, 'ocr-drawing': ocr.isDrawing.value }"
        tabindex="-1"
      >
        <div class="pdfViewer" />
      </div>
      <PdfReaderOcrBar :ocr="ocr" />
      <div v-if="pdf.loading.value && !pdf.loadFailure.value" class="pdf-reader-progress" :class="{ indeterminate: Number.isNaN(pdf.loadProgress.value) }" role="progressbar" :aria-label="$t('pdfReader.load.progress')">
        <div class="pdf-reader-progress-fill" :style="Number.isNaN(pdf.loadProgress.value) ? undefined : { width: `${pdf.loadProgress.value}%` }" />
      </div>
      <div v-if="pdf.loadFailure.value" class="pdf-reader-error" role="alert">
        <div class="pdf-reader-error-box">
          <p class="pdf-reader-error-text">{{ loadFailureText }}</p>
          <button type="button" class="pdf-reader-error-retry" @click="pdf.retryOpen()">{{ $t('pdfReader.load.retry') }}</button>
        </div>
      </div>
    </div>
    </div>
    <PdfOcrResultPopup
      v-if="ocr.result.value"
      :result="ocr.result.value"
      :script="pdfOcrStore.script"
      :is-processing="ocr.isProcessing.value"
      :processing-progress="ocr.processingProgress.value"
      @dismiss="ocr.dismissResult"
      @update:script="pdfOcrStore.setScript"
    />
    <ContextMenu ref="contextMenuRef" :items="contextMenu.items.value" />
    <SelectionToolbar
      :container="container"
      :items="contextMenu.items.value"
      :menu="contextMenuRef"
      :is-blocked="() => ocr.isActive.value || editing.mode.value !== AnnotationEditorType.NONE"
      :prepare="contextMenu.prepareForSelection"
    />
    <PdfPasswordDialog
      v-if="pdf.passwordRequest.value"
      :incorrect="pdf.passwordRequest.value.incorrect"
      @submit="pdf.passwordRequest.value?.submit($event)"
      @cancel="pdf.passwordRequest.value?.cancel()"
    />
    <AlertDialog v-if="pdf.scriptAlerts.value.length" :message="pdf.scriptAlerts.value[0]!" @close="pdf.scriptAlerts.value = pdf.scriptAlerts.value.slice(1)" />
    <PdfPrintChoiceDialog v-if="printChoiceOpen" :can-save="saving.canSave.value" @choose="onPrintChoice" />
    <PdfPropertiesDialog
      v-if="showProperties && pdf.pdfDocument.value"
      :document="pdf.pdfDocument.value"
      :file-name="fileName ?? null"
      :file-path="filePath ?? null"
      @close="showProperties = false"
    />
    <PdfOrganizerWindow
      v-if="organizerOpen && pdf.pdfDocument.value"
      ref="organizerWindow"
      :document="pdf.pdfDocument.value"
      :file-name="fileName ?? null"
      :file-path="filePath ?? null"
      @close="organizerOpen = false"
      @saved="onOrganizerSaved"
    />
  </div>
</template>

<style scoped>
.pdf-reader {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  min-height: 0;
}
.pdf-reader.toolbar-at-bottom { flex-direction: column-reverse; }
.pdf-reader.toolbar-at-right { flex-direction: row; }
.pdf-reader.toolbar-at-left { flex-direction: row-reverse; }
.pdf-reader-row {
  position: relative;
  display: flex;
  flex: 1;
  min-width: 0;
  min-height: 0;
}
.pdf-reader-toc {
  min-width: 0;
}
.pdf-side {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  min-width: 170px;
}
.pdf-side-empty {
  padding: 10px;
  font-size: 12px;
  color: var(--text-secondary);
}
.pdf-reader-body {
  position: relative;
  flex: 1;
  min-width: 0;
  min-height: 0;
}
.pdf-reader-scroller {
  position: absolute;
  inset: 0;
  overflow: auto;
  touch-action: pan-x pan-y;
  outline: none;
  background: var(--bg-secondary, #e8e8e8);
}
.pdf-reader-scroller.hand-tool {
  cursor: grab;
  user-select: none;
}
.pdf-reader-scroller.hand-tool :deep(.textLayer) {
  cursor: inherit;
}
.pdf-reader-scroller.grabbing {
  cursor: grabbing;
}
.pdf-reader-scroller.ocr-mode,
.pdf-reader-scroller.ocr-mode :deep(*) {
  cursor: crosshair;
  user-select: none;
}
.pdf-reader-scroller.ocr-drawing,
.pdf-reader-scroller.ocr-drawing :deep(*) {
  cursor: default;
}
/* OCR selection box (added by usePdfReaderOcr inside the scroller), as the old viewer's. */
.pdf-reader-scroller :deep(.pdf-reader-ocr-rect) {
  position: absolute;
  border: 2px dashed #0078d4;
  background: rgba(0, 120, 212, 0.12);
  pointer-events: none;
  z-index: 9000;
  box-shadow: 0 0 0 1px rgba(0, 120, 212, 0.3), inset 0 0 0 1px rgba(0, 120, 212, 0.2);
  border-radius: 2px;
  transition: box-shadow 100ms ease;
  box-sizing: content-box;
}
.pdf-reader-scroller :deep(.pdf-reader-ocr-rect.active) {
  box-shadow: 0 0 8px rgba(0, 120, 212, 0.4), inset 0 0 0 1px rgba(0, 120, 212, 0.3);
}
.pdf-reader-error {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  background: var(--bg-secondary, #e8e8e8);
}
.pdf-reader-error-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  max-width: 420px;
  padding: 16px;
  text-align: center;
}
.pdf-reader-error-text {
  margin: 0;
  font-size: 13px;
  color: var(--text-primary);
}
.pdf-reader-error-retry {
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  background: var(--control-bg);
  color: var(--text-primary);
  border-radius: 4px;
}
.pdf-reader-error-retry:hover {
  background: var(--control-bg-hover);
}
/* Loading progress: a thin bar at the top of the pages (indeterminate while the size is unknown). */
.pdf-reader-progress {
  position: absolute;
  top: 0;
  inset-inline: 0;
  height: 3px;
  overflow: hidden;
  z-index: 5;
  pointer-events: none;
}
.pdf-reader-progress-fill {
  height: 100%;
  width: 0;
  background: var(--accent-color, #0078d4);
  transition: width 150ms linear;
}
.pdf-reader-progress.indeterminate .pdf-reader-progress-fill {
  width: 30%;
  transition: none;
  animation: pdf-reader-progress-slide 1.2s ease-in-out infinite;
}
@keyframes pdf-reader-progress-slide {
  from { transform: translateX(-100%); }
  to { transform: translateX(340%); }
}
</style>
