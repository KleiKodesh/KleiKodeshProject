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

  const toolbarVisible = ref(true)
  const toolbarPosition = ref<ToolbarPosition>('top')
  const toggleBottomPanelSignal = ref(0)
  const openSearchSignal = ref(0)
  const toggleTocPanelSignal = ref(0)
  const autoSelectTopLine = ref(false)

  function toggleBottomPanel() {
    toggleBottomPanelSignal.value++
  }

  function openSearch() {
    openSearchSignal.value++
  }

  function toggleTocPanel() {
    toggleTocPanelSignal.value++
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
    if (toolbar != null) toolbarVisible.value = toolbar
    const pos = lsGet<ToolbarPosition>(KEYS.SETTINGS_TOOLBAR_POSITION)
    if (pos != null) toolbarPosition.value = pos
    const autoSelect = lsGet<boolean>(KEYS.SETTINGS_AUTO_SELECT_TOP_LINE)
    if (autoSelect != null) autoSelectTopLine.value = autoSelect
    else autoSelectTopLine.value = useSettingsStore().defaultAutoSyncCommentary
  }

  function toggleToolbar() {
    toolbarVisible.value = !toolbarVisible.value
    lsSet(KEYS.SETTINGS_TOOLBAR, toolbarVisible.value)
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
    toggleBottomPanelSignal,
    toggleBottomPanel,
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
    init,
    toggleToolbar,
    setToolbarPosition,
    zoomIn,
    zoomOut,
    resetZoom,
  }
})
