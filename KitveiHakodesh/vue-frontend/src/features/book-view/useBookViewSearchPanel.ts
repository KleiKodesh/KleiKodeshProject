/**
 * Search panel state and navigation for the book view.
 *
 * Orchestrates content search and commentary search behind a unified interface.
 * Owns: panel open/close, mode switching, query forwarding, match navigation,
 * and scrolling the active match into view.
 */
import { ref, computed, nextTick, watch } from 'vue'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { COMMENTARY_SLOTS, searchModeForSlot, slotForSearchMode } from './bookViewTypes'
import type { CommentarySlot, SearchMode } from './bookViewTypes'

type ContentSearch = {
  query: import('vue').Ref<string>
  matchCount: import('vue').Ref<number>
  currentMatchIdx: import('vue').Ref<number>
  currentMatchLineIndex: import('vue').Ref<number>
  currentMatchOccurrence: import('vue').Ref<number>
  gotoNearestMatch?: (direction: 'forward' | 'backward') => void
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
  gotoNearestMatch?: (direction: 'forward' | 'backward') => void
  next: () => void
  prev: () => void
  clear: () => void
}

type LinesContentInstance = {
  scrollToLine: (lineIndex: number, options?: { occurrence?: number }) => void
  focusScroller: () => void
}

type CommentaryViewInstance = {
  scrollToFlatIndex: (index: number, occurrence?: number) => void
}

export function useBookViewSearchPanel(
  contentSearch: ContentSearch,
  /** One search per commentary panel: each scans only the rows its panel renders. */
  commentarySearches: Record<CommentarySlot, CommentarySearch>,
  linesContentRef: () => LinesContentInstance | null,
  commentaryViewRefs: Record<CommentarySlot, () => CommentaryViewInstance | null>,
  searchBarRef: () => { focus: (opts?: { selectAll?: boolean }) => void } | null,
  clearFullTextSearchHighlights: () => void,
  /**
   * Counter bumped whenever the READER scrolls the book text (never when the app
   * jumps it). Watched below to re-anchor the next Enter/Shift+Enter to wherever
   * they scrolled to.
   */
  userScrollTick: import('vue').Ref<number>,
) {
  const searchVisible = ref(false)
  const searchMode = ref<SearchMode>('content')

  // Tracks whether the user has pressed next/prev at least once since the
  // panel opened or the mode changed. On the first press we jump to the
  // nearest match rather than advancing past it.
  const searchNavigationState: Record<SearchMode, boolean> = {
    content: false,
    ...(Object.fromEntries(
      COMMENTARY_SLOTS.map((slot) => [searchModeForSlot(slot), false]),
    ) as Record<Exclude<SearchMode, 'content'>, boolean>),
  }

  function searchForMode(mode: SearchMode): ContentSearch | CommentarySearch {
    const slot = slotForSearchMode(mode)
    return slot ? commentarySearches[slot] : contentSearch
  }

  // A reader scroll re-arms the re-anchor for the BOOK TEXT search. The next
  // Enter/Shift+Enter then jumps to the match nearest where they scrolled to,
  // instead of resuming from the match they left behind — which, after scrolling
  // a long way, would send them back across the whole book.
  //
  // Only 'content' is re-armed: this tick tracks the book text scroller, and each
  // commentary panel keeps its own place. Their re-anchor already reads a live
  // cursor (topVisibleFlatIndex), so a commentary scroll needs no signal here.
  watch(userScrollTick, () => {
    searchNavigationState.content = false
  })

  const activeSearch = computed(() => searchForMode(searchMode.value))
  const activeMatchCount = computed(() => activeSearch.value.matchCount.value)
  const activeMatchIdx = computed(() => activeSearch.value.currentMatchIdx.value)

  function scrollContentMatch() {
    const slot = slotForSearchMode(searchMode.value)
    if (!slot) {
      if (contentSearch.currentMatchLineIndex.value === -1) return
      // Same jump the TOC makes, refined to the matching occurrence within the line.
      linesContentRef()?.scrollToLine(contentSearch.currentMatchLineIndex.value, {
        occurrence: contentSearch.currentMatchOccurrence.value,
      })
      return
    }
    const search = commentarySearches[slot]
    if (search.currentMatchFlatIndex.value === -1) return
    commentaryViewRefs[slot]()?.scrollToFlatIndex(
      search.currentMatchFlatIndex.value,
      search.currentMatchOccurrence.value,
    )
  }

  // Element the current selection (or caret) is anchored in, if any.
  function selectionAnchorElement(): Element | null {
    const node = window.getSelection()?.anchorNode
    if (!node) return null
    return node instanceof Element ? node : node.parentElement
  }

  /**
   * Which commentary panel the selection/caret is inside, or null for the book text.
   * Every panel renders a CommentaryView, so the slot comes from the data attribute
   * each one stamps on its root rather than from the shared `.commentary-view` class.
   */
  function selectionCommentarySlot(): CommentarySlot | null {
    const host = selectionAnchorElement()?.closest('[data-commentary-slot]')
    const slot = host?.getAttribute('data-commentary-slot')
    return COMMENTARY_SLOTS.includes(slot as CommentarySlot) ? (slot as CommentarySlot) : null
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

  /**
   * Ctrl+F inside a commentary panel searches THAT panel — pressing it again in the
   * same panel closes the bar, pressing it in the other panel re-targets the bar.
   */
  function openCommentarySearch(slot: CommentarySlot) {
    const mode = searchModeForSlot(slot)
    if (searchVisible.value && searchMode.value === mode) {
      searchVisible.value = false
      return
    }
    searchVisible.value = true
    searchMode.value = mode
    searchNavigationState[mode] = false
    prefillFromSelection(commentarySearches[slot])
    nextTick(() => searchBarRef()?.focus())
  }

  // Toolbar toggle: close if open in any mode; otherwise open in the mode matching
  // where the selection/caret sits — the commentary panel it is inside, or the book
  // text everywhere else.
  function toggleSearch() {
    if (searchVisible.value) {
      searchVisible.value = false
      return
    }
    const slot = selectionCommentarySlot()
    if (slot) openCommentarySearch(slot)
    else openContentSearch()
  }

  function onModeChange(mode: SearchMode) {
    const currentQuery = activeSearch.value.query.value
    contentSearch.clear()
    for (const slot of COMMENTARY_SLOTS) commentarySearches[slot].clear()
    searchMode.value = mode
    searchNavigationState[mode] = false
    if (!currentQuery) return
    searchForMode(mode).query.value = currentQuery
  }

  function onQueryChange(query: string) {
    if (query.trim()) {
      clearFullTextSearchHighlights()
    }
    activeSearch.value.query.value = query
    searchNavigationState[searchMode.value] = false
  }

  /**
   * One step through the matches, in `direction`.
   *
   * The first press after the panel opens, after the query changes, or after the
   * READER scrolls does not advance — it re-anchors to the match nearest the
   * current view, travelling the way the key points: Enter to the first match at
   * or after the top of the view, Shift+Enter to the last one above it. Anything
   * else would ignore where the reader just navigated to.
   *
   * Every later press steps normally from there.
   */
  function stepSearch(direction: 'forward' | 'backward') {
    const search = activeSearch.value
    if (search.matchCount.value === 0) return
    if (!searchNavigationState[searchMode.value]) {
      searchNavigationState[searchMode.value] = true
      search.gotoNearestMatch?.(direction)
    } else if (direction === 'forward') {
      search.next()
    } else {
      search.prev()
    }
    scrollContentMatch()
  }

  function onSearchNext() { stepSearch('forward') }
  function onSearchPrev() { stepSearch('backward') }

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
