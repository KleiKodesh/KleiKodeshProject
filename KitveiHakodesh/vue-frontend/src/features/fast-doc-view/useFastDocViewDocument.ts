import { computed, onScopeDispose, ref, shallowRef, watch } from 'vue'
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

/** A fetch that lost its document (closed, replaced, reopened meanwhile): the scroller just asks again - never shown. */
export class FastDocViewStaleFetchError extends Error {}

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
  /** A block fetch (or the reopen after a change on disk) failed after the document was shown: the page shows
   * a banner over the text with a retry. Cleared by `retryRead` and by every open. */
  const readError = ref<unknown>(null)
  /** The failure came from reopening the file (moved, deleted, now unreadable): a retry opens it again. */
  let readErrorNeedsReopen = false
  /** Bumped when cached content is replaced under the same indexes (a reopen) - the scroller's rowSourceVersion. */
  const contentVersion = ref(0)
  /** Bumped when provisional blocks (drawn before the index was final) are dropped - the scroller asks
   * for its window again and fetches only them; with contentVersion, the scroller's rowSourceVersion. */
  const refetchVersion = ref(0)
  /** Drops provisional blocks once the index is final, so they are fetched again. */
  function dropProvisionalBlocks() {
    if (cache.dropWhere((block) => block.provisional === true) > 0) refetchVersion.value++
  }
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
  // The open still running in the service, if any: a new open or the page going away cancels it there,
  // so a big file's first index work stops instead of finishing for nobody.
  let pendingOpenId: string | null = null
  let openCounter = 0
  async function openInService(path: string) {
    const openId = `${Date.now().toString(36)}-${++openCounter}-${Math.random().toString(36).slice(2)}`
    pendingOpenId = openId
    try {
      return await api.open(path, openId)
    } finally {
      if (pendingOpenId === openId) pendingOpenId = null
    }
  }
  function cancelPendingOpen() {
    const openId = pendingOpenId
    if (!openId) return
    pendingOpenId = null
    void api.cancelOpen?.(openId).catch(() => {})
  }

  async function open(path: string, openOptions: { reachIndex?: number } = {}): Promise<void> {
    const generation = ++openGeneration
    cancelPendingOpen()
    await closeCurrent()
    if (generation !== openGeneration) return
    openedPath = path
    isLoading.value = true
    error.value = null
    readError.value = null
    readErrorNeedsReopen = false
    try {
      const opened = await openInService(path)
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
    // The margins come with the open reply, so the first paint already has them (no jump).
    if (opened.sections?.length) sections.value = opened.sections
    cache.clear()
    cacheRevision.value++
  }

  async function loadOutline(documentId: string, generation: number) {
    try {
      // Sections (the margins) are not fetched here: they arrive with the open and scan replies.
      const [loadedHeadings, loadedWeights] = await Promise.all([api.headings(documentId), api.weights(documentId)])
      if (generation !== openGeneration || summary.value?.documentId !== documentId) return
      headings.value = loadedHeadings
      weights.value = loadedWeights
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
      sections: state.sections?.length ? state.sections : current.sections,
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
      if (state.sections?.length) sections.value = state.sections
      if (state.isIndexComplete) {
        isIndexComplete.value = true
        // Blocks drawn with what only the final index knows: dropped, so the scroller asks for
        // the ones on screen again (only those - the rest are fetched when scrolled to).
        dropProvisionalBlocks()
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
        const opened = await openInService(path)
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
      if (!documentId) throw new FastDocViewStaleFetchError('FastDocView: document closed')
      beginFetch()
      let fetched
      try {
        fetched = await api.blocks(documentId, start, count)
      } finally {
        endFetch()
      }
      // Reopened or closed meanwhile: these blocks belong to the old document. The
      // reopen bumps contentVersion, so the scroller asks for its window again.
      if (summary.value?.documentId !== documentId) throw new FastDocViewStaleFetchError('FastDocView: document replaced')
      cache.store(fetched, focus, options.retainedRanges?.() ?? [])
      cacheRevision.value++
      // a fetch begun before the index completed may still bring provisional blocks: fetch them again
      if (isIndexComplete.value) dropProvisionalBlocks()
    }
  }

  /**
   * Resolves once block `index` is known (the index still being built has reached it), or once
   * it is known it never will be: the index completed or stopped short of it, or the document
   * went away. Post-flush, so the scroller's count has caught up when a landing reads it.
   */
  function whenBlockKnown(index: number): Promise<void> {
    const documentId = summary.value?.documentId
    const isSettled = () =>
      blockCount.value > index || isIndexComplete.value || indexBuildFailed.value || summary.value?.documentId !== documentId
    if (isSettled()) return Promise.resolve()
    return new Promise<void>((resolve) => {
      const stop = watch(isSettled, (settled) => {
        if (!settled) return
        stop()
        resolve()
      }, { flush: 'post' })
    })
  }

  /**
   * The scroller's row source: resolves once [start, end) is cached, rejects when it
   * cannot be had. A "source changed" error reopens the document and tries once more.
   */
  async function ensureBlocks(start: number, end: number): Promise<void> {
    if (!summary.value) throw new FastDocViewStaleFetchError('FastDocView: no document open')
    await whenBlockKnown(start)
    if (!summary.value) throw new FastDocViewStaleFetchError('FastDocView: no document open')
    const clampedEnd = Math.min(end, blockCount.value)
    const focus = [start, clampedEnd] as const
    const fetchMissing = async () => {
      for (const [spanStart, spanEnd] of cache.missingSpans(start, Math.min(end, blockCount.value))) {
        await fetchSpan(spanStart, spanEnd, focus)
      }
    }
    const generation = openGeneration
    try {
      try {
        await fetchMissing()
      } catch (caught) {
        if (!isFastDocViewSourceChangedError(caught)) throw caught
        try {
          await reopen()
        } catch (reopenFailure) {
          if (generation === openGeneration && !(reopenFailure instanceof FastDocViewStaleFetchError)) readErrorNeedsReopen = true
          throw reopenFailure
        }
        await fetchMissing()
      }
    } catch (caught) {
      // the scroller only learns "not available"; the reader is told here (a stale fetch is just asked again)
      if (generation === openGeneration && !(caught instanceof FastDocViewStaleFetchError) && readError.value == null) {
        console.error('[FastDocView] reading blocks failed', caught)
        readError.value = caught
      }
      throw caught
    }
  }

  /** The reader's "try again" after a read failure: reopens when the reopen failed, else asks for the window again. */
  async function retryRead(): Promise<void> {
    if (readError.value == null) return
    const generation = openGeneration
    const needsReopen = readErrorNeedsReopen
    readError.value = null
    readErrorNeedsReopen = false
    if (!needsReopen) {
      refetchVersion.value++
      return
    }
    try {
      await reopen()
    } catch (caught) {
      if (generation !== openGeneration || caught instanceof FastDocViewStaleFetchError) return
      readError.value = caught
      readErrorNeedsReopen = true
    }
  }

  function blockAt(index: number): FastDocViewBlock | undefined {
    void cacheRevision.value
    return cache.get(index)
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
    cancelPendingOpen()
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
    readError,
    retryRead,
    contentVersion,
    refetchVersion,
    landmarkTiers,
    rowWeights,
    open,
    ensureBlocks,
    blockAt,
    /** Opens the same file again (as after a change on disk) - diagnostics. */
    reopen,
    /** For tests and diagnostics: how many blocks are held. */
    cachedBlockCount: () => cache.size,
  }
}
