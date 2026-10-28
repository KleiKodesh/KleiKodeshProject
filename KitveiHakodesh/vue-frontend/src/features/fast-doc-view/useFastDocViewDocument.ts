import { computed, onScopeDispose, ref, shallowRef } from 'vue'
import {
  isFastDocViewSourceChangedError,
  type FastDocViewApi,
  type FastDocViewBlock,
  type FastDocViewHeading,
  type FastDocViewNote,
  type FastDocViewSummary,
} from '@/webview-host/fastDocViewApi'
import { FastDocViewBlockCache } from './fastDocViewBlockCache'

/** Blocks kept near the window. A window is a few dozen blocks; this is several windows. */
export const FAST_DOC_VIEW_BLOCK_CACHE_CAPACITY = 400
/** Largest single blocks request; a longer missing span is asked for in pieces. */
export const FAST_DOC_VIEW_MAX_BLOCKS_PER_REQUEST = 100

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
  const headings = shallowRef<FastDocViewHeading[]>([])
  const weights = shallowRef<Int32Array | null>(null)
  const isLoading = ref(false)
  const error = ref<unknown>(null)
  /** Bumped when cached content is replaced under the same indexes (a reopen) - the scroller's rowSourceVersion. */
  const contentVersion = ref(0)
  /** Bumped on every cache write so rows reading the cache re-render. */
  const cacheRevision = ref(0)

  let openedPath: string | null = null
  let openGeneration = 0
  let reopening: Promise<void> | null = null

  async function open(path: string): Promise<void> {
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
      applySummary(opened)
      void loadOutline(opened.documentId, generation)
    } catch (caught) {
      if (generation === openGeneration) error.value = caught
    } finally {
      if (generation === openGeneration) isLoading.value = false
    }
  }

  function applySummary(opened: FastDocViewSummary) {
    summary.value = opened
    blockCount.value = opened.blockCount
    cache.clear()
    cacheRevision.value++
  }

  async function loadOutline(documentId: string, generation: number) {
    try {
      const [loadedHeadings, loadedWeights] = await Promise.all([
        api.headings(documentId),
        api.weights(documentId),
      ])
      if (generation !== openGeneration) return
      headings.value = loadedHeadings
      weights.value = loadedWeights
    } catch {
      // The outline is a convenience: the text reads without it.
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
      const fetched = await api.blocks(documentId, start, count)
      // Reopened or closed meanwhile: these blocks belong to the old document. The
      // reopen bumps contentVersion, so the scroller asks for its window again.
      if (summary.value?.documentId !== documentId) throw new Error('FastDocView: document replaced')
      cache.store(fetched, focus, options.retainedRanges?.() ?? [])
      cacheRevision.value++
      // Fewer blocks than asked = the document ends sooner than the estimate said.
      if (fetched.length < count && start + fetched.length < blockCount.value) {
        blockCount.value = start + fetched.length
        return
      }
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

  /** The notes of the cached blocks in [start, end), in block order. */
  function notesInRange(start: number, end: number): FastDocViewNote[] {
    void cacheRevision.value
    const notes: FastDocViewNote[] = []
    for (let index = start; index < end; index++) {
      const block = cache.get(index)
      if (block?.notes.length) notes.push(...block.notes)
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

  /** Row weights, only while they describe exactly the scroller's rows. */
  const rowWeights = computed(() =>
    weights.value && weights.value.length === blockCount.value ? weights.value : undefined,
  )

  async function closeCurrent() {
    const documentId = summary.value?.documentId
    summary.value = null
    headings.value = []
    weights.value = null
    blockCount.value = 0
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
    headings,
    isLoading,
    error,
    contentVersion,
    landmarkTiers,
    rowWeights,
    open,
    ensureBlocks,
    blockAt,
    notesInRange,
    /** For tests and diagnostics: how many blocks are held. */
    cachedBlockCount: () => cache.size,
  }
}
