/**
 * Search panel state and navigation for the book view.
 *
 * Orchestrates content search and commentary search behind a unified interface.
 * Owns: panel open/close, mode switching, query forwarding, match navigation,
 * and scrolling the active match into view.
 */
import { ref, computed, nextTick } from 'vue'
import type { SearchMode } from './bookViewTypes'

type ContentSearch = {
  query: import('vue').Ref<string>
  matchCount: import('vue').Ref<number>
  currentMatchIdx: import('vue').Ref<number>
  currentMatchLineIndex: import('vue').Ref<number>
  currentMatchOccurrence: import('vue').Ref<number>
  gotoNearestMatch?: () => void
  next: () => void
  prev: () => void
  clear: () => void
}

type CommentarySearch = {
  query: import('vue').Ref<string>
  matchCount: import('vue').Ref<number>
  currentMatchIdx: import('vue').Ref<number>
  currentMatchFlatIndex: import('vue').Ref<number>
  currentMatchOccurrence: import('vue').Ref<number>
  gotoNearestMatch?: () => void
  next: () => void
  prev: () => void
  clear: () => void
}

type LinesContentInstance = {
  scrollToLineIndex: (lineIndex: number, occurrence?: number) => void
  focusScroller: () => void
}

type CommentaryViewInstance = {
  scrollToFlatIndex: (index: number, occurrence?: number) => void
}

export function useBookViewSearchPanel(
  contentSearch: ContentSearch,
  commentarySearch: CommentarySearch,
  linesContentRef: () => LinesContentInstance | null,
  commentaryViewRef: () => CommentaryViewInstance | null,
  searchBarRef: () => { focus: () => void } | null,
  clearFullTextSearchHighlights: () => void,
) {
  const searchVisible = ref(false)
  const searchMode = ref<SearchMode>('content')

  // Tracks whether the user has pressed next/prev at least once since the
  // panel opened or the mode changed. On the first press we jump to the
  // nearest match rather than advancing past it.
  const searchNavigationState = { content: false, commentary: false }

  const activeSearch = computed(() => searchMode.value === 'content' ? contentSearch : commentarySearch)
  const activeMatchCount = computed(() => activeSearch.value.matchCount.value)
  const activeMatchIdx = computed(() => activeSearch.value.currentMatchIdx.value)

  function scrollContentMatch() {
    if (searchMode.value === 'content') {
      if (contentSearch.currentMatchLineIndex.value === -1) return
      linesContentRef()?.scrollToLineIndex(
        contentSearch.currentMatchLineIndex.value,
        contentSearch.currentMatchOccurrence.value,
      )
    } else {
      if (commentarySearch.currentMatchFlatIndex.value === -1) return
      commentaryViewRef()?.scrollToFlatIndex(
        commentarySearch.currentMatchFlatIndex.value,
        commentarySearch.currentMatchOccurrence.value,
      )
    }
  }

  function openContentSearch() {
    if (searchVisible.value && searchMode.value === 'content') {
      searchVisible.value = false
      nextTick(() => linesContentRef()?.focusScroller())
      return
    }
    searchVisible.value = true
    searchMode.value = 'content'
    searchNavigationState.content = false
    nextTick(() => searchBarRef()?.focus())
  }

  function openCommentarySearch() {
    if (searchVisible.value && searchMode.value === 'commentary') {
      searchVisible.value = false
      return
    }
    searchVisible.value = true
    searchMode.value = 'commentary'
    searchNavigationState.commentary = false
    nextTick(() => searchBarRef()?.focus())
  }

  function onModeChange(mode: SearchMode) {
    const currentQuery = activeSearch.value.query.value
    contentSearch.clear()
    commentarySearch.clear()
    searchMode.value = mode
    searchNavigationState[mode] = false
    if (!currentQuery) return
    const target = mode === 'content' ? contentSearch : commentarySearch
    target.query.value = currentQuery
  }

  function onQueryChange(query: string) {
    if (query.trim()) {
      clearFullTextSearchHighlights()
    }
    activeSearch.value.query.value = query
    searchNavigationState[searchMode.value] = false
  }

  function onSearchNext() {
    const search = activeSearch.value
    if (search.matchCount.value === 0) return
    if (!searchNavigationState[searchMode.value]) {
      searchNavigationState[searchMode.value] = true
      search.gotoNearestMatch?.()
      scrollContentMatch()
      return
    }
    search.next()
    scrollContentMatch()
  }

  function onSearchPrev() {
    const search = activeSearch.value
    if (search.matchCount.value === 0) return
    if (!searchNavigationState[searchMode.value]) {
      searchNavigationState[searchMode.value] = true
      search.gotoNearestMatch?.()
      scrollContentMatch()
      return
    }
    search.prev()
    scrollContentMatch()
  }

  return {
    searchVisible,
    searchMode,
    activeMatchCount,
    activeMatchIdx,
    openContentSearch,
    openCommentarySearch,
    onModeChange,
    onQueryChange,
    onSearchNext,
    onSearchPrev,
  }
}
