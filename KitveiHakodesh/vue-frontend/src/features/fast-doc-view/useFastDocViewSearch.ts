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
  /** The row at the top of the view (a block, or a note in the notes pane): the scan starts here, and the count anchors here. */
  currentBlock: () => number
  /**
   * What is searched: the document's text (`blocks`, the default) or its footnotes and endnotes
   * (`notes`, rows = the notes pane's notes). Two separate searches with separate results, like the
   * book view's lines search and commentary panel search - a note's match is never a match on the
   * block that references it (the reader could not be shown it there).
   */
  scope?: 'blocks' | 'notes'
  /**
   * Whether the document's index is final (reactive). Blocks a step reports provisional (searched while it was still
   * building, with text that depended on what only the final index knows) are searched again once this turns true -
   * the same signal that refetches provisional blocks - and their hits replaced. Omitted: always final.
   */
  isIndexComplete?: () => boolean
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
  /** This run's blocks searched over the partial index whose text may change (FastDocViewSearchStep.provisionalBlocks). */
  let provisionalBlocks = new Set<number>()
  let isRecountingProvisional = false
  /** The running query and its generation, for the provisional recount. */
  let runningText = ''
  let runningDocumentId: string | null = null

  const matchCount = computed(() => matches.value.total)

  /** One bounded scan of this search's scope. */
  const searchStep = (documentId: string, text: string, from: number, max: number) =>
    options.scope === 'notes'
      ? options.api.searchNotes(documentId, text, false, from, max)
      : options.api.search(documentId, text, false, from, max)

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

  /** New results in place of the old (a step's matches added, or blocks recounted), keeping the current match. */
  function replaceMatches(next: SearchMatchIndex) {
    // Once navigating, the current match is a MATCH (block + occurrence): matches inserted before
    // it (the wrap phase) must not shift it to another one. A recount that removed it moves to the
    // next match at or after its place.
    const current = isNavigationArmed ? matches.value.at(currentMatch.value) : null
    matches.value = next
    if (!current) {
      currentMatch.value = nearestIndex(next)
      return
    }
    const same = next.indexOf(current.row, current.occurrence)
    if (same >= 0) currentMatch.value = same
    else {
      const after = next.firstAtOrAfter(current.row + 1)
      currentMatch.value = after >= next.total ? 0 : after
    }
  }

  /** A step's results added to those found so far (the scan arrives in steps). */
  function insertMatches(found: SearchMatchIndex) {
    if (found.total === 0) return
    replaceMatches(matches.value.mergedWith(found))
  }

  /** The step's provisional blocks (those `keep` takes) are noted, and recounted at once if the index is already final. */
  function noteProvisional(step: FastDocViewSearchStep, keep: (block: number) => boolean = () => true) {
    for (const block of step.provisionalBlocks ?? []) if (keep(block)) provisionalBlocks.add(block)
    void recountProvisional()
  }

  /**
   * Once the index is final: the blocks searched provisionally are searched again (runs of adjacent blocks per call)
   * and their hits replaced - a block's count may grow, shrink or drop to none, never counted twice.
   */
  async function recountProvisional() {
    if (isRecountingProvisional || provisionalBlocks.size === 0) return
    if (!(options.isIndexComplete?.() ?? true)) return
    const myGeneration = generation
    const documentId = runningDocumentId
    const text = runningText
    if (!documentId) return
    isRecountingProvisional = true
    try {
      while (provisionalBlocks.size > 0 && myGeneration === generation && options.documentId() === documentId) {
        const blocks = [...provisionalBlocks].sort((a, b) => a - b)
        provisionalBlocks.clear()
        // every block's new count first, then one replacement: the current match moves once, to its final place
        const recounted: number[] = []
        const counts: number[] = []
        for (let i = 0; i < blocks.length;) {
          let end = i + 1
          while (end < blocks.length && blocks[end] === blocks[end - 1]! + 1 && end - i < FAST_DOC_VIEW_SEARCH_BLOCKS_PER_STEP) end++
          const first = blocks[i]!
          const count = end - i
          const step = await searchStep(documentId, text, first, count)
          if (myGeneration !== generation || options.documentId() !== documentId) return
          for (let block = first; block < first + count; block++) {
            const hit = step.hitBlocks.indexOf(block)
            recounted.push(block)
            counts.push(hit < 0 ? 0 : Math.max(1, step.hitCounts[hit] ?? 1))
          }
          i = end
        }
        replaceMatches(matches.value.withRowsRecounted(recounted, counts))
      }
    } catch {
      // A failed recount leaves the matches found so far; the reader can search again.
    } finally {
      isRecountingProvisional = false
      // a newer run noted blocks while this (older) recount was still waiting for the service
      if (myGeneration !== generation && provisionalBlocks.size > 0) void recountProvisional()
    }
  }
  if (options.isIndexComplete) watch(options.isIndexComplete, (complete) => { if (complete) void recountProvisional() })

  async function run(text: string) {
    const myGeneration = ++generation
    matches.value = SearchMatchIndex.EMPTY
    currentMatch.value = 0
    isNavigationArmed = false
    provisionalBlocks = new Set<number>()
    const documentId = options.documentId()
    runningText = text
    runningDocumentId = documentId ?? null
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
        const step = await searchStep(documentId, text, from, FAST_DOC_VIEW_SEARCH_BLOCKS_PER_STEP)
        if (!isCurrent()) return
        insertMatches(matchesOf(step))
        noteProvisional(step)
        if (step.reachedEnd || step.nextBlock < 0) break
        if (step.nextBlock <= from) await new Promise((resolve) => setTimeout(resolve, WAIT_FOR_INDEX_MS))
        from = Math.max(from, step.nextBlock)
      }
      // ...then from the start up to where it began.
      from = 0
      while (isCurrent() && from < start) {
        const step = await searchStep(documentId, text, from, Math.min(FAST_DOC_VIEW_SEARCH_BLOCKS_PER_STEP, start - from))
        if (!isCurrent()) return
        insertMatches(matchesOf(step, (block) => block < start))
        noteProvisional(step, (block) => block < start)
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
    provisionalBlocks = new Set<number>()
    matches.value = SearchMatchIndex.EMPTY
    currentMatch.value = 0
    isSearching.value = false
    isNavigationArmed = false
  }

  return { query, matches, matchCount, currentMatch, currentMatchAsBlock, isSearching, step, onReaderScroll, clear }
}
