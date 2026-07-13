/**
 * Search panel state and navigation for the book view.
 *
 * Orchestrates content search and commentary search behind a unified interface.
 * Owns: panel open/close, mode switching, query forwarding, match navigation,
 * and scrolling the active match into view.
 */
import { ref, computed, nextTick } from 'vue'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
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

  // Element the current selection (or caret) is anchored in, if any.
  function selectionAnchorElement(): Element | null {
    const node = window.getSelection()?.anchorNode
    if (!node) return null
    return node instanceof Element ? node : node.parentElement
  }

  // Text currently selected in the lines or commentary view, normalized for
  // use as a search query: diacritics removed, non-word characters collapsed
  // to single spaces — except `-`, `"`, and `״` between word characters
  // (hyphenated words, acronyms like רמב"ם), which are kept.
  function selectionPrefill(): string {
    const sel = window.getSelection()
    if (!sel || sel.isCollapsed) return ''
    if (!selectionAnchorElement()?.closest('.lines-content, .commentary-view')) return ''
    return removeDiacriticsForSearch(sel.toString())
      .replace(/[^\p{L}\p{N}\s"״-]+/gu, ' ')
      .replace(/(?<![\p{L}\p{N}])["״-]+|["״-]+(?![\p{L}\p{N}])/gu, ' ')
      .replace(/\s+/g, ' ')
      .trim()
  }

  function prefillFromSelection(target: ContentSearch | CommentarySearch) {
    const prefill = selectionPrefill()
    if (!prefill) return
    clearFullTextSearchHighlights()
    target.query.value = prefill
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
    prefillFromSelection(contentSearch)
    nextTick(() => searchBarRef()?.focus())
  }

  // Toolbar toggle: close if open in any mode; otherwise open in the mode
  // matching where the selection/caret sits — commentary search when it is
  // in the commentary view, content search everywhere else.
  function toggleSearch() {
    if (searchVisible.value) {
      searchVisible.value = false
      return
    }
    if (selectionAnchorElement()?.closest('.commentary-view')) openCommentarySearch()
    else openContentSearch()
  }

  function openCommentarySearch() {
    if (searchVisible.value && searchMode.value === 'commentary') {
      searchVisible.value = false
      return
    }
    searchVisible.value = true
    searchMode.value = 'commentary'
    searchNavigationState.commentary = false
    prefillFromSelection(commentarySearch)
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
    toggleSearch,
    onModeChange,
    onQueryChange,
    onSearchNext,
    onSearchPrev,
  }
}
