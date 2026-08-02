import { defineStore } from 'pinia'
import { ref, reactive, computed, watch } from 'vue'
import { useTabStore } from './tabStore'
import { useSettingsStore } from './settingsStore'
import { lsGet, lsSet } from '@/utils/persistence'
import {
  ZOOM_CONFIG,
  zoomIn as zoomInUtil,
  zoomOut as zoomOutUtil,
  resetZoom as resetZoomUtil,
} from '@/composables/useZoom'
import type { TocEntry } from '@/webview-host/queries.types'

/** Disk names for the book-view UI state this store owns. Nothing else reads them. */
const KEYS = {
  SETTINGS_TOOLBAR: 'bookView.toolbarVisible',
  SETTINGS_TOOLBAR_POSITION: 'bookView.toolbarPosition',
  SETTINGS_AUTO_SELECT_TOP_LINE: 'bookView.autoSelectTopLine',
  SETTINGS_SPLIT_VIEW: 'splitView.enabled',
  SETTINGS_SPLIT_VIEW_FRACTION: 'splitView.fraction',
} as const

export type ToolbarPosition = 'top' | 'bottom' | 'left' | 'right'

/**
 * Bridge registered by each mounted book-view tab so the title bar can read
 * the TOC entry tree and navigate to a TOC entry without querying the database.
 * Never persisted — in-memory only; cleaned up on unmount.
 */
export interface TocBridge {
  tocEntries: TocEntry[]
  navigateToEntry: (entry: TocEntry) => void
}

/**
 * A single entry in the PDF outline, shaped for breadcrumb navigation.
 * Derived from the flat OutlineEntry list in usePdfViewPageTracking.
 */
export interface PdfOutlineEntry {
  id: number        // index in the flat outline list — stable unique key
  text: string      // the last path segment (leaf label)
  fullPath: string  // the full " · "-joined path, used to match the active tocPath
  parentPath: string // all segments except the last, or "" for root entries
}

/**
 * Bridge registered by each mounted PDF-view tab so the title bar can read
 * the outline tree and navigate to a page without querying the iframe directly.
 * Never persisted — in-memory only; cleaned up on unmount.
 */
export interface PdfBridge {
  outlineEntries: PdfOutlineEntry[]
  navigateToEntry: (entry: PdfOutlineEntry) => void
  /**
   * Live unsaved-changes state, read straight from the viewer's
   * PDFViewerApplication._hasChanges() (annotations + structural page edits +
   * outline edits). Only meaningful while the tab's iframe is mounted — for
   * background tabs use the parked snapshot in pdfEditStateByTabId instead.
   */
  hasUnsavedChanges?: () => boolean
}

/**
 * One serialized outline entry, as produced by the viewer's outline editor
 * (outline-search.js serializeOutlineDom). Page-level destinations.
 */
export interface PdfOutlineEditEntry {
  title: string
  page: number
  items: PdfOutlineEditEntry[]
  /**
   * Index into the document's ORIGINAL outline (BFS order). Present on rows
   * that came from the PDF; the save path uses it to preserve the original
   * destination/action/styling verbatim. Absent on user-created rows.
   */
  src?: number
}

/**
 * A parked snapshot of unsaved PDF outline edits for one tab.
 *
 * The PDF viewer lives in ONE iframe per pane rendering the ACTIVE tab, so
 * switching tabs destroys (or navigates) the viewer — a background PDF tab has
 * no live viewer to ask about unsaved state. The viewer therefore pushes its
 * edited outline out EAGERLY after every edit; this is where it lands. When
 * the tab becomes active again with the SAME file, the snapshot is rehydrated
 * into the viewer (setState) and editing continues seamlessly.
 *
 * In-memory only, never persisted: surviving an app restart would require
 * writing the edits somewhere, and the design keeps the PDF file itself as the
 * only durable store — that is exactly what the close/unload guards are for.
 */
export interface PdfEditState {
  filePath: string
  dirty: boolean
  outline: PdfOutlineEditEntry[]
}

