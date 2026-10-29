import { computed, onScopeDispose, ref, shallowRef } from 'vue'
import {
  isFastDocViewSourceChangedError,
  type FastDocViewApi,
  type FastDocViewBlock,
  type FastDocViewHeading,
  type FastDocViewNote,
  type FastDocViewScanState,
  type FastDocViewSection,
  type FastDocViewSummary,
} from '@/webview-host/fastDocViewApi'
import { FastDocViewBlockCache } from './fastDocViewBlockCache'

/** Blocks kept near the window. A window is a few dozen blocks; this is several windows. */
export const FAST_DOC_VIEW_BLOCK_CACHE_CAPACITY = 400
/** Largest single blocks request; a longer missing span is asked for in pieces. */
export const FAST_DOC_VIEW_MAX_BLOCKS_PER_REQUEST = 100
/** Index-building step on a first open: the most blocks one fastDocViewScan call may index.
 * The service also stops each call after a few ms, so a block fetch never waits long behind it. */
export const FAST_DOC_VIEW_INDEX_BLOCKS_PER_SCAN = 5000
/** A failed scan call is retried after these delays (ms); after the last one the index stops
 * growing and `indexBuildFailed` is set. A changed file is not retried here (it reopens). */
export const FAST_DOC_VIEW_SCAN_RETRY_DELAYS_MS: readonly number[] = [500, 1000, 2000, 4000, 8000]

/** A note with the block whose text references it - for jumping between reference and note. */
export type FastDocViewPlacedNote = FastDocViewNote & { blockIndex: number }

export interface FastDocViewDocumentOptions {
  api: FastDocViewApi
  /** The ranges that must stay cached whatever the budget (mounted window, pinned rows). */
  retainedRanges?: () => ReadonlyArray<readonly [number, number]>
  cacheCapacity?: number
}

/**
 * One open FastDocView document: open/close, the bounded block cache behind the
 * scroller's `ensureRows`, and the outline (headings), row weights and notes.
 * Holds blocks near the window only - never the document's whole text.
 */
