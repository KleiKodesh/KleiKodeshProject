import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { useTabStore } from './tabStore'
import { useSettingsStore } from './settingsStore'
import { idbGetMany, idbSet, KEYS } from '@/utils/idbPersistence'
import {
  ZOOM_CONFIG,
  zoomIn as zoomInUtil,
  zoomOut as zoomOutUtil,
  resetZoom as resetZoomUtil,
} from '@/composables/useZoom'
import type { ToolbarPosition } from '@/composables/useToolbarPosition'

export interface SearchBarPos {
  x: number
  y: number
}

export const useBookViewStore = defineStore('bookView', () => {
  const tabStore = useTabStore()

  const toolbarVisible = ref(true)
  const toolbarPosition = ref<ToolbarPosition>('top')
  const toggleBottomPanelSignal = ref(0)
  const searchBarPos = ref<SearchBarPos | null>(null)
  const autoSelectTopLine = ref(false)

  function toggleBottomPanel() {
    toggleBottomPanelSignal.value++
  }

  const isBookViewActive = computed(() => tabStore.activeTab.route === '/book-view')

  // Per-tab+book zoom map: key = `${tabId}:${bookId}`
  const zoomMap = ref<Map<string, number>>(new Map())

  function zoomKey(tabId: string, bookId: number) {
    return `${tabId}:${bookId}`
  }

  function getZoom(tabId: string, bookId: number): number {
    return zoomMap.value.get(zoomKey(tabId, bookId)) ?? ZOOM_CONFIG.DEFAULT
  }

  function setZoom(tabId: string, bookId: number, value: number) {
    zoomMap.value.set(zoomKey(tabId, bookId), value)
  }

  // Prune zoom entries for tabs that no longer exist
  watch(
    () => tabStore.tabs.map((t) => t.id),
    (currentIds) => {
      const idSet = new Set(currentIds)
      for (const key of zoomMap.value.keys()) {
        const tabId = key.split(':')[0]!
        if (!idSet.has(tabId)) zoomMap.value.delete(key)
      }
    },
  )

  const zoom = computed({
    get() {
      const tab = tabStore.activeTab
      if (tab.route !== '/book-view' || tab.bookId == null) return ZOOM_CONFIG.DEFAULT
      return getZoom(tab.id, tab.bookId)
    },
    set(value: number) {
      const tab = tabStore.activeTab
      if (tab.route !== '/book-view' || tab.bookId == null) return
      setZoom(tab.id, tab.bookId, value)
    },
  })

  async function init() {
    const data = await idbGetMany([
      KEYS.SETTINGS_TOOLBAR,
      KEYS.SETTINGS_TOOLBAR_POSITION,
      KEYS.SETTINGS_SEARCH_BAR_POS,
      KEYS.SETTINGS_AUTO_SELECT_TOP_LINE,
    ])
    if (data[KEYS.SETTINGS_TOOLBAR] != null) toolbarVisible.value = data[KEYS.SETTINGS_TOOLBAR] as boolean
    if (data[KEYS.SETTINGS_TOOLBAR_POSITION] != null) toolbarPosition.value = data[KEYS.SETTINGS_TOOLBAR_POSITION] as ToolbarPosition
    if (data[KEYS.SETTINGS_SEARCH_BAR_POS] != null) searchBarPos.value = data[KEYS.SETTINGS_SEARCH_BAR_POS] as SearchBarPos
    if (data[KEYS.SETTINGS_AUTO_SELECT_TOP_LINE] != null) autoSelectTopLine.value = data[KEYS.SETTINGS_AUTO_SELECT_TOP_LINE] as boolean
    else autoSelectTopLine.value = useSettingsStore().defaultAutoSyncCommentary
  }

  function toggleToolbar() {
    toolbarVisible.value = !toolbarVisible.value
    idbSet(KEYS.SETTINGS_TOOLBAR, toolbarVisible.value)
  }

  function setToolbarPosition(pos: ToolbarPosition) {
    toolbarPosition.value = pos
    idbSet(KEYS.SETTINGS_TOOLBAR_POSITION, pos)
  }

  function setSearchBarPos(pos: SearchBarPos) {
    searchBarPos.value = pos
    idbSet(KEYS.SETTINGS_SEARCH_BAR_POS, pos)
  }

  function toggleAutoSelectTopLine() {
    autoSelectTopLine.value = !autoSelectTopLine.value
    idbSet(KEYS.SETTINGS_AUTO_SELECT_TOP_LINE, autoSelectTopLine.value)
  }

  function setAutoSelectTopLine(value: boolean) {
    autoSelectTopLine.value = value
    idbSet(KEYS.SETTINGS_AUTO_SELECT_TOP_LINE, value)
  }

  function zoomIn() {
    zoom.value = zoomInUtil(zoom.value)
  }
  function zoomOut() {
    zoom.value = zoomOutUtil(zoom.value)
  }
  function resetZoom() {
    zoom.value = resetZoomUtil()
  }

  return {
    toolbarVisible,
    toolbarPosition,
    toggleBottomPanelSignal,
    toggleBottomPanel,
    isBookViewActive,
    zoom,
    getZoom,
    setZoom,
    searchBarPos,
    setSearchBarPos,
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
