import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { useTabStore } from './tabStore'
import { persistGet, persistSet, persistRemove, PERSIST_KEYS } from '@/utils/persist'

export interface BookTabState {
  bottomVisible: boolean
}

export const useBookViewStore = defineStore('bookView', () => {
  const tabStore = useTabStore()

  const toolbarVisible = ref(persistGet(PERSIST_KEYS.BOOK_VIEW_TOOLBAR, true))
  const searchBarPos = ref<{ x: number; y: number } | null>(
    persistGet(PERSIST_KEYS.BOOK_VIEW_SEARCH_BAR_POS, null)
  )
  const isBookViewActive = computed(() => tabStore.activeTab.route === '/book-view')

  function toggleToolbar() {
    toolbarVisible.value = !toolbarVisible.value
    persistSet(PERSIST_KEYS.BOOK_VIEW_TOOLBAR, toolbarVisible.value)
  }

  function setSearchBarPos(pos: { x: number; y: number }) {
    searchBarPos.value = pos
    persistSet(PERSIST_KEYS.BOOK_VIEW_SEARCH_BAR_POS, pos)
  }

  // ── Per-tab state ────────────────────────────────────────────────────────────

  function getTabState(tabId: string): BookTabState {
    return persistGet<BookTabState>(PERSIST_KEYS.BOOK_TAB(tabId), {
      bottomVisible: false,
    })
  }

  function setTabState(tabId: string, patch: Partial<BookTabState>) {
    const current = getTabState(tabId)
    persistSet(PERSIST_KEYS.BOOK_TAB(tabId), { ...current, ...patch })
  }

  // Clear persisted tab data when a tab is closed
  tabStore.onTabClose((id) => persistRemove(PERSIST_KEYS.BOOK_TAB(id)))

  return { toolbarVisible, searchBarPos, isBookViewActive, toggleToolbar, setSearchBarPos, getTabState, setTabState }
})
