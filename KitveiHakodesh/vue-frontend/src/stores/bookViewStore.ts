import { defineStore } from 'pinia'
import { ref, shallowReactive, computed, watch, nextTick } from 'vue'
import { useTabStore } from './tabStore'
import { useSettingsStore } from './settingsStore'
import {
  ZOOM_CONFIG,
  zoomIn as zoomInUtil,
  zoomOut as zoomOutUtil,
  resetZoom as resetZoomUtil,
} from '@/composables/useZoom'
import type { TocEntry } from '@/webview-host/queries.types'
import { COMMENTARY_SLOTS } from '@/features/book-view/bookViewTypes'
import type { CommentarySlot } from '@/features/book-view/bookViewTypes'

// The toolbar preferences this store used to persist itself now live in
// settingsStore, so one reset() covers them along with the fonts they sit beside in the
// settings UI. This store reads them through that store and keeps only the session
// state built on top.
export type { ToolbarPosition } from './settingsStore'
import type { ToolbarPosition } from './settingsStore'

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
 * Derived from the PDF reader's flat outline (features/pdf-reader/usePdfOutline.ts).
 */
export interface PdfOutlineEntry {
  id: number        // index in the flat outline list — stable unique key
  text: string      // the last path segment (leaf label)
  fullPath: string  // the full " · "-joined path, used to match the active tocPath
  parentPath: string // all segments except the last, or "" for root entries
}

/**
 * Registered by the mounted PDF view of a /pdf-view tab (features/pdf-reader/PdfReaderView):
 * the title bar reads the outline and navigates through it, and the close / leave guards ask
 * about unsaved changes and save. In memory only; unregistered on unmount.
 *
 * Only a tab's ACTIVE page is mounted, so a tab without a registered viewer has nothing
 * unsaved: leaving a dirty viewer (close, switch tab, navigate) goes through the guard first.
 */
export interface PdfBridge {
  /** Flat outline in document order (the title-bar breadcrumb). */
  outlineEntries: PdfOutlineEntry[]
  navigateToEntry: (entry: PdfOutlineEntry) => void
  /** Edits not yet in the file (annotations, outline). */
  hasUnsavedChanges: () => boolean
  /** Save in place (Save As when the tab has no file). Resolves true once the file was written. */
  save: () => Promise<boolean>
}

