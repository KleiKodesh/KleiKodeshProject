import { ref, shallowRef, computed, watch, onScopeDispose } from 'vue'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { linesSubstringSearch } from '@/webview-host/seforimApi'

export interface BookViewMatch {
  lineIndex: number
  occurrenceInLine: number
}

/**
 * In-book search: every occurrence of the query as a SUBSTRING of the book's text
 * (diacritics and markup ignored), in reading order.
 *
 * The service does the whole search (linesSubstringSearch): it narrows with the full-text
 * index when it can and confirms or scans the text natively, with the same normalization
 * the highlighter uses, and answers with the matching lines and their counts — numbers
 * only. No book text is ever held here for this.
 *
 * Two asks per fresh query: the lines around the reader first, so the count and the
 * highlights near them land at once, then the whole book. Typing narrows: a query that
 * CONTAINS the previous one can only match lines the previous one matched, so only those
 * lines are searched again — and a whole-book answer still in flight when the query was
 * extended is waited for and narrowed from, not thrown away.
 */

/** Lines around the reader searched first. */
const NEAR_READER_LINES = 2000
/** The debounce after a keystroke: a single character matches most lines of any book, so it waits longer for the next one. */
const DEBOUNCE_MS = 150
const SINGLE_CHAR_DEBOUNCE_MS = 600

type Ranges = { bookId: number; firstLineIndex: number; lastLineIndex: number }[]

export function useBookViewSearch(options: {
  bookId: () => number | undefined
  /** The alternate version being shown, or null for the merged text. */
  activeVersionId: () => number | null
  /** The book's line count — how far a search reaches. */
  lineCount: () => number
  currentLineIndex: () => number
}) {
  const query = ref('')
  // The query a search runs for, settled: a keystroke waits DEBOUNCE_MS for the next one,
  // a lone character SINGLE_CHAR_DEBOUNCE_MS (it is almost always the start of a word).
  const debouncedQuery = ref('')
  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  watch(query, (value) => {
    if (debounceTimer) clearTimeout(debounceTimer)
    const delay = value.trim().length === 1 ? SINGLE_CHAR_DEBOUNCE_MS : DEBOUNCE_MS
    debounceTimer = setTimeout(() => { debounceTimer = null; debouncedQuery.value = value }, delay)
  })
  onScopeDispose(() => { if (debounceTimer) clearTimeout(debounceTimer) })

  const currentMatchIdx = ref(0)
  // Shallow: a search can hold a million matches; wrapping each in a proxy is pure cost.
  const matches = shallowRef<BookViewMatch[]>([])

  function searchKey(bookId: number) {
    return `${bookId}:${options.activeVersionId() ?? 0}`
  }

  // ── Searching ────────────────────────────────────────────────────────────────

  // Token used to cancel an in-flight search when a new query arrives.
  let currentScanToken = 0

  /** The last complete search: its needle and the line indexes it matched (ascending). */
  let lastSearch: { key: string; needle: string; lineIndexes: number[] } | null = null

  /**
   * A whole-book answer still in flight when its query was extended: the next search
   * waits for it and narrows from it instead of searching the book again. The request
   * itself is never cancelled — its answer is what the extended query needs.
   */
  let pendingWholeSearch: { key: string; needle: string; answer: Promise<{ lineIndexes: number[] } | null> } | null = null

  /** The previous search's matched lines, when the new needle can only match among them. */
  async function narrowingFor(key: string, needle: string): Promise<number[] | null> {
    const previous = lastSearch
    if (previous && previous.key === key && previous.needle.length >= 2 && needle.includes(previous.needle)) return previous.lineIndexes
    const pending = pendingWholeSearch
    if (!pending || pending.key !== key || pending.needle.length < 2 || !needle.includes(pending.needle)) return null
    const answer = await pending.answer
    return answer ? answer.lineIndexes : null
  }

  /** Ascending line indexes as (first, last) runs — the ranges a search of listed lines takes. */
  function runsOf(bookId: number, lineIndexes: number[]): Ranges {
    const ranges: Ranges = []
    for (const index of lineIndexes) {
      const last = ranges[ranges.length - 1]
      if (last && last.lastLineIndex === index - 1) last.lastLineIndex = index
      else ranges.push({ bookId, firstLineIndex: index, lastLineIndex: index })
    }
    return ranges
  }

  /** Matches from an answer (lines and counts), in the answer's order. */
  function matchesOf(lineIndexes: number[], counts: number[]): BookViewMatch[] {
    const all: BookViewMatch[] = []
    for (let i = 0; i < lineIndexes.length; i++) {
      for (let occurrenceInLine = 0; occurrenceInLine < counts[i]!; occurrenceInLine++) all.push({ lineIndex: lineIndexes[i]!, occurrenceInLine })
    }
    return all
  }

  function publish(found: BookViewMatch[]) {
    matches.value = found
    // Anchor the current match to the nearest line at or after the current position.
    const cursor = options.currentLineIndex()
    const nearestIndex = found.findIndex((m) => m.lineIndex >= cursor)
    currentMatchIdx.value = nearestIndex === -1 ? 0 : nearestIndex
  }

  async function runScan(scanQuery: string) {
    const token = ++currentScanToken
    const isStale = () => token !== currentScanToken
    const needle = removeDiacriticsForSearch(scanQuery.trim())
    const bookId = options.bookId()
    const total = options.lineCount()

    if (!needle || bookId == null || !total) {
      matches.value = []
      currentMatchIdx.value = 0
      return
    }
    const key = searchKey(bookId)
    const versionId = options.activeVersionId() ?? 0

    const narrowing = await narrowingFor(key, needle)
    if (isStale()) return
    if (narrowing) {
      const answer = narrowing.length
        ? await linesSubstringSearch(scanQuery, runsOf(bookId, narrowing), versionId).catch(() => null)
        : { lineIndexes: [], counts: [] }
      if (isStale() || !answer) return
      lastSearch = { key, needle, lineIndexes: answer.lineIndexes }
      publish(matchesOf(answer.lineIndexes, answer.counts))
      return
    }

    // The lines around the reader first, then the whole book.
    const cursor = Math.min(Math.max(0, options.currentLineIndex()), total - 1)
    const nearFirst = Math.max(0, cursor - NEAR_READER_LINES / 2)
    const nearLast = Math.min(total - 1, nearFirst + NEAR_READER_LINES - 1)
    const near = await linesSubstringSearch(scanQuery, [{ bookId, firstLineIndex: nearFirst, lastLineIndex: nearLast }], versionId).catch(() => null)
    if (isStale()) return
    if (near) publish(matchesOf(near.lineIndexes, near.counts))

    const answer = linesSubstringSearch(scanQuery, [{ bookId, firstLineIndex: 0, lastLineIndex: total - 1 }], versionId).catch(() => null)
    const pending = { key, needle, answer }
    pendingWholeSearch = pending
    const whole = await answer
    if (pendingWholeSearch === pending) pendingWholeSearch = null
    if (isStale() || !whole) return
    lastSearch = { key, needle, lineIndexes: whole.lineIndexes }
    publish(matchesOf(whole.lineIndexes, whole.counts))
  }

  // Search whenever the debounced query changes, and again when the shown version
  // changes (a different text).
  watch(debouncedQuery, (newQuery) => { void runScan(newQuery) })
  watch(options.activeVersionId, () => {
    lastSearch = null
    pendingWholeSearch = null
    if (debouncedQuery.value) void runScan(debouncedQuery.value)
  })
  watch(options.bookId, () => { lastSearch = null; pendingWholeSearch = null })

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
    pendingWholeSearch = null
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
