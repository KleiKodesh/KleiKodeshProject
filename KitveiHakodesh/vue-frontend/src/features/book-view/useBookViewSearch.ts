import { ref, computed, watch, onScopeDispose } from 'vue'
import { refDebounced, useTimeoutFn } from '@vueuse/core'
import { removeDiacriticsForSearch, stripHtmlForSearch } from '@/utils/hebrewTextProcessing'
import { ftsLinesSearch, getLineContents, getLinesPaged } from '@/webview-host/seforimApi'

export interface BookViewMatch {
  lineIndex: number
  occurrenceInLine: number
}

/**
 * In-book search: every occurrence of the query as a SUBSTRING of the book's text
 * (diacritics and markup ignored), in reading order.
 *
 * The book's text is not kept in memory for this — lines load on demand for reading.
 * The search has two sources of its own:
 *
 *  1. The full-text index (ftsLinesSearch, scoped to this book). It names the lines of THIS book that may
 *     hold the query; only their text is fetched, and each is checked here for the
 *     exact substring and its occurrences (the same normalization the highlighter uses).
 *  2. The book itself, streamed, when the index cannot answer: still being built, a
 *     fragment too common to be worth it, or an alternate version showing (the index
 *     holds the merged text only). The streamed text is held for the next searches —
 *     typing refines a query keystroke by keystroke — and dropped after
 *     FALLBACK_TEXT_IDLE_MS without a search, or at once when the view goes away.
 */

/** How long the streamed text of a book outlives the last search that used it. */
const FALLBACK_TEXT_IDLE_MS = 2 * 60 * 1000
/** Lines per request when streaming the book. */
const STREAM_CHUNK_LINES = 2000
/** Candidate lines per text request when the index answered. */
const CANDIDATE_TEXT_BATCH = 500

