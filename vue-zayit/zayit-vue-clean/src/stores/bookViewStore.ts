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

  // Reactive zoom for the currently active tab+book (used by toolbar and zoom handler)
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

  // Called from main.ts after tabStore.init()
  async function init() {
    const toolbar = await idbGet<boolean>(KEYS.SETTINGS_TOOLBAR)
    if (toolbar !== null) toolbarVisible.value = toolbar
  }

  function toggleToolbar() {
    toolbarVisible.value = !toolbarVisible.value
    idbSet(KEYS.SETTINGS_TOOLBAR, toolbarVisible.value)
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
    toggleBottomPanelSignal,
    toggleBottomPanel,
    isBookViewActive,
    zoom,
    getZoom,
    setZoom,
    init,
    toggleToolbar,
    zoomIn,
    zoomOut,
    resetZoom,
  }
})