export const useBookViewStore = defineStore('bookView', () => {
  const tabStore = useTabStore()

  const settings = useSettingsStore()

  // The stored preference itself, read live - so a settings reset takes effect at once.
  const toolbarVisible = computed(() => settings.toolbarVisible)

  function getToolbarVisible(): boolean {
    return settings.toolbarVisible
  }

  // Writable pass-through so `v-model="toolbarPosition"` in the settings page keeps
  // working — the value itself is settingsStore's.
  const toolbarPosition = computed<ToolbarPosition>({
    get: () => settings.toolbarPosition,
    set: (pos) => { settings.toolbarPosition = pos },
  })
  const toggleCommentaryPanelSignal = ref<{ count: number; slot: CommentarySlot }>({ count: 0, slot: 'bottom' })
  const openSearchSignal = ref<{ count: number }>({ count: 0 })
  const toggleTocPanelSignal = ref<{ count: number }>({ count: 0 })
  const autoSelectTopLine = computed<boolean>({
    get: () => settings.autoSelectTopLine,
    set: (value) => { settings.autoSelectTopLine = value },
  })

  /** Toggles ONE commentary panel. Ctrl+J targets 'bottom', Ctrl+Shift+J 'side', Ctrl+Alt+J 'side-left'. */
  function toggleCommentaryPanel(slot: CommentarySlot = 'bottom') {
    toggleCommentaryPanelSignal.value = { count: toggleCommentaryPanelSignal.value.count + 1, slot }
  }

  function openSearch() {
    openSearchSignal.value = { count: openSearchSignal.value.count + 1 }
  }

  function toggleTocPanel() {
    toggleTocPanelSignal.value = { count: toggleTocPanelSignal.value.count + 1 }
  }

  const isBookViewActive = computed(() => tabStore.activeTab.route === '/book-view')

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

  // Keyed by tab AND panel: the commentary panels zoom independently, so a reader
  // can enlarge one panel's commentator without touching the others.
  function commentaryZoomKey(tabId: string, bookId: number, slot: CommentarySlot) {
    return `${tabId}:${bookId}:${slot}`
  }

  function getCommentaryZoom(tabId: string, bookId: number, slot: CommentarySlot): number {
    return commentaryZoomMap.value.get(commentaryZoomKey(tabId, bookId, slot)) ?? ZOOM_CONFIG.DEFAULT
  }

  function setCommentaryZoom(tabId: string, bookId: number, slot: CommentarySlot, value: number) {
    commentaryZoomMap.value.set(commentaryZoomKey(tabId, bookId, slot), value)
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
  // Keyed by tab AND panel: the two commentary panels search independently, so one
  // shared query would make reopening the bar show the other panel's search.
  function commentaryQueryKey(tabId: string, slot: CommentarySlot): string {
    return `${tabId}::${slot}`
  }
  function getCommentarySearchQuery(tabId: string, slot: CommentarySlot): string {
    return commentarySearchQueryByTabId.value.get(commentaryQueryKey(tabId, slot)) ?? ''
  }
  function setCommentarySearchQuery(tabId: string, slot: CommentarySlot, value: string) {
    const key = commentaryQueryKey(tabId, slot)
    if (value) commentarySearchQueryByTabId.value.set(key, value)
    else commentarySearchQueryByTabId.value.delete(key)
  }

  // Prune per-tab entries for tabs that no longer exist (zoom + in-session search)
  //
  // Deferred to the next tick: closing a tab removes it from the list BEFORE its
  // book view unmounts, and that unmount runs a final savePos() which reads the
  // zoom back out of these maps. Pruning inline made that read miss and persist
  // the 100% default over the reader's actual zoom. Same ordering hazard as the
  // commentary check-trees in tabStore.disposeClosedTab.
  watch(
    () => tabStore.tabs.map((t) => t.id),
    async (currentIds) => {
      await nextTick()
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
      for (const key of commentarySearchQueryByTabId.value.keys()) {
        // Keys are `${tabId}::${slot}` - liveness is decided by the tab part alone.
        if (!idSet.has(key.slice(0, key.indexOf('::')))) {
          commentarySearchQueryByTabId.value.delete(key)
        }
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
      // The zooms can differ per panel; the store-level computed reports the
      // bottom panel's (the fallback consumers predate the side panel).
      return getCommentaryZoom(tab.id, tab.bookId, 'bottom')
    },
    set(value: number) {
      const tab = tabStore.activeTab
      if (tab.route !== '/book-view' || tab.bookId == null) return
      for (const slot of COMMENTARY_SLOTS) setCommentaryZoom(tab.id, tab.bookId, slot, value)
    },
  })

  /**
   * Nothing to load any more: every book-view preference lives in settingsStore and is
   * read live. Kept because main.ts calls it and a later addition may need it again.
   */
  function init() {}

  function toggleToolbar() {
    settings.toolbarVisible = !settings.toolbarVisible
  }

  function setToolbarPosition(pos: ToolbarPosition) {
    toolbarPosition.value = pos
  }

  function toggleAutoSelectTopLine() {
    autoSelectTopLine.value = !autoSelectTopLine.value
  }

  function setAutoSelectTopLine(value: boolean) {
    autoSelectTopLine.value = value
  }

  // Resolve which tab's zoom a zoom action targets. Callers inside a pane pass
  // their own (tabId, bookId) so each view's controls change its own book — the
  // no-arg fallback (the active tab) exists for legacy callers only. An
  // explicit tabId with no bookId means "this view isn't showing a book": no-op,
  // never fall through to the active tab.
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
    for (const slot of COMMENTARY_SLOTS)
      setCommentaryZoom(t.tabId, t.bookId, slot, zoomInUtil(getCommentaryZoom(t.tabId, t.bookId, slot)))
  }
  function zoomOut(tabId?: string, bookId?: number) {
    const t = zoomTarget(tabId, bookId)
    if (!t) return
    setLinesZoom(t.tabId, t.bookId, zoomOutUtil(getLinesZoom(t.tabId, t.bookId)))
    for (const slot of COMMENTARY_SLOTS)
      setCommentaryZoom(t.tabId, t.bookId, slot, zoomOutUtil(getCommentaryZoom(t.tabId, t.bookId, slot)))
  }
  function resetZoom(tabId?: string, bookId?: number) {
    const t = zoomTarget(tabId, bookId)
    if (!t) return
    setLinesZoom(t.tabId, t.bookId, resetZoomUtil())
    for (const slot of COMMENTARY_SLOTS)
      setCommentaryZoom(t.tabId, t.bookId, slot, resetZoomUtil())
  }

  // ── TOC bridge — per-tab registration for title bar navigation ────────────
  // A SHALLOW reactive Map: insertions/deletions still trigger computeds, but get()
  // returns the bridge itself. A deep reactive Map handed back a proxy, which never
  // equalled the registered bridge, so unregister never deleted it and every closed
  // tab's whole book view (lines, TOC, search tree) stayed in memory through it.

  const tocBridgeByTabId = shallowReactive(new Map<string, TocBridge>())

  function registerTocBridge(tabId: string, bridge: TocBridge) {
    tocBridgeByTabId.set(tabId, bridge)
  }

  /**
   * Drop a tab's bridge — but only if `bridge` is still the registered one.
   *
   * Navigating a tab in place (a cross-book word link) mounts the incoming
   * BookView before the outgoing one unmounts, and both key on the same tabId.
   * An unconditional delete would let the old component's teardown remove the
   * NEW book's bridge, emptying rootTocEntries and un-suppressing the segment-0
   * chevron — a stray dropdown arrow beside the title.
   */
  function unregisterTocBridge(tabId: string, bridge?: TocBridge) {
    if (bridge && tocBridgeByTabId.get(tabId) !== bridge) return
    tocBridgeByTabId.delete(tabId)
  }

  function getTocBridge(tabId: string): TocBridge | null {
    return tocBridgeByTabId.get(tabId) ?? null
  }

  // ── PDF bridge — per-tab registration for PDF outline breadcrumb navigation

  const pdfBridgeByTabId = shallowReactive(new Map<string, PdfBridge>())

  function registerPdfBridge(tabId: string, bridge: PdfBridge) {
    pdfBridgeByTabId.set(tabId, bridge)
  }

  function unregisterPdfBridge(tabId: string) {
    pdfBridgeByTabId.delete(tabId)
  }

  function getPdfBridge(tabId: string): PdfBridge | null {
    return pdfBridgeByTabId.get(tabId) ?? null
  }

  // ── PDF close / leave guard state ────────────────────────────────────────

  /** True when closing or leaving this tab would lose PDF edits (its live viewer says so). */
  function hasUnsavedPdfChanges(tabId: string): boolean {
    return pdfBridgeByTabId.get(tabId)?.hasUnsavedChanges() === true
  }

  /** True when ANY tab has unsaved PDF edits - the app-close guard. */
  function hasAnyUnsavedPdfChanges(): boolean {
    for (const bridge of pdfBridgeByTabId.values()) {
      if (bridge.hasUnsavedChanges()) return true
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
  const pdfClosePendingQueue = ref<
    { tabTitles: string[]; proceed: () => void; tabId: string | null }[]
  >([])

  const pdfClosePending = computed(() => pdfClosePendingQueue.value[0] ?? null)

  /** `tabId` - set when the request is about exactly ONE dirty tab, enabling
   *  the dialog's Save action (save that tab, then proceed). */
  function requestPdfCloseConfirm(tabTitles: string[], proceed: () => void, tabId?: string | null) {
    pdfClosePendingQueue.value.push({ tabTitles, proceed, tabId: tabId ?? null })
  }

  function resolvePdfCloseConfirm(confirmed: boolean) {
    const pending = pdfClosePendingQueue.value.shift()
    if (confirmed && pending) pending.proceed()
  }

  /** Dequeue the head WITHOUT resolving — the save-as flow needs to hold the
   *  `proceed` callback across an async save and only then invoke it. */
  function takePdfClosePending() {
    return pdfClosePendingQueue.value.shift() ?? null
  }

  return {
    hasUnsavedPdfChanges,
    hasAnyUnsavedPdfChanges,
    pdfClosePending,
    requestPdfCloseConfirm,
    resolvePdfCloseConfirm,
    takePdfClosePending,
    toolbarVisible,
    toolbarPosition,
    getToolbarVisible,
    toggleCommentaryPanelSignal,
    toggleCommentaryPanel,
    openSearchSignal,
    openSearch,
    toggleTocPanelSignal,
    toggleTocPanel,
    isBookViewActive,
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
