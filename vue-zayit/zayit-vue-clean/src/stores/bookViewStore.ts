import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { useTabStore } from './tabStore'
import { idbGet, idbSet, KEYS } from '@/utils/idbPersistence'
import {
  ZOOM_CONFIG,
  zoomIn as zoomInUtil,
  zoomOut as zoomOutUtil,
  resetZoom as resetZoomUtil,
} from '@/composables/useZoom'

export const useBookViewStore = defineStore('bookView', () => {
  const tabStore = useTabStore()

  const toolbarVisible = ref(true)
  const toggleBottomPanelSignal = ref(0)
  function toggleBottomPanel() {
    toggleBottomPanelSignal.value++
  }
  const searchBarPos = ref<{ x: number; y: number } | null>(null)
  const isBookViewActive = computed(() => tabStore.activeTab.route === '/book-view')
  const zoom = ref<number>(ZOOM_CONFIG.DEFAULT)

  // Called from main.ts after tabStore.init()
  async function init() {
    const [toolbar, pos, z] = await Promise.all([
      idbGet<boolean>(KEYS.SETTINGS_TOOLBAR),
      idbGet<{ x: number; y: number }>(KEYS.SETTINGS_SEARCH_BAR_POS),
      idbGet<number>(KEYS.SETTINGS_ZOOM),
    ])
    if (toolbar !== null) toolbarVisible.value = toolbar
    if (pos !== null) searchBarPos.value = pos
    if (z !== null) zoom.value = z
  }

  function toggleToolbar() {
    toolbarVisible.value = !toolbarVisible.value
    idbSet(KEYS.SETTINGS_TOOLBAR, toolbarVisible.value)
  }

  function setSearchBarPos(pos: { x: number; y: number }) {
    searchBarPos.value = pos
    idbSet(KEYS.SETTINGS_SEARCH_BAR_POS, pos)
  }

  function zoomIn() {
    zoom.value = zoomInUtil(zoom.value)
    idbSet(KEYS.SETTINGS_ZOOM, zoom.value)
  }
  function zoomOut() {
    zoom.value = zoomOutUtil(zoom.value)
    idbSet(KEYS.SETTINGS_ZOOM, zoom.value)
  }
  function resetZoom() {
    zoom.value = resetZoomUtil()
    idbSet(KEYS.SETTINGS_ZOOM, zoom.value)
  }

  return {
    toolbarVisible,
    toggleBottomPanelSignal,
    toggleBottomPanel,
    searchBarPos,
    isBookViewActive,
    zoom,
    init,
    toggleToolbar,
    setSearchBarPos,
    zoomIn,
    zoomOut,
    resetZoom,
  }
})