export function useFastDocViewDocument(options: FastDocViewDocumentOptions) {
  const { api } = options
  const cache = new FastDocViewBlockCache(options.cacheCapacity ?? FAST_DOC_VIEW_BLOCK_CACHE_CAPACITY)

  const summary = shallowRef<FastDocViewSummary | null>(null)
  const blockCount = ref(0)
  /** False while a first open is still building its index; the count only grows meanwhile. */
  const isIndexComplete = ref(true)
  /** The background index build gave up after its retries: the count stops at what is known. */
  const indexBuildFailed = ref(false)
  const headings = shallowRef<FastDocViewHeading[]>([])
  const weights = shallowRef<Int32Array | null>(null)
  /** Page text width per section (block order); empty until loaded. */
  const sections = shallowRef<FastDocViewSection[]>([])
  const isLoading = ref(false)
  const error = ref<unknown>(null)
  /** Bumped when cached content is replaced under the same indexes (a reopen) - the scroller's rowSourceVersion. */
  const contentVersion = ref(0)
  /** Bumped on every cache write so rows reading the cache re-render. */
  const cacheRevision = ref(0)

  let openedPath: string | null = null
  let openGeneration = 0
  let reopening: Promise<void> | null = null
  /** Block fetches in flight: the index scan waits while any is pending, so the screen comes first. */
  let pendingFetches = 0
  let fetchesIdle: Promise<void> | null = null
  let resolveFetchesIdle: (() => void) | null = null

  function beginFetch() {
    if (pendingFetches++ === 0) fetchesIdle = new Promise((resolve) => (resolveFetchesIdle = resolve))
  }
  function endFetch() {
    if (--pendingFetches === 0) {
      resolveFetchesIdle?.()
      fetchesIdle = null
      resolveFetchesIdle = null
    }
  }

  /**
   * Opens `path`. `reachIndex` (a restored position): while the index is incomplete, scan
   * until that block is known BEFORE the document is shown, or the reader lands elsewhere.
   */
  async function open(path: string, openOptions: { reachIndex?: number } = {}): Promise<void> {
    const generation = ++openGeneration
    await closeCurrent()
    if (generation !== openGeneration) return
    openedPath = path
    isLoading.value = true
    error.value = null
    try {
      const opened = await api.open(path)
      if (generation !== openGeneration) {
        void api.close(opened.documentId).catch(() => {})
        return
      }
      let summaryToShow = opened
      const reachIndex = openOptions.reachIndex ?? 0
      let complete = opened.isIndexComplete
      let known = opened.blockCount
      while (!complete && known <= reachIndex) {
        const state = await api.scan(opened.documentId, FAST_DOC_VIEW_INDEX_BLOCKS_PER_SCAN)
        if (generation !== openGeneration) {
          void api.close(opened.documentId).catch(() => {})
          return
        }
        complete = state.isIndexComplete
        known = Math.max(known, state.knownBlockCount)
        summaryToShow = withScanState(summaryToShow, state)
      }
      applySummary(summaryToShow)
      void loadOutline(opened.documentId, generation)
      if (!summaryToShow.isIndexComplete) void buildIndexInBackground(opened.documentId, generation)
    } catch (caught) {
      if (generation === openGeneration) error.value = caught
    } finally {
      if (generation === openGeneration) isLoading.value = false
    }
  }

  function applySummary(opened: FastDocViewSummary) {
    summary.value = opened
    blockCount.value = opened.blockCount
    isIndexComplete.value = opened.isIndexComplete
    cache.clear()
    cacheRevision.value++
  }

  async function loadOutline(documentId: string, generation: number) {
    try {
      const [loadedHeadings, loadedWeights, loadedSections] = await Promise.all([
        api.headings(documentId),
        api.weights(documentId),
        api.sections(documentId).catch(() => [] as FastDocViewSection[]),
      ])
      if (generation !== openGeneration || summary.value?.documentId !== documentId) return
      headings.value = loadedHeadings
      weights.value = loadedWeights
      sections.value = loadedSections
    } catch {
      // The outline is a convenience: the text reads without it.
    }
  }

  /**
   * A first open returns after its first blocks: keep building the index, one scan call at a
   * time, until it is complete. Stops when the document is replaced, closed or a call fails
   * (a changed file reopens through the next blocks fetch). On completion the outline and
   * weights are fetched again in full.
   */
  function withScanState(current: FastDocViewSummary, state: FastDocViewScanState): FastDocViewSummary {
    return {
      ...current,
      blockCount: Math.max(current.blockCount, state.knownBlockCount),
      knownBlockCount: Math.max(current.knownBlockCount, state.knownBlockCount),
      isIndexComplete: state.isIndexComplete,
      footnoteCount: state.footnoteCount,
      endnoteCount: state.endnoteCount,
    }
  }

  async function buildIndexInBackground(documentId: string, generation: number) {
    const isCurrent = () => generation === openGeneration && summary.value?.documentId === documentId
    indexBuildFailed.value = false
    let failures = 0
    while (isCurrent() && !isIndexComplete.value) {
      // The screen first: never queue a scan call while a block fetch is waiting.
      while (fetchesIdle) await fetchesIdle
      if (!isCurrent()) return
      let state
      try {
        state = await api.scan(documentId, FAST_DOC_VIEW_INDEX_BLOCKS_PER_SCAN)
        failures = 0
      } catch (caught) {
        // A changed file (or a forgotten id) reopens through the next blocks fetch, which
        // starts a new build; anything else is retried with backoff, then reported.
        if (isFastDocViewSourceChangedError(caught) || !isCurrent()) return
        if (failures >= FAST_DOC_VIEW_SCAN_RETRY_DELAYS_MS.length) {
          console.error('[FastDocView] index build stopped after retries', caught)
          indexBuildFailed.value = true
          return
        }
        await new Promise((resolve) => setTimeout(resolve, FAST_DOC_VIEW_SCAN_RETRY_DELAYS_MS[failures++]))
        continue
      }
      if (!isCurrent()) return
      // The count only grows: never move it back for a stale answer. Note counts grow too.
      if (state.knownBlockCount > blockCount.value) blockCount.value = state.knownBlockCount
      if (summary.value) summary.value = withScanState(summary.value, state)
      if (state.isIndexComplete) {
        isIndexComplete.value = true
        await loadOutline(documentId, generation)
      }
    }
  }

  /** The file changed on disk (or the service forgot the id): open it again, once for all waiters. */
  function reopen(): Promise<void> {
    if (reopening) return reopening
    const path = openedPath
    const generation = openGeneration
    reopening = (async () => {
      try {
        if (!path) return
        const staleDocumentId = summary.value?.documentId
        const opened = await api.open(path)
        if (generation !== openGeneration) {
          void api.close(opened.documentId).catch(() => {})
          return
        }
        if (staleDocumentId) void api.close(staleDocumentId).catch(() => {})
        applySummary(opened)
        contentVersion.value++
        void loadOutline(opened.documentId, generation)
        if (!opened.isIndexComplete) void buildIndexInBackground(opened.documentId, generation)
      } finally {
        reopening = null
      }
    })()
    return reopening
  }

  async function fetchSpan(first: number, end: number, focus: readonly [number, number]) {
    for (let start = first; start < end; start += FAST_DOC_VIEW_MAX_BLOCKS_PER_REQUEST) {
      const count = Math.min(FAST_DOC_VIEW_MAX_BLOCKS_PER_REQUEST, end - start)
      const documentId = summary.value?.documentId
      if (!documentId) throw new Error('FastDocView: document closed')
      beginFetch()
      let fetched
      try {
        fetched = await api.blocks(documentId, start, count)
      } finally {
        endFetch()
      }
      // Reopened or closed meanwhile: these blocks belong to the old document. The
      // reopen bumps contentVersion, so the scroller asks for its window again.
      if (summary.value?.documentId !== documentId) throw new Error('FastDocView: document replaced')
      cache.store(fetched, focus, options.retainedRanges?.() ?? [])
      cacheRevision.value++
    }
  }

  /**
   * The scroller's row source: resolves once [start, end) is cached, rejects when it
   * cannot be had. A "source changed" error reopens the document and tries once more.
   */
  async function ensureBlocks(start: number, end: number): Promise<void> {
    if (!summary.value) throw new Error('FastDocView: no document open')
    const clampedEnd = Math.min(end, blockCount.value)
    const focus = [start, clampedEnd] as const
    const fetchMissing = async () => {
      for (const [spanStart, spanEnd] of cache.missingSpans(start, Math.min(end, blockCount.value))) {
        await fetchSpan(spanStart, spanEnd, focus)
      }
    }
    try {
      await fetchMissing()
    } catch (caught) {
      if (!isFastDocViewSourceChangedError(caught)) throw caught
      await reopen()
      await fetchMissing()
    }
  }

  function blockAt(index: number): FastDocViewBlock | undefined {
    void cacheRevision.value
    return cache.get(index)
  }

  /** The notes of the cached blocks in [start, end), in block order, each with the block holding its reference. */
  function notesInRange(start: number, end: number): FastDocViewPlacedNote[] {
    void cacheRevision.value
    const notes: FastDocViewPlacedNote[] = []
    for (let index = start; index < end; index++) {
      const block = cache.get(index)
      if (block?.notes.length) for (const note of block.notes) notes.push({ ...note, blockIndex: index })
    }
    return notes
  }

  /** Heading starts by level, coarse first - the scrollbar's landmark tiers. */
  const landmarkTiers = computed<number[][]>(() => {
    const byLevel = new Map<number, number[]>()
    for (const heading of headings.value) {
      let tier = byLevel.get(heading.level)
      if (!tier) byLevel.set(heading.level, (tier = []))
      tier.push(heading.blockIndex)
    }
    return [...byLevel.entries()]
      .sort(([first], [second]) => first - second)
      .map(([, tier]) => tier.sort((first, second) => first - second))
  })

  /** The page text width (twips) of the section holding block `index`, or undefined before sections load. */
  function textWidthTwipsAt(index: number): number | undefined {
    const list = sections.value
    if (list.length === 0) return undefined
    let low = 0
    let high = list.length - 1
    while (low < high) {
      const middle = (low + high + 1) >> 1
      if (list[middle]!.firstBlockIndex <= index) low = middle
      else high = middle - 1
    }
    return list[low]!.textWidthTwips
  }

  /** Row weights, only while they describe exactly the scroller's rows. */
  const rowWeights = computed(() =>
    weights.value && weights.value.length === blockCount.value ? weights.value : undefined,
  )

  async function closeCurrent() {
    const documentId = summary.value?.documentId
    summary.value = null
    headings.value = []
    weights.value = null
    sections.value = []
    blockCount.value = 0
    isIndexComplete.value = true
    indexBuildFailed.value = false
    cache.clear()
    if (documentId) await api.close(documentId).catch(() => {})
  }

  onScopeDispose(() => {
    openGeneration++
    void closeCurrent()
  })

  return {
    summary,
    blockCount,
    isIndexComplete,
    indexBuildFailed,
    headings,
    sections,
    textWidthTwipsAt,
    isLoading,
    error,
    contentVersion,
    landmarkTiers,
    rowWeights,
    open,
    ensureBlocks,
    blockAt,
    notesInRange,
    /** Opens the same file again (as after a change on disk) - diagnostics. */
    reopen,
    /** For tests and diagnostics: how many blocks are held. */
    cachedBlockCount: () => cache.size,
  }
}
