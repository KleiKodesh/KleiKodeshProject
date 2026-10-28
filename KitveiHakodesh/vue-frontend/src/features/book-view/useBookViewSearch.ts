import { ref, shallowRef, computed, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { removeDiacriticsForSearch, stripHtmlForSearch } from '@/utils/hebrewTextProcessing'
import { ftsLinesSearch, getLineContents, linesTextScan } from '@/webview-host/seforimApi'

export interface BookViewMatch {
  lineIndex: number
  occurrenceInLine: number
}

/**
 * In-book search: every occurrence of the query as a SUBSTRING of the book's text
 * (diacritics and markup ignored), in reading order.
 *
 * The book's text is never held here for this — lines load on demand for reading.
 * The search has two sources, both answering with numbers only:
 *
 *  1. The full-text index (ftsLinesSearch, scoped to this book). It names the lines of
 *     THIS book that may hold the query; only their text is fetched, and each is checked
 *     here for the exact substring and its occurrences (the same normalization the
 *     highlighter uses).
 *  2. The service's native scan (linesTextScan) when the index cannot answer: still being
 *     built, a fragment too common to be worth it, or an alternate version showing (the
 *     index holds the merged text only). The service reads the book's lines itself, with
 *     the same normalization, and returns the matching lines and their counts; nothing is
 *     kept anywhere afterwards. The chunk under the reader is asked for first.
 *
 * Typing narrows: a query that CONTAINS the previous one can only match lines the
 * previous one matched, so only those lines are checked again — from their text kept
 * from the last search when it was fetched here, or by a scan of just those lines.
 */

/** Candidate lines per text request when the index answered. */
const CANDIDATE_TEXT_BATCH = 500
/** How often a running scan publishes its matches so far (the counter, the highlights). */
const PUBLISH_MS = 250
/** Lines around the reader scanned first when the service scans the book. */
const NEAR_READER_LINES = 2000

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
  // Shallow: a scan can hold a million matches; wrapping each in a proxy is pure cost.
  const matches = shallowRef<BookViewMatch[]>([])
  let lastPublishedAt = 0

  /** Publishes the scan so far, at most every PUBLISH_MS unless `final`. */
  function publish(accumulated: BookViewMatch[], final: boolean) {
    const now = performance.now()
    if (!final && now - lastPublishedAt < PUBLISH_MS) return
    lastPublishedAt = now
    matches.value = accumulated.slice()
  }

  function searchKey(bookId: number) {
    return `${bookId}:${options.activeVersionId() ?? 0}`
  }

  /**
   * The order to read candidate batches in: the one holding the reader's position first,
   * then the rest from the top. Matches near the viewport show at once; the rest follow.
   */
  function partsNearestFirst(partCount: number, cursorPart: number): number[] {
    const order: number[] = []
    const first = Math.min(Math.max(0, cursorPart), Math.max(0, partCount - 1))
    if (partCount > 0) order.push(first)
    for (let part = 0; part < partCount; part++) if (part !== first) order.push(part)
    return order
  }

  /** The matches of every scanned part so far, in reading order. */
  function flatten(parts: (BookViewMatch[] | undefined)[]): BookViewMatch[] {
    const all: BookViewMatch[] = []
    for (const part of parts) if (part) for (const match of part) all.push(match)
    return all
  }

  // ── Scanning ─────────────────────────────────────────────────────────────────

  // Token used to cancel an in-flight search when a new query arrives.
  let currentScanToken = 0

  /**
   * The last complete search: its needle and every line it matched — with the line's
   * stripped text when it was fetched here (the index path), null when the service
   * scanned it (only its count came back).
   */
  let lastSearch: { key: string; needle: string; lines: Map<number, string | null> } | null = null

  /**
   * A whole-book scan still running when its query was extended: the next search waits
   * for it and narrows from its answer instead of scanning the book again. The request
   * itself is never cancelled — its answer is what the extended query needs.
   */
  let pendingWholeScan: { key: string; needle: string; answer: Promise<{ lineIndexes: number[] } | null> } | null = null

  /** The previous search's matched lines, when the new needle can only match among them. */
  async function narrowingFor(key: string, needle: string): Promise<Map<number, string | null> | null> {
    const previous = lastSearch
    if (previous && previous.key === key && previous.needle.length >= 2 && needle.includes(previous.needle)) return previous.lines
    const pending = pendingWholeScan
    if (!pending || pending.key !== key || pending.needle.length < 2 || !needle.includes(pending.needle)) return null
    const answer = await pending.answer
    if (!answer) return null
    const lines = new Map<number, string | null>()
    for (const lineIndex of answer.lineIndexes) lines.set(lineIndex, null)
    return lines
  }

  /** Consecutive line indexes as (first, last) runs — what a scan of listed lines takes. */
  function runsOf(bookId: number, lineIndexes: number[]) {
    const ranges: { bookId: number; firstLineIndex: number; lastLineIndex: number }[] = []
    for (const index of lineIndexes) {
      const last = ranges[ranges.length - 1]
      if (last && last.lastLineIndex === index - 1) last.lastLineIndex = index
      else ranges.push({ bookId, firstLineIndex: index, lastLineIndex: index })
    }
    return ranges
  }

  /** Matches from a native scan's answer (lines and counts), in the answer's order. */
  function collectCounts(into: BookViewMatch[], lineIndexes: number[], counts: number[]) {
    for (let i = 0; i < lineIndexes.length; i++) {
      for (let occurrenceInLine = 0; occurrenceInLine < counts[i]!; occurrenceInLine++) into.push({ lineIndex: lineIndexes[i]!, occurrenceInLine })
    }
  }

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
    const matchedLines = new Map<number, string | null>()
    const key = searchKey(bookId)
    const narrowing = await narrowingFor(key, needle)
    if (isStale()) return
    if (narrowing) {
      // Lines whose text is here are checked here; the rest the service scans — only them.
      const toScan: number[] = []
      for (const [lineIndex, stripped] of narrowing) {
        if (stripped == null) toScan.push(lineIndex)
        else if (stripped.includes(needle)) { matchedLines.set(lineIndex, stripped); collect(accumulated, lineIndex, stripped, needle) }
      }
      if (toScan.length) {
        const scanned = await linesTextScan(scanQuery, runsOf(bookId, toScan), options.activeVersionId() ?? 0).catch(() => null)
        if (isStale()) return
        if (scanned) {
          for (const lineIndex of scanned.lineIndexes) matchedLines.set(lineIndex, null)
          collectCounts(accumulated, scanned.lineIndexes, scanned.counts)
          accumulated.sort((a, b) => a.lineIndex - b.lineIndex || a.occurrenceInLine - b.occurrenceInLine)
        }
      }
      publish(accumulated, true)
    } else {
      const answeredByIndex = options.activeVersionId() == null && await searchByIndex(bookId, needle, accumulated, matchedLines, isStale)
      if (isStale()) return
      if (!answeredByIndex) {
        accumulated.length = 0
        matchedLines.clear()
        await searchByService(bookId, scanQuery, accumulated, matchedLines, isStale)
        if (isStale()) return
      }
    }
    lastSearch = { key, needle, lines: matchedLines }

    // Anchor the current match to the nearest line at or after the current position.
    const cursor = options.currentLineIndex()
    const nearestIndex = accumulated.findIndex((m) => m.lineIndex >= cursor)
    currentMatchIdx.value = nearestIndex === -1 ? 0 : nearestIndex
  }

  /** Source 1: the index's candidate lines, confirmed on their text. False when it cannot answer. */
  async function searchByIndex(
    bookId: number, needle: string, accumulated: BookViewMatch[], matchedLines: Map<number, string | null>, isStale: () => boolean,
  ): Promise<boolean> {
    let candidates: Awaited<ReturnType<typeof ftsLinesSearch>>
    try {
      const lastLineIndex = Math.max(0, options.lineCount() - 1)
      candidates = await ftsLinesSearch(needle, [{ bookId, firstLineIndex: 0, lastLineIndex }])
    } catch {
      return false
    }
    if (isStale() || !candidates.answered) return false
    // Candidates come in reading order; their text is fetched in batches, the batch
    // holding the reader's position first.
    const batchCount = Math.ceil(candidates.lineIds.length / CANDIDATE_TEXT_BATCH)
    const cursor = options.currentLineIndex()
    let cursorBatch = 0
    while (cursorBatch + 1 < batchCount && candidates.lineIndexes[(cursorBatch + 1) * CANDIDATE_TEXT_BATCH]! <= cursor) cursorBatch++
    const parts: (BookViewMatch[] | undefined)[] = new Array(batchCount)
    for (const batch of partsNearestFirst(batchCount, cursorBatch)) {
      const i = batch * CANDIDATE_TEXT_BATCH
      const ids = candidates.lineIds.slice(i, i + CANDIDATE_TEXT_BATCH)
      const rows = await getLineContents(ids)
      if (isStale()) return true
      const textById = new Map(rows.map((row) => [row.id, row.content ?? '']))
      const found: BookViewMatch[] = []
      for (let j = 0; j < ids.length; j++) {
        const text = textById.get(ids[j]!)
        if (!text) continue
        const stripped = stripHtmlForSearch(text)
        if (!stripped.includes(needle)) continue
        matchedLines.set(candidates.lineIndexes[i + j]!, stripped)
        collect(found, candidates.lineIndexes[i + j]!, stripped, needle)
      }
      parts[batch] = found
      publish(flatten(parts), false)
    }
    accumulated.push(...flatten(parts))
    publish(accumulated, true)
    return true
  }

  /**
   * Source 2: the service scans the book's lines itself (linesTextScan). Two asks: the
   * lines around the reader first, so the count and the highlights near them land at
   * once, then the whole book.
   */
  async function searchByService(
    bookId: number, scanQuery: string, accumulated: BookViewMatch[], matchedLines: Map<number, string | null>, isStale: () => boolean,
  ) {
    const total = options.lineCount()
    if (!total) return
    const versionId = options.activeVersionId() ?? 0
    const cursor = Math.min(Math.max(0, options.currentLineIndex()), total - 1)
    const nearFirst = Math.max(0, cursor - NEAR_READER_LINES / 2)
    const nearLast = Math.min(total - 1, nearFirst + NEAR_READER_LINES - 1)
    const near = await linesTextScan(scanQuery, [{ bookId, firstLineIndex: nearFirst, lastLineIndex: nearLast }], versionId).catch(() => null)
    if (isStale()) return
    if (near) {
      const nearMatches: BookViewMatch[] = []
      collectCounts(nearMatches, near.lineIndexes, near.counts)
      publish(nearMatches, true)
    }
    const answer = linesTextScan(scanQuery, [{ bookId, firstLineIndex: 0, lastLineIndex: total - 1 }], versionId).catch(() => null)
    const pending = { key: searchKey(bookId), needle: removeDiacriticsForSearch(scanQuery.trim()), answer }
    pendingWholeScan = pending
    const whole = await answer
    if (pendingWholeScan === pending) pendingWholeScan = null
    if (isStale() || !whole) return
    for (const lineIndex of whole.lineIndexes) matchedLines.set(lineIndex, null)
    collectCounts(accumulated, whole.lineIndexes, whole.counts)
    publish(accumulated, true)
  }

  // Search whenever the debounced query changes, and again when the shown version
  // changes (a different text).
  watch(debouncedQuery, (newQuery) => { void runScan(newQuery) })
  watch(options.activeVersionId, () => {
    lastSearch = null
    pendingWholeScan = null
    if (debouncedQuery.value) void runScan(debouncedQuery.value)
  })
  watch(options.bookId, () => { lastSearch = null; pendingWholeScan = null })

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
    lastSearch = null
    pendingWholeScan = null
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
