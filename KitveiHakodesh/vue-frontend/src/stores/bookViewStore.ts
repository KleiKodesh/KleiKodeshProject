import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { useTabStore } from './tabStore'
import { useSettingsStore } from './settingsStore'
import { lsGet, lsSet, KEYS } from '@/utils/persistence'
import {
  ZOOM_CONFIG,
  zoomIn as zoomInUtil,
  zoomOut as zoomOutUtil,
  resetZoom as resetZoomUtil,
} from '@/composables/useZoom'
export type ToolbarPosition = 'top' | 'bottom' | 'left' | 'right'

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

  // Prune zoom entries for tabs that no longer exist
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

  function setSplitViewFraction(fraction: number) {
    splitViewFraction.value = fraction
    lsSet(KEYS.SETTINGS_SPLIT_VIEW_FRACTION, fraction)
  }

  function zoomIn() {
    zoom.value = zoomInUtil(zoom.value)
    commentaryZoom.value = zoomInUtil(commentaryZoom.value)
  }
  function zoomOut() {
    zoom.value = zoomOutUtil(zoom.value)
    commentaryZoom.value = zoomOutUtil(commentaryZoom.value)
  }
  function resetZoom() {
    zoom.value = resetZoomUtil()
    commentaryZoom.value = resetZoomUtil()
  }

  return {
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
    init,
    toggleToolbar,
    setToolbarPosition,
    zoomIn,
    zoomOut,
    resetZoom,
  }
})
