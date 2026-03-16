import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { useTabStore } from './tabStore'

export const useBookViewStore = defineStore('bookView', () => {
  const tabStore = useTabStore()

  const toolbarVisible = ref(tabStore.getToolbarVisible())
  const searchBarPos = ref<{ x: number; y: number } | null>(tabStore.getSearchBarPos())
  const isBookViewActive = computed(() => tabStore.activeTab.route === '/book-view')

  function toggleToolbar() {
    toolbarVisible.value = !toolbarVisible.value
    tabStore.setToolbarVisible(toolbarVisible.value)
  }

  function setSearchBarPos(pos: { x: number; y: number }) {
    searchBarPos.value = pos
    tabStore.setSearchBarPos(pos)
  }

  return { toolbarVisible, searchBarPos, isBookViewActive, toggleToolbar, setSearchBarPos }
})
