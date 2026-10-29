import { computed, ref, shallowRef } from 'vue'
import type { FastDocViewApi } from '@/webview-host/fastDocViewApi'

/** Blocks the service scans per search call (it stops sooner at the end of the known blocks). */
export const FAST_DOC_VIEW_SEARCH_BLOCKS_PER_STEP = 500
/** While the index still grows, the search waits this long before looking at new blocks. */
const WAIT_FOR_INDEX_MS = 300

export interface FastDocViewSearchOptions {
  api: FastDocViewApi
  documentId: () => string | null | undefined
  /** The reader's position: the search scans from here to the end, then wraps to the start. */
  startBlock: () => number
  /** Called with the first hit found (at or after the start, else the first after the wrap). */
  onFirstHit?: (block: number) => void
}

/**
 * In-document search over the windowed document: the service scans bounded steps of blocks and
 * returns only hit block indexes (no text). Starts at the reader's block, runs to the end, then
 * wraps from the start up to where it began - like the book view. The hit list grows step by
 * step (the count shown grows with it); a newer query (or close) cancels the running loop.
 * Never holds the document's text: highlighting happens in the rendered blocks only.
 */
export function useFastDocViewSearch(options: FastDocViewSearchOptions) {
  const query = ref('')
  /** Hit blocks, ascending. */
  const hits = shallowRef<number[]>([])
  const currentHit = ref(-1)
  const isSearching = ref(false)
  const isComplete = ref(false)
  let generation = 0

  const hitCount = computed(() => hits.value.length)
  const currentBlock = computed(() => (currentHit.value >= 0 ? hits.value[currentHit.value] : undefined))

  function insertHits(found: number[]) {
    if (found.length === 0) return
    const merged = [...new Set([...hits.value, ...found])].sort((a, b) => a - b)
    const current = currentBlock.value
    hits.value = merged
    if (current != null) currentHit.value = merged.indexOf(current)
  }

  async function run(text: string) {
    const myGeneration = ++generation
    query.value = text
    hits.value = []
    currentHit.value = -1
    isComplete.value = false
    const documentId = options.documentId()
    if (!text.trim() || !documentId) {
      isSearching.value = false
      return
    }
    isSearching.value = true
    const start = Math.max(0, options.startBlock())
    const isCurrent = () => myGeneration === generation && options.documentId() === documentId
    let reportedFirst = false
    const report = (found: number[], from: number) => {
      insertHits(found)
      if (reportedFirst || hits.value.length === 0) return
      const first = hits.value.find((block) => block >= from) ?? hits.value[0]!
      reportedFirst = true
      currentHit.value = hits.value.indexOf(first)
      options.onFirstHit?.(first)
    }
    try {
      // Phase 1: from the reader's block to the end of the document.
      let from = start
      while (isCurrent()) {
        const step = await options.api.search(documentId, text, false, from, FAST_DOC_VIEW_SEARCH_BLOCKS_PER_STEP)
        if (!isCurrent()) return
        report(step.hitBlocks, start)
        if (step.reachedEnd || step.nextBlock < 0) break
        if (step.nextBlock <= from) await new Promise((resolve) => setTimeout(resolve, WAIT_FOR_INDEX_MS))
        from = Math.max(from, step.nextBlock)
      }
      // Phase 2 (wrap): from the start up to where phase 1 began.
      from = 0
      while (isCurrent() && from < start) {
        const step = await options.api.search(documentId, text, false, from, Math.min(FAST_DOC_VIEW_SEARCH_BLOCKS_PER_STEP, start - from))
        if (!isCurrent()) return
        report(step.hitBlocks.filter((block) => block < start), 0)
        if (step.nextBlock < 0 || step.nextBlock <= from) break
        from = step.nextBlock
      }
      if (isCurrent()) isComplete.value = true
    } catch {
      // A failed step leaves the hits found so far; the reader can search again.
    } finally {
      if (myGeneration === generation) isSearching.value = false
    }
  }

  /** Moves to the next (+1) or previous (-1) hit block, wrapping; returns its block. */
  function step(direction: 1 | -1): number | undefined {
    const count = hits.value.length
    if (count === 0) return undefined
    currentHit.value = currentHit.value < 0 ? 0 : (currentHit.value + direction + count) % count
    return hits.value[currentHit.value]
  }

  function clear() {
    generation++
    query.value = ''
    hits.value = []
    currentHit.value = -1
    isSearching.value = false
    isComplete.value = false
  }

  return { query, hits, hitCount, currentHit, currentBlock, isSearching, isComplete, run, step, clear }
}
