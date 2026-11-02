import { ref, shallowRef, watch, type Ref } from 'vue'
import { isFastDocViewSourceChangedError, type FastDocViewApi, type FastDocViewSummary } from '@/webview-host/fastDocViewApi'
import { FastDocViewBlockCache } from './fastDocViewBlockCache'
import type { FastDocViewPlacedNote } from './useFastDocViewDocument'

/** Notes kept near the notes pane's window (each a note's HTML). Several windows; never all notes. */
export const FAST_DOC_VIEW_NOTE_CACHE_CAPACITY = 300
/** Largest single notes request (the service clamps to 100); a longer missing span goes in pieces. */
export const FAST_DOC_VIEW_MAX_NOTES_PER_REQUEST = 100
/** While a first open builds its index, the note -> block list is refreshed at most this often (ms). */
const NOTE_BLOCKS_REFRESH_MS = 500

/** A note row of the notes pane: its index among all the document's notes (body order). */
export type FastDocViewIndexedNote = FastDocViewPlacedNote & { index: number }

export interface FastDocViewNotesOptions {
  api: FastDocViewApi
  summary: Ref<FastDocViewSummary | null>
  /** The notes pane's mounted rows: never evicted. */
  retainedRanges?: () => ReadonlyArray<readonly [number, number]>
}

/**
 * The notes pane's own row source: every note of the document in body order, as rows of
 * a WindowedScroller (the same concept as the text pane's blocks). Holds only an Int32 per
 * note (the block holding its reference - index data, no text) and a bounded cache of the
 * notes' HTML near the pane's window. While a first open builds its index, the rows are the
 * notes known so far (the count only grows; never an estimate).
 */