export const useBookViewStore = defineStore('bookView', () => {
  const tabStore = useTabStore()

  // Per-pane toolbar visibility. Both panes start from the same persisted preference,
  // but can be toggled independently once split view is active.
  const toolbarVisibleByPane = ref<Map<1 | 2, boolean>>(new Map([[1, true], [2, true]]))

  function getToolbarVisible(paneId: 1 | 2): boolean {
    return toolbarVisibleByPane.value.get(paneId) ?? true
  }

  // Single computed for backward-compat consumers that don't pass a pane ID.
  // Always reflects pane 1.
  const toolbarVisible = computed(() => getToolbarVisible(1))

  const toolbarPosition = ref<ToolbarPosition>('top')
  const toggleBottomPanelSignal = ref<{ count: number; paneId: 1 | 2 }>({ count: 0, paneId: 1 })
  const openSearchSignal = ref<{ count: number; paneId: 1 | 2 }>({ count: 0, paneId: 1 })
  const toggleTocPanelSignal = ref<{ count: number; paneId: 1 | 2 }>({ count: 0, paneId: 1 })
  const txtViewToggleSearchSignal = ref<{ count: number; paneId: 1 | 2 }>({ count: 0, paneId: 1 })
  const txtViewSearchVisible = ref(false)
  const autoSelectTopLine = ref(false)

  function toggleBottomPanel(paneId: 1 | 2 = 1) {
    toggleBottomPanelSignal.value = { count: toggleBottomPanelSignal.value.count + 1, paneId }
  }

  function openSearch(paneId: 1 | 2 = 1) {
    openSearchSignal.value = { count: openSearchSignal.value.count + 1, paneId }
  }

  function toggleTocPanel(paneId: 1 | 2 = 1) {
    toggleTocPanelSignal.value = { count: toggleTocPanelSignal.value.count + 1, paneId }
  }

  function txtViewToggleSearch(paneId: 1 | 2 = 1) {
    txtViewToggleSearchSignal.value = { count: txtViewToggleSearchSignal.value.count + 1, paneId }
  }

  const isBookViewActive = computed(() => tabStore.activeTab.route === '/book-view')
  const isTxtViewActive = computed(() => tabStore.activeTab.route === '/txt-view')

  // Per-tab+book zoom maps — one for lines text, one for commentary text.
  // Keys: `${tabId}:${bookId}`
  const linesZoomMap = ref<Map<string, number>>(new Map())
  const commentaryZoomMap = ref<Map<string, number>>(new Map())

  function zoomKey(tabId: string, bookId: number) {
    return `${tabId}:${bookId}`
  }

  function getLinesZoom(tabId: string, bookId: number): number {
    return linesZoomMap.value.get(zoomKey(tabId, bookId)) ?? ZOOM_CONFIG.DEFAULT
  }

  function setLinesZoom(tabId: string, bookId: number, value: number) {
    linesZoomMap.value.set(zoomKey(tabId, bookId), value)
  }

  function getCommentaryZoom(tabId: string, bookId: number): number {
    return commentaryZoomMap.value.get(zoomKey(tabId, bookId)) ?? ZOOM_CONFIG.DEFAULT
  }

  function setCommentaryZoom(tabId: string, bookId: number, value: number) {
    commentaryZoomMap.value.set(zoomKey(tabId, bookId), value)
  }

  // Keep old getZoom/setZoom as aliases for lines zoom so callers that haven't
  // been migrated yet continue to work.
  function getZoom(tabId: string, bookId: number): number {
    return getLinesZoom(tabId, bookId)
  }

  function setZoom(tabId: string, bookId: number, value: number) {
    setLinesZoom(tabId, bookId, value)
  }

  // ── In-book search query (in-session, per-tab, never persisted) ────────────
  // The Ctrl+F search input text is kept alive across close/reopen of the search
  // bar (and across tab switches, which remount BookViewPage) so reopening shows
  // the last query. It is deliberately NOT persisted to IDB/localStorage and is
  // NOT part of the "save last position" persistence — a fresh session always
  // starts empty. Cleared automatically on tab close by the prune watch below.
  // Keyed by tabId (not tabId:bookId): the query follows the tab, not the book.
  const searchQueryByTabId = ref<Map<string, string>>(new Map())
  const commentarySearchQueryByTabId = ref<Map<string, string>>(new Map())

  function getSearchQuery(tabId: string): string {
    return searchQueryByTabId.value.get(tabId) ?? ''
  }
  function setSearchQuery(tabId: string, value: string) {
    if (value) searchQueryByTabId.value.set(tabId, value)
    else searchQueryByTabId.value.delete(tabId)
  }
  function getCommentarySearchQuery(tabId: string): string {
    return commentarySearchQueryByTabId.value.get(tabId) ?? ''
  }
  function setCommentarySearchQuery(tabId: string, value: string) {
    if (value) commentarySearchQueryByTabId.value.set(tabId, value)
    else commentarySearchQueryByTabId.value.delete(tabId)
  }

  // Prune per-tab entries for tabs that no longer exist (zoom + in-session search)
  watch(
    () => tabStore.tabs.map((t) => t.id),
    (currentIds) => {
      const idSet = new Set(currentIds)
      for (const key of linesZoomMap.value.keys()) {
        const tabId = key.split(':')[0]!
        if (!idSet.has(tabId)) linesZoomMap.value.delete(key)
      }
      for (const key of commentaryZoomMap.value.keys()) {
        const tabId = key.split(':')[0]!
        if (!idSet.has(tabId)) commentaryZoomMap.value.delete(key)
      }
      for (const tabId of searchQueryByTabId.value.keys()) {
        if (!idSet.has(tabId)) searchQueryByTabId.value.delete(tabId)
      }
      for (const tabId of commentarySearchQueryByTabId.value.keys()) {
        if (!idSet.has(tabId)) commentarySearchQueryByTabId.value.delete(tabId)
      }
    },
  )

  // Active-tab computed for lines zoom — used by the toolbar display and keyboard handler.
  const zoom = computed({
    get() {
      const tab = tabStore.activeTab
      if (tab.route !== '/book-view' || tab.bookId == null) return ZOOM_CONFIG.DEFAULT
      return getLinesZoom(tab.id, tab.bookId)
    },
    set(value: number) {
      const tab = tabStore.activeTab
      if (tab.route !== '/book-view' || tab.bookId == null) return
      setLinesZoom(tab.id, tab.bookId, value)
    },
  })

  // Active-tab computed for commentary zoom — used by the toolbar display.
  const commentaryZoom = computed({
    get() {
      const tab = tabStore.activeTab
      if (tab.route !== '/book-view' || tab.bookId == null) return ZOOM_CONFIG.DEFAULT
      return getCommentaryZoom(tab.id, tab.bookId)
    },
    set(value: number) {
      const tab = tabStore.activeTab
      if (tab.route !== '/book-view' || tab.bookId == null) return
      setCommentaryZoom(tab.id, tab.bookId, value)
    },
  })

  // Synchronous — all bookView settings are in localStorage
  function init() {
    const toolbar = lsGet<boolean>(KEYS.SETTINGS_TOOLBAR)
    if (toolbar != null) {
      toolbarVisibleByPane.value.set(1, toolbar)
      toolbarVisibleByPane.value.set(2, toolbar)
    }
    const pos = lsGet<ToolbarPosition>(KEYS.SETTINGS_TOOLBAR_POSITION)
    if (pos != null) toolbarPosition.value = pos
    const autoSelect = lsGet<boolean>(KEYS.SETTINGS_AUTO_SELECT_TOP_LINE)
    if (autoSelect != null) autoSelectTopLine.value = autoSelect
    else autoSelectTopLine.value = useSettingsStore().defaultAutoSyncCommentary
    const splitView = lsGet<boolean>(KEYS.SETTINGS_SPLIT_VIEW)
    if (splitView != null) splitViewEnabled.value = splitView
    const splitFraction = lsGet<number>(KEYS.SETTINGS_SPLIT_VIEW_FRACTION)
    if (splitFraction != null) splitViewFraction.value = splitFraction
  }

  function toggleToolbar(paneId: 1 | 2 = 1) {
    const next = !getToolbarVisible(paneId)
    toolbarVisibleByPane.value.set(paneId, next)
    // Persist using pane 1's value as the canonical preference.
    if (paneId === 1) lsSet(KEYS.SETTINGS_TOOLBAR, next)
  }

  function setToolbarPosition(pos: ToolbarPosition) {
    toolbarPosition.value = pos
    lsSet(KEYS.SETTINGS_TOOLBAR_POSITION, pos)
  }

  function toggleAutoSelectTopLine() {
    autoSelectTopLine.value = !autoSelectTopLine.value
    lsSet(KEYS.SETTINGS_AUTO_SELECT_TOP_LINE, autoSelectTopLine.value)
  }

  function setAutoSelectTopLine(value: boolean) {
    autoSelectTopLine.value = value
    lsSet(KEYS.SETTINGS_AUTO_SELECT_TOP_LINE, value)
  }

  // ── Split view ─────────────────────────────────────────────────────────────

  const splitViewEnabled = ref(false)
  const splitViewFraction = ref(0.5)
  const focusedPaneId = ref<1 | 2>(1)

  function setFocusedPane(paneId: 1 | 2) {
    focusedPaneId.value = paneId
  }

  function toggleSplitView() {
    splitViewEnabled.value = !splitViewEnabled.value
    lsSet(KEYS.SETTINGS_SPLIT_VIEW, splitViewEnabled.value)
  }

  function disableSplitView() {
    splitViewEnabled.value = false
    lsSet(KEYS.SETTINGS_SPLIT_VIEW, false)
  }

  function setSplitViewFraction(fraction: number) {
    splitViewFraction.value = fraction
    lsSet(KEYS.SETTINGS_SPLIT_VIEW_FRACTION, fraction)
  }

  // Resolve which tab's zoom a zoom action targets. Callers inside a pane pass
  // their own (tabId, bookId) so pane 2's controls change pane 2's book — the
  // no-arg fallback (pane 1's active tab) exists for legacy callers only. An
  // explicit tabId with no bookId means "this pane isn't showing a book": no-op,
  // never fall through to pane 1.
  function zoomTarget(tabId?: string, bookId?: number): { tabId: string; bookId: number } | null {
    if (tabId != null) return bookId != null ? { tabId, bookId } : null
    const tab = tabStore.activeTab
    if (tab.route !== '/book-view' || tab.bookId == null) return null
    return { tabId: tab.id, bookId: tab.bookId }
  }

  function zoomIn(tabId?: string, bookId?: number) {
    const t = zoomTarget(tabId, bookId)
    if (!t) return
    setLinesZoom(t.tabId, t.bookId, zoomInUtil(getLinesZoom(t.tabId, t.bookId)))
    setCommentaryZoom(t.tabId, t.bookId, zoomInUtil(getCommentaryZoom(t.tabId, t.bookId)))
  }
  function zoomOut(tabId?: string, bookId?: number) {
    const t = zoomTarget(tabId, bookId)
    if (!t) return
    setLinesZoom(t.tabId, t.bookId, zoomOutUtil(getLinesZoom(t.tabId, t.bookId)))
    setCommentaryZoom(t.tabId, t.bookId, zoomOutUtil(getCommentaryZoom(t.tabId, t.bookId)))
  }
  function resetZoom(tabId?: string, bookId?: number) {
    const t = zoomTarget(tabId, bookId)
    if (!t) return
    setLinesZoom(t.tabId, t.bookId, resetZoomUtil())
    setCommentaryZoom(t.tabId, t.bookId, resetZoomUtil())
  }

  // ── TOC bridge — per-tab registration for title bar navigation ────────────
  // Uses a reactive Map so insertions/deletions trigger computed re-evaluation.

  const tocBridgeByTabId = reactive(new Map<string, TocBridge>())

  function registerTocBridge(tabId: string, bridge: TocBridge) {
    tocBridgeByTabId.set(tabId, bridge)
  }

  function unregisterTocBridge(tabId: string) {
    tocBridgeByTabId.delete(tabId)
  }

  function getTocBridge(tabId: string): TocBridge | null {
    return tocBridgeByTabId.get(tabId) ?? null
  }

  // ── PDF bridge — per-tab registration for PDF outline breadcrumb navigation

  const pdfBridgeByTabId = reactive(new Map<string, PdfBridge>())

  function registerPdfBridge(tabId: string, bridge: PdfBridge) {
    pdfBridgeByTabId.set(tabId, bridge)
  }

  function unregisterPdfBridge(tabId: string) {
    pdfBridgeByTabId.delete(tabId)
  }

  function getPdfBridge(tabId: string): PdfBridge | null {
    return pdfBridgeByTabId.get(tabId) ?? null
  }

  // ── PDF unsaved-edit snapshots + close guard state ─────────────────────────

  const pdfEditStateByTabId = reactive(new Map<string, PdfEditState>())

  /** Called by the PDF view on every viewer notification (edit or save). */
  function setPdfEditState(tabId: string, state: PdfEditState) {
    if (state.dirty) pdfEditStateByTabId.set(tabId, state)
    else pdfEditStateByTabId.delete(tabId) // a completed save resolves the debt
  }

  function getPdfEditState(tabId: string): PdfEditState | null {
    return pdfEditStateByTabId.get(tabId) ?? null
  }

  function clearPdfEditState(tabId: string) {
    pdfEditStateByTabId.delete(tabId)
  }

  /**
   * True when closing this tab would lose PDF edits: the live viewer says so
   * (active tab), or a parked snapshot is dirty (background tab / same-tab
   * navigation that replaced the PDF).
   */
  function hasUnsavedPdfChanges(tabId: string): boolean {
    const live = pdfBridgeByTabId.get(tabId)?.hasUnsavedChanges?.()
    if (live) return true
    return pdfEditStateByTabId.get(tabId)?.dirty === true
  }

  /** True when ANY tab has unsaved PDF edits — the app-close guard. */
  function hasAnyUnsavedPdfChanges(): boolean {
    // Snapshots are dirty by construction — setPdfEditState deletes on clean.
    if (pdfEditStateByTabId.size > 0) return true
    for (const bridge of pdfBridgeByTabId.values()) {
      if (bridge.hasUnsavedChanges?.()) return true
    }
    return false
  }

  /**
   * Pending close confirmations, FIFO. tabStore's close paths enqueue instead
   * of closing when a tab has unsaved PDF edits; App.vue renders the head.
   * `proceed` re-runs the close with the tabs pre-approved. A queue rather
   * than a single slot: a second close request (e.g. from the native
   * chrome-tabs strip, which the web dialog does not block) must not evaporate
   * the first.
   */
  const pdfClosePendingQueue = ref<{ tabTitles: string[]; proceed: () => void }[]>([])

  const pdfClosePending = computed(() => pdfClosePendingQueue.value[0] ?? null)

  function requestPdfCloseConfirm(tabTitles: string[], proceed: () => void) {
    pdfClosePendingQueue.value.push({ tabTitles, proceed })
  }

  function resolvePdfCloseConfirm(confirmed: boolean) {
    const pending = pdfClosePendingQueue.value.shift()
    if (confirmed && pending) pending.proceed()
  }

  return {
    pdfEditStateByTabId,
    setPdfEditState,
    getPdfEditState,
    clearPdfEditState,
    hasUnsavedPdfChanges,
    hasAnyUnsavedPdfChanges,
    pdfClosePending,
    requestPdfCloseConfirm,
    resolvePdfCloseConfirm,
    toolbarVisible,
    toolbarPosition,
    getToolbarVisible,
    toggleBottomPanelSignal,
    toggleBottomPanel,
    openSearchSignal,
    openSearch,
    toggleTocPanelSignal,
    toggleTocPanel,
    txtViewToggleSearchSignal,
    txtViewToggleSearch,
    txtViewSearchVisible,
    isBookViewActive,
    isTxtViewActive,
    splitViewEnabled,
    splitViewFraction,
    focusedPaneId,
    setFocusedPane,
    toggleSplitView,
    disableSplitView,
    setSplitViewFraction,
    zoom,
    commentaryZoom,
    getZoom,
    setZoom,
    getLinesZoom,
    setLinesZoom,
    getCommentaryZoom,
    setCommentaryZoom,
    autoSelectTopLine,
    toggleAutoSelectTopLine,
    setAutoSelectTopLine,
    getSearchQuery,
    setSearchQuery,
    getCommentarySearchQuery,
    setCommentarySearchQuery,
    init,
    toggleToolbar,
    setToolbarPosition,
    zoomIn,
    zoomOut,
    resetZoom,
    registerTocBridge,
    unregisterTocBridge,
    getTocBridge,
    registerPdfBridge,
    unregisterPdfBridge,
    getPdfBridge,
  }
})