export function useBookViewSearch(options: {
  bookId: () => number | undefined
  /** The alternate version being shown, or null for the merged text. */
  activeVersionId: () => number | null
  /** The book's line count — how far a streamed scan reads. */
  lineCount: () => number
  currentLineIndex: () => number
}) {
  const query = ref('')
  // Debounce the query to avoid searching on every keystroke.
  const debouncedQuery = refDebounced(query, 150)

  const currentMatchIdx = ref(0)
  const matches = ref<BookViewMatch[]>([])

  // ── The streamed book text (fallback source) ─────────────────────────────────

  /** Stripped text by lineIndex, for one book and version; filled as it is streamed. */
  let fallbackText: { key: string; lines: string[]; loaded: number } | null = null

  const dropFallbackTimer = useTimeoutFn(() => { fallbackText = null }, FALLBACK_TEXT_IDLE_MS, { immediate: false })
  onScopeDispose(() => {
    dropFallbackTimer.stop()
    fallbackText = null
  })

  function fallbackKey(bookId: number) {
    return `${bookId}:${options.activeVersionId() ?? 0}`
  }

  // ── Scanning ─────────────────────────────────────────────────────────────────

  // Token used to cancel an in-flight search when a new query arrives.
  let currentScanToken = 0

  /** Occurrences of `needle` in one line's stripped text, appended in order. */
  function collect(into: BookViewMatch[], lineIndex: number, stripped: string, needle: string) {
    let characterIndex = 0
    let occurrenceInLine = 0
    while ((characterIndex = stripped.indexOf(needle, characterIndex)) !== -1) {
      into.push({ lineIndex, occurrenceInLine })
      occurrenceInLine++
      characterIndex++
    }
  }

  async function runScan(scanQuery: string) {
    const token = ++currentScanToken
    const isStale = () => token !== currentScanToken
    const needle = removeDiacriticsForSearch(scanQuery.trim())
    const bookId = options.bookId()

    if (!needle || bookId == null) {
      matches.value = []
      currentMatchIdx.value = 0
      return
    }

    const accumulated: BookViewMatch[] = []
    const answeredByIndex = options.activeVersionId() == null && await searchByIndex(bookId, needle, accumulated, isStale)
    if (isStale()) return
    if (!answeredByIndex) {
      accumulated.length = 0
      await searchByStreaming(bookId, needle, accumulated, isStale)
      if (isStale()) return
    }

    // Anchor the current match to the nearest line at or after the current position.
    const cursor = options.currentLineIndex()
    const nearestIndex = accumulated.findIndex((m) => m.lineIndex >= cursor)
    currentMatchIdx.value = nearestIndex === -1 ? 0 : nearestIndex
  }

  /** Source 1: the index's candidate lines, confirmed on their text. False when it cannot answer. */
  async function searchByIndex(
    bookId: number, needle: string, accumulated: BookViewMatch[], isStale: () => boolean,
  ): Promise<boolean> {
    let candidates: Awaited<ReturnType<typeof ftsLinesSearch>>
    try {
      const lastLineIndex = Math.max(0, options.lineCount() - 1)
      candidates = await ftsLinesSearch(needle, [{ bookId, firstLineIndex: 0, lastLineIndex }])
    } catch {
      return false
    }
    if (isStale() || !candidates.answered) return false
    for (let i = 0; i < candidates.lineIds.length; i += CANDIDATE_TEXT_BATCH) {
      const ids = candidates.lineIds.slice(i, i + CANDIDATE_TEXT_BATCH)
      const rows = await getLineContents(ids)
      if (isStale()) return true
      const textById = new Map(rows.map((row) => [row.id, row.content ?? '']))
      for (let j = 0; j < ids.length; j++) {
        const text = textById.get(ids[j]!)
        if (text) collect(accumulated, candidates.lineIndexes[i + j]!, stripHtmlForSearch(text), needle)
      }
      // Publish partial results after each batch so the counter updates live.
      matches.value = [...accumulated]
    }
    matches.value = [...accumulated]
    return true
  }

  /** Source 2: the book's own lines, streamed (and kept for the next searches, see above). */
  async function searchByStreaming(
    bookId: number, needle: string, accumulated: BookViewMatch[], isStale: () => boolean,
  ) {
    const key = fallbackKey(bookId)
    if (fallbackText?.key !== key) fallbackText = { key, lines: [], loaded: 0 }
    const text = fallbackText
    // Each search keeps the text alive; it goes FALLBACK_TEXT_IDLE_MS after the last one.
    dropFallbackTimer.stop()

    const total = options.lineCount()
    let scanned = 0
    while (scanned < total) {
      if (scanned >= text.loaded) {
        const rows = await getLinesPaged(bookId, STREAM_CHUNK_LINES, text.loaded, options.activeVersionId() ?? 0).catch(() => null)
        if (isStale() || fallbackText !== text) break
        if (!rows || !rows.length) break
        for (const row of rows) text.lines[row.lineIndex] = stripHtmlForSearch(row.content ?? '')
        text.loaded += rows.length
      }
      const end = Math.min(text.loaded, total)
      for (let lineIndex = scanned; lineIndex < end; lineIndex++) {
        const stripped = text.lines[lineIndex]
        if (stripped) collect(accumulated, lineIndex, stripped, needle)
      }
      scanned = end
      // Publish partial results after each chunk so the counter updates live.
      matches.value = [...accumulated]
      // Already-held text is scanned in one go; yield between streamed chunks.
      if (scanned < total) await new Promise<void>((resolve) => setTimeout(resolve, 0))
      if (isStale()) break
    }
    dropFallbackTimer.start()
  }

  // Search whenever the debounced query changes, and again when the shown version
  // changes (a different text).
  watch(debouncedQuery, (newQuery) => { void runScan(newQuery) })
  watch(options.activeVersionId, () => {
    fallbackText = null
    if (debouncedQuery.value) void runScan(debouncedQuery.value)
  })
  // A new book: its predecessor's streamed text is useless.
  watch(options.bookId, () => { fallbackText = null })

  const matchCount = computed(() => matches.value.length)
  const currentMatch = computed(() => matches.value[currentMatchIdx.value] ?? null)
  const currentMatchLineIndex = computed(() => currentMatch.value?.lineIndex ?? -1)
  const currentMatchOccurrence = computed(() => currentMatch.value?.occurrenceInLine ?? 0)

  /**
   * Re-anchor the current match to wherever the reader is now, in the direction
   * they asked to travel: 'forward' takes the first match at or after the top of
   * the view, 'backward' the last match above it. Each wraps at the end.
   *
   * Direction matters because this runs after the reader has scrolled away, and
   * an undirected "nearest" would send Shift+Enter forwards — the opposite of the
   * key they pressed.
   */
  function gotoNearestMatch(direction: 'forward' | 'backward' = 'forward') {
    const allMatches = matches.value
    if (!allMatches.length) return
    const cursor = options.currentLineIndex()
    if (direction === 'backward') {
      // Last match strictly above the view; wrap to the final match.
      let found = -1
      for (let i = allMatches.length - 1; i >= 0; i--) {
        if (allMatches[i]!.lineIndex < cursor) { found = i; break }
      }
      currentMatchIdx.value = found === -1 ? allMatches.length - 1 : found
      return
    }
    const nearestIndex = allMatches.findIndex((m) => m.lineIndex >= cursor)
    currentMatchIdx.value = nearestIndex === -1 ? 0 : nearestIndex
  }

  function next() {
    if (matchCount.value) currentMatchIdx.value = (currentMatchIdx.value + 1) % matchCount.value
  }
  function prev() {
    if (matchCount.value)
      currentMatchIdx.value = (currentMatchIdx.value - 1 + matchCount.value) % matchCount.value
  }
  function clear() {
    query.value = ''
    currentScanToken++ // cancel any in-flight search
    matches.value = []
    currentMatchIdx.value = 0
  }

  return {
    query,
    matchCount,
    currentMatchIdx,
    currentMatchLineIndex,
    currentMatchOccurrence,
    next,
    prev,
    clear,
    gotoNearestMatch,
  }
}