export function useFastDocViewNotes(options: FastDocViewNotesOptions) {
  const { api } = options
  const cache = new FastDocViewBlockCache<FastDocViewIndexedNote>(FAST_DOC_VIEW_NOTE_CACHE_CAPACITY)
  /** Per known note (body order): the block holding its reference. Non-decreasing. */
  const noteBlocks = shallowRef<Int32Array>(new Int32Array(0))
  const noteCount = ref(0)
  const cacheRevision = ref(0)
  /** Bumped when the notes are replaced under the same indexes (a reopen) - the scroller's rowSourceVersion. */
  const contentVersion = ref(0)

  let loadedFor: string | null = null
  let refreshTimer: ReturnType<typeof setTimeout> | null = null
  let refreshing: Promise<void> | null = null

  async function refreshNoteBlocks(documentId: string) {
    try {
      const blocks = await api.noteBlocks(documentId)
      if (options.summary.value?.documentId !== documentId) return
      // Known notes only grow while the index builds: never move the count back for a stale answer.
      if (blocks.length >= noteBlocks.value.length) {
        noteBlocks.value = blocks
        noteCount.value = blocks.length
      }
    } catch {
      // The notes pane is a convenience: the text reads without it.
    }
  }

  function scheduleRefresh(documentId: string) {
    if (refreshTimer) return
    refreshTimer = setTimeout(() => {
      refreshTimer = null
      refreshing = refreshNoteBlocks(documentId).finally(() => (refreshing = null))
    }, NOTE_BLOCKS_REFRESH_MS)
  }

  // A new document (or a reopen): start over. Grown note counts (index building): refresh the list.
  watch(
    () => {
      const summary = options.summary.value
      return summary ? [summary.documentId, summary.footnoteCount + summary.endnoteCount, summary.isIndexComplete] as const : null
    },
    (state) => {
      if (!state) {
        reset()
        return
      }
      const [documentId, total, isComplete] = state
      if (documentId !== loadedFor) {
        reset()
        loadedFor = documentId
        contentVersion.value++
        if (total > 0) refreshing = refreshNoteBlocks(documentId).finally(() => (refreshing = null))
        return
      }
      if (isComplete) {
        if (refreshTimer) clearTimeout(refreshTimer)
        refreshTimer = null
        refreshing = refreshNoteBlocks(documentId).finally(() => (refreshing = null))
      } else if (total > noteCount.value) scheduleRefresh(documentId)
    },
    { immediate: true },
  )

  function reset() {
    if (refreshTimer) clearTimeout(refreshTimer)
    refreshTimer = null
    loadedFor = null
    noteBlocks.value = new Int32Array(0)
    noteCount.value = 0
    cache.clear()
    cacheRevision.value++
  }

  async function fetchSpan(first: number, end: number, focus: readonly [number, number]) {
    for (let start = first; start < end; start += FAST_DOC_VIEW_MAX_NOTES_PER_REQUEST) {
      const count = Math.min(FAST_DOC_VIEW_MAX_NOTES_PER_REQUEST, end - start)
      const documentId = options.summary.value?.documentId
      if (!documentId) throw new Error('FastDocView: document closed')
      const fetched = await api.notes(documentId, start, count)
      if (options.summary.value?.documentId !== documentId) throw new Error('FastDocView: document replaced')
      const blocks = noteBlocks.value
      cache.store(
        fetched.map((note, offset) => ({ ...note, index: start + offset, blockIndex: blocks[start + offset] ?? 0 })),
        focus,
        options.retainedRanges?.() ?? [],
      )
      cacheRevision.value++
      if (fetched.length < count) break   // clamped to the notes known so far
    }
  }

  /** The notes scroller's row source: resolves once notes [start, end) are cached. */
  async function ensureNotes(start: number, end: number): Promise<void> {
    const clampedEnd = Math.min(end, noteCount.value)
    const focus = [start, clampedEnd] as const
    try {
      for (const [spanStart, spanEnd] of cache.missingSpans(start, clampedEnd)) await fetchSpan(spanStart, spanEnd, focus)
    } catch (caught) {
      // A changed file reopens through the text's next blocks fetch; the new summary resets these rows.
      if (!isFastDocViewSourceChangedError(caught)) throw caught
    }
  }

  function noteAt(index: number): FastDocViewIndexedNote | undefined {
    void cacheRevision.value
    return cache.get(index)
  }

  /** The first note whose reference is at or after block `blockIndex` (noteCount when none). */
  function firstNoteAtOrAfter(blockIndex: number): number {
    const blocks = noteBlocks.value
    let low = 0
    let high = blocks.length
    while (low < high) {
      const middle = (low + high) >> 1
      if (blocks[middle]! < blockIndex) low = middle + 1
      else high = middle
    }
    return low
  }

  /** The notes whose references sit in block `blockIndex`: [first, end). */
  function notesOfBlock(blockIndex: number): [number, number] {
    return [firstNoteAtOrAfter(blockIndex), firstNoteAtOrAfter(blockIndex + 1)]
  }

  /**
   * The index of the note a reference in block `blockIndex` points at: the notes of that block
   * are fetched (a handful) and matched by kind / number / label; `occurrence` (the reference's
   * place among the block's references) is the fallback. Null when the note is not known yet.
   */
  async function findNote(blockIndex: number, occurrence: number, matches: (note: FastDocViewIndexedNote) => boolean): Promise<number | null> {
    if (refreshing) await refreshing
    const [first, end] = notesOfBlock(blockIndex)
    if (first >= end) return null
    await ensureNotes(first, end).catch(() => {})
    for (let index = first; index < end; index++) {
      const note = cache.get(index)
      if (note && matches(note)) return index
    }
    return Math.min(first + occurrence, end - 1)
  }

  return {
    noteCount,
    contentVersion,
    ensureNotes,
    noteAt,
    noteBlockAt: (index: number) => noteBlocks.value[index],
    firstNoteAtOrAfter,
    findNote,
    /** For tests and diagnostics: how many notes' HTML are held. */
    cachedNoteCount: () => cache.size,
  }
}
