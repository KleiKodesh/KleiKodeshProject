import { computed, onScopeDispose, ref, shallowRef, watch } from 'vue'
import type { FastDocViewApi } from '@/webview-host/fastDocViewApi'

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

/**
 * In-document search over the windowed document, behaving like the book view's search
 * (useBookViewSearch + useBookViewSearchPanel): typing never moves the view; the count shows the
 * match nearest the reader as current; the FIRST Enter (or Shift+Enter) after a new query or a
 * reader scroll re-anchors to the match nearest the view in that direction, later presses step.
 * The service scans bounded steps of blocks and returns hit block indexes only (no text); the scan
 * runs from the reader's block to the end, then wraps; a newer query cancels the running loop.
 */
export function useFastDocViewSearch(options: FastDocViewSearchOptions) {
  const query = ref('')
  /** Hit blocks, ascending. */
  const hits = shallowRef<number[]>([])
  const currentHit = ref(0)
  const isSearching = ref(false)
  let generation = 0
  /** False until the first step after a query change or a reader scroll (see step). */
  let isNavigationArmed = false

  const hitCount = computed(() => hits.value.length)

  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  watch(query, (value) => {
    if (debounceTimer) clearTimeout(debounceTimer)
    const delay = value.trim().length === 1 ? SINGLE_CHAR_DEBOUNCE_MS : DEBOUNCE_MS
    debounceTimer = setTimeout(() => { debounceTimer = null; void run(value) }, delay)
  })
  onScopeDispose(() => { if (debounceTimer) clearTimeout(debounceTimer) })

  /** The index of the hit nearest the reader: the first at or after the view's top. */
  function nearestIndex(found: number[]): number {
    const cursor = options.currentBlock()
    const index = found.findIndex((block) => block >= cursor)
    return index === -1 ? 0 : index
  }

  function insertHits(found: number[]) {
    if (found.length === 0) return
    // Once navigating, the current match is a BLOCK: hits inserted before it (the wrap phase)
    // must not shift it to another one.
    const currentBlock = isNavigationArmed ? hits.value[currentHit.value] : undefined
    const merged = [...new Set([...hits.value, ...found])].sort((a, b) => a - b)
    hits.value = merged
    currentHit.value = currentBlock != null ? merged.indexOf(currentBlock) : nearestIndex(merged)
  }

  async function run(text: string) {
    const myGeneration = ++generation
    hits.value = []
    currentHit.value = 0
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
        insertHits(step.hitBlocks)
        if (step.reachedEnd || step.nextBlock < 0) break
        if (step.nextBlock <= from) await new Promise((resolve) => setTimeout(resolve, WAIT_FOR_INDEX_MS))
        from = Math.max(from, step.nextBlock)
      }
      // ...then from the start up to where it began.
      from = 0
      while (isCurrent() && from < start) {
        const step = await options.api.search(documentId, text, false, from, Math.min(FAST_DOC_VIEW_SEARCH_BLOCKS_PER_STEP, start - from))
        if (!isCurrent()) return
        insertHits(step.hitBlocks.filter((block) => block < start))
        if (step.nextBlock < 0 || step.nextBlock <= from) break
        from = step.nextBlock
      }
    } catch {
      // A failed step leaves the hits found so far; the reader can search again.
    } finally {
      if (myGeneration === generation) isSearching.value = false
    }
  }

  /**
   * One step in `direction`; returns the hit block to show. The first press after a query
   * change or a reader scroll re-anchors: forward to the first hit at or after the view's top,
   * backward to the last one above it. Later presses step and wrap.
   */
  function step(direction: 'forward' | 'backward'): number | undefined {
    const list = hits.value
    if (list.length === 0) return undefined
    if (!isNavigationArmed) {
      isNavigationArmed = true
      const cursor = options.currentBlock()
      if (direction === 'forward') {
        currentHit.value = nearestIndex(list)
      } else {
        let found = -1
        for (let index = list.length - 1; index >= 0; index--) if (list[index]! < cursor) { found = index; break }
        currentHit.value = found === -1 ? list.length - 1 : found
      }
    } else {
      currentHit.value = (currentHit.value + (direction === 'forward' ? 1 : -1) + list.length) % list.length
    }
    return list[currentHit.value]
  }

  /** The reader scrolled: the next step re-anchors to the view (as in the book view). */
  function onReaderScroll() {
    isNavigationArmed = false
  }

  function clear() {
    if (debounceTimer) { clearTimeout(debounceTimer); debounceTimer = null }
    generation++
    hits.value = []
    currentHit.value = 0
    isSearching.value = false
    isNavigationArmed = false
  }

  return { query, hits, hitCount, currentHit, isSearching, step, onReaderScroll, clear }
}
