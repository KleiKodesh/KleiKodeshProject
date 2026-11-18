/**
 * The find bar of ONE commentary panel shown in its own window.
 *
 * A panel inside the book view is searched from the book view's own bar
 * (useBookViewSearchPanel), which also searches the book text and the other panels.
 * A detached panel cannot use that bar - it is in another window - so it gets a bar
 * of its own that searches only this panel. The search itself is the panel's
 * (panel.search); this file owns the bar: open/close, the query, and the steps.
 */
import { ref, watch, type Ref } from 'vue'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import type { HostWindow } from '@/composables/detachedWindow/hostWindow'

type PanelSearch = {
  query: Ref<string>
  matchCount: Ref<number>
  currentMatchFlatIndex: Ref<number>
  currentMatchOccurrence: Ref<number>
  gotoNearestMatch?: (direction: 'forward' | 'backward') => void
  next: () => void
  prev: () => void
}

type Direction = 'forward' | 'backward'

export function useCommentaryPanelFind(
  search: PanelSearch,
  scrollToMatch: (flatIndex: number, occurrence: number) => void,
  hostWindow: HostWindow,
) {
  const visible = ref(false)

  // The first step after the bar opens or the query changes goes to the match nearest
  // the place the reader is at; later steps advance from there.
  let hasStepped = false
  // A step asked for before the results arrived is carried out when they do.
  let owedStep: Direction | null = null

  /** The text selected in this window, cleaned into a query ('' when none). */
  function selectionAsQuery(): string {
    const selection = hostWindow.getSelection()
    if (!selection || selection.isCollapsed) return ''
    return removeDiacriticsForSearch(selection.toString())
      .replace(/[^\p{L}\p{N}\s"״-]+/gu, ' ')
      .replace(/(?<![\p{L}\p{N}])["״-]+|["״-]+(?![\p{L}\p{N}])/gu, ' ')
      .replace(/\s+/g, ' ')
      .trim()
  }

  function toggle() {
    visible.value = !visible.value
    if (!visible.value) return
    hasStepped = false
    owedStep = null
    const prefill = selectionAsQuery()
    if (prefill) search.query.value = prefill
  }

  function close() {
    visible.value = false
    owedStep = null
  }

  function onQueryChange(query: string) {
    search.query.value = query
    hasStepped = false
    owedStep = null
  }

  function step(direction: Direction) {
    if (search.matchCount.value === 0) {
      if (search.query.value.trim()) owedStep = direction
      return
    }
    if (!hasStepped) {
      hasStepped = true
      search.gotoNearestMatch?.(direction)
    } else if (direction === 'forward') search.next()
    else search.prev()
    if (search.currentMatchFlatIndex.value !== -1)
      scrollToMatch(search.currentMatchFlatIndex.value, search.currentMatchOccurrence.value)
  }

  watch(search.matchCount, (count) => {
    const owed = owedStep
    if (!count || !owed || !visible.value) return
    owedStep = null
    step(owed)
  })

  return {
    visible,
    toggle,
    close,
    onQueryChange,
    next: () => step('forward'),
    prev: () => step('backward'),
  }
}
