import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { useTabStore } from './tabStore'
import { persistGet, persistSet, PERSIST_KEYS } from '@/utils/persist'
import { zoomIn as zoomInUtil, zoomOut as zoomOutUtil, resetZoom as resetZoomUtil, ZOOM_CONFIG } from '@/composables/useZoom'

export const useBookViewStore = defineStore('bookView', () => {
  const tabStore = useTabStore()

  const toolbarVisible = ref(tabStore.getToolbarVisible())
  const searchBarPos = ref<{ x: number; y: number } | null>(tabStore.getSearchBarPos())
  const isBookViewActive = computed(() => tabStore.activeTab.route === '/book-view')
  const zoom = ref(persistGet(PERSIST_KEYS.BOOK_VIEW_ZOOM, ZOOM_CONFIG.DEFAULT))

  function toggleToolbar() {
    toolbarVisible.value = !toolbarVisible.value
    tabStore.setToolbarVisible(toolbarVisible.value)
  }

  function setSearchBarPos(pos: { x: number; y: number }) {
    searchBarPos.value = pos
    tabStore.setSearchBarPos(pos)
  }

  function zoomIn() { zoom.value = zoomInUtil(zoom.value); persistSet(PERSIST_KEYS.BOOK_VIEW_ZOOM, zoom.value) }
  function zoomOut() { zoom.value = zoomOutUtil(zoom.value); persistSet(PERSIST_KEYS.BOOK_VIEW_ZOOM, zoom.value) }
  function resetZoom() { zoom.value = resetZoomUtil(); persistSet(PERSIST_KEYS.BOOK_VIEW_ZOOM, zoom.value) }

  return { toolbarVisible, searchBarPos, isBookViewActive, zoom, toggleToolbar, setSearchBarPos, zoomIn, zoomOut, resetZoom }
})
