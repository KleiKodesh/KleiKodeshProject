import { computed, onScopeDispose, ref, shallowRef, watch } from 'vue'
import type { FastDocViewApi, FastDocViewSearchStep } from '@/webview-host/fastDocViewApi'
import { SearchMatchIndex } from '@/utils/searchMatchIndex'

/** Blocks the service scans per search call (it stops sooner at the end of the known blocks). */
export const FAST_DOC_VIEW_SEARCH_BLOCKS_PER_STEP = 500
/** While the index still grows, the search waits this long before looking at new blocks. */
const WAIT_FOR_INDEX_MS = 300
/** The book view search's keystroke debounce (useBookViewSearch): a lone character waits longer. */
const DEBOUNCE_MS = 150
const SINGLE_CHAR_DEBOUNCE_MS = 600

export interface FastDocViewSearchOptions {
  api: FastDocViewApi
  documentId: () => string | null | undefined
  /** The block at the top of the view: the search scans from here, and the count anchors here. */
  currentBlock: () => number
}

/** One match: the block it is in and which occurrence of the query within that block (the book view's BookViewMatch). */
export interface FastDocViewMatch {
  block: number
  occurrence: number
}

/**
 * In-document search over the windowed document, behaving like the book view's search
 * (useBookViewSearch + useBookViewSearchPanel): typing never moves the view; the count shows the
 * match nearest the reader as current; the FIRST Enter (or Shift+Enter) after a new query or a
 * reader scroll re-anchors to the match nearest the view in that direction, later presses step.
 * The service scans bounded steps of blocks and returns hit block indexes with the match count of
 * each (no text); every occurrence is one match, as the book view's per-line counts. The scan
 * runs from the reader's block to the end, then wraps; a newer query cancels the running loop.
 */
export function useFastDocViewSearch(options: FastDocViewSearchOptions) {
  const query = ref('')
  /**
   * The results as matching BLOCKS plus a running count of occurrences - not an object per
   * occurrence (the book view's search keeps the same shape). See SearchMatchIndex for why;
   * do not go back to a list of matches. Match k is matches.value.at(k).
   */
  const matches = shallowRef<SearchMatchIndex>(SearchMatchIndex.EMPTY)
  const currentMatch = ref(0)
  const isSearching = ref(false)
  let generation = 0
  /** False until the first step after a query change or a reader scroll (see step). */
  let isNavigationArmed = false

  const matchCount = computed(() => matches.value.total)

  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  watch(query, (value) => {
    if (debounceTimer) clearTimeout(debounceTimer)
    const delay = value.trim().length === 1 ? SINGLE_CHAR_DEBOUNCE_MS : DEBOUNCE_MS
    debounceTimer = setTimeout(() => { debounceTimer = null; void run(value) }, delay)
  })
  onScopeDispose(() => { if (debounceTimer) clearTimeout(debounceTimer) })

  /** The match number nearest the reader: the first at or after the view's top block. */
  function nearestIndex(found: SearchMatchIndex): number {
    const nearest = found.firstAtOrAfter(options.currentBlock())
    return nearest >= found.total ? 0 : nearest
  }

  /** The step's hit blocks and their counts; `keep` filters blocks (the wrap phase). */
  function matchesOf(step: FastDocViewSearchStep, keep: (block: number) => boolean = () => true): SearchMatchIndex {
    const blocks: number[] = []
    const counts: number[] = []
    for (let i = 0; i < step.hitBlocks.length; i++) {
      const block = step.hitBlocks[i]!
      if (!keep(block)) continue
      blocks.push(block)
      counts.push(Math.max(1, step.hitCounts[i] ?? 1))
    }
    return SearchMatchIndex.fromRowCounts(blocks, counts)
  }

  /** A step's results added to those found so far (the scan arrives in steps). */
  function insertMatches(found: SearchMatchIndex) {
    if (found.total === 0) return
    // Once navigating, the current match is a MATCH (block + occurrence): matches inserted before
    // it (the wrap phase) must not shift it to another one.
    const current = isNavigationArmed ? matches.value.at(currentMatch.value) : null
    const merged = matches.value.mergedWith(found)
    matches.value = merged
    currentMatch.value = current
      ? Math.max(0, merged.indexOf(current.row, current.occurrence))
      : nearestIndex(merged)
  }

  async function run(text: string) {
    const myGeneration = ++generation
    matches.value = SearchMatchIndex.EMPTY
    currentMatch.value = 0
    isNavigationArmed = false
    const documentId = options.documentId()
    if (!text.trim() || !documentId) {
      isSearching.value = false
      return
    }
    isSearching.value = true
    const start = Math.max(0, options.currentBlock())
    const isCurrent = () => myGeneration === generation && options.documentId() === documentId
    try {
      // From the reader's block to the end of the document...
      let from = start
      while (isCurrent()) {
        const step = await options.api.search(documentId, text, false, from, FAST_DOC_VIEW_SEARCH_BLOCKS_PER_STEP)
        if (!isCurrent()) return
        insertMatches(matchesOf(step))
        if (step.reachedEnd || step.nextBlock < 0) break
        if (step.nextBlock <= from) await new Promise((resolve) => setTimeout(resolve, WAIT_FOR_INDEX_MS))
        from = Math.max(from, step.nextBlock)
      }
      // ...then from the start up to where it began.
      from = 0
      while (isCurrent() && from < start) {
        const step = await options.api.search(documentId, text, false, from, Math.min(FAST_DOC_VIEW_SEARCH_BLOCKS_PER_STEP, start - from))
        if (!isCurrent()) return
        insertMatches(matchesOf(step, (block) => block < start))
        if (step.nextBlock < 0 || step.nextBlock <= from) break
        from = step.nextBlock
      }
    } catch {
      // A failed step leaves the matches found so far; the reader can search again.
    } finally {
      if (myGeneration === generation) isSearching.value = false
    }
  }

  /**
   * One step in `direction`; returns the match to show. The first press after a query change
   * or a reader scroll re-anchors: forward to the first match at or after the view's top block,
   * backward to the last one above it. Later presses step match by match (through a block's
   * occurrences, then on to the next block) and wrap.
   */
  function step(direction: 'forward' | 'backward'): FastDocViewMatch | undefined {
    const found = matches.value
    if (found.total === 0) return undefined
    if (!isNavigationArmed) {
      isNavigationArmed = true
      if (direction === 'forward') {
        currentMatch.value = nearestIndex(found)
      } else {
        const before = found.lastBefore(options.currentBlock())
        currentMatch.value = before < 0 ? found.total - 1 : before
      }
    } else {
      currentMatch.value = (currentMatch.value + (direction === 'forward' ? 1 : -1) + found.total) % found.total
    }
    return currentMatchAsBlock()
  }

  /** The current match as its block and occurrence. */
  function currentMatchAsBlock(): FastDocViewMatch | undefined {
    const match = matches.value.at(currentMatch.value)
    return match ? { block: match.row, occurrence: match.occurrence } : undefined
  }

  /** The reader scrolled: the next step re-anchors to the view (as in the book view). */
  function onReaderScroll() {
    isNavigationArmed = false
  }

  function clear() {
    if (debounceTimer) { clearTimeout(debounceTimer); debounceTimer = null }
    generation++
    matches.value = SearchMatchIndex.EMPTY
    currentMatch.value = 0
    isSearching.value = false
    isNavigationArmed = false
  }

  return { query, matches, matchCount, currentMatch, currentMatchAsBlock, isSearching, step, onReaderScroll, clear }
}
