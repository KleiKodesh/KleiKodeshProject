import { ref, shallowRef, computed, watch, onScopeDispose, readonly } from 'vue'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { linesSubstringSearch } from '@/webview-host/seforimApi'
import { SearchMatchIndex } from '@/utils/searchMatchIndex'
import type { CommentaryGroup } from './useCommentary'

/**
 * The commentary panel's search: every occurrence of the query as a SUBSTRING of the
 * panel's lines (diacritics and markup ignored), in panel order.
 *
 * The service does the whole search (linesSubstringSearch) over exactly the panel's
 * lines: it narrows with the full-text index when it can and confirms or scans the
 * text natively, and answers with the matching lines and their counts — numbers only.
 * No commentary text is held here for this.
 *
 * Typing narrows: a query that CONTAINS the previous one can only match lines the
 * previous one matched, so only those lines are searched again.
 */

/** Lines that are not real lines (the injected no-text rows) have no text to search. */
const isRealLine = (lineId: number) => lineId > 0
/** The debounce after a keystroke: a single character matches most lines of any book, so it waits longer for the next one. */
const DEBOUNCE_MS = 150
const SINGLE_CHAR_DEBOUNCE_MS = 600

type Place = { bookId: number; lineIndex: number }
type Ranges = { bookId: number; firstLineIndex: number; lastLineIndex: number }[]

export function useCommentarySearch(
  groups: () => CommentaryGroup[],
  currentFlatIndex: () => number = () => 0,
) {
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
  // The results as matching panel ROWS plus a running count of occurrences - not an object per
  // occurrence. See SearchMatchIndex for why; do not go back to a list of matches.
  const matches = shallowRef<SearchMatchIndex>(SearchMatchIndex.EMPTY)
  /** The query the current results are FOR - what the panel is highlighted with, on the result rows only. */
  const resultsQuery = ref('')

  // ── Where each line sits in the panel ────────────────────────────────────────

  /** Flat index of every real line, by `bookId:lineIndex`, and each flat index's place; rebuilt with the groups. */
  const placement = computed(() => {
    const flatIndexOf = new Map<string, number[]>()
    const placeAt = new Map<number, Place>()
    let flatIndex = 0
    for (const group of groups()) {
      flatIndex++ // header
      for (const line of group.lines) {
        if (isRealLine(line.lineId)) {
          const key = `${group.bookId}:${line.lineIndex}`
          const at = flatIndexOf.get(key)
          if (at) at.push(flatIndex)
          else flatIndexOf.set(key, [flatIndex])
          placeAt.set(flatIndex, { bookId: group.bookId, lineIndex: line.lineIndex })
        }
        flatIndex++
      }
    }
    return { flatIndexOf, placeAt }
  })

  /** Places as runs of consecutive lines per book — the ranges a search takes. */
  function runsOf(places: Iterable<Place>): Ranges {
    const sorted = [...places].sort((a, b) => a.bookId - b.bookId || a.lineIndex - b.lineIndex)
    const ranges: Ranges = []
    for (const { bookId, lineIndex } of sorted) {
      const last = ranges[ranges.length - 1]
      if (last && last.bookId === bookId && last.lastLineIndex >= lineIndex - 1) { if (lineIndex > last.lastLineIndex) last.lastLineIndex = lineIndex }
      else ranges.push({ bookId, firstLineIndex: lineIndex, lastLineIndex: lineIndex })
    }
    return ranges
  }

  // ── Searching ────────────────────────────────────────────────────────────────

  let currentSearchToken = 0

  /** The last complete search on these groups: its needle and the flat indexes it matched. */
  let lastSearch: { needle: string; flatIndexes: number[] } | null = null

  /**
   * A full-panel answer still in flight when its query was extended: the next search waits
   * for it and narrows from it instead of searching the panel again. The request itself is
   * never cancelled — its answer is what the extended query needs.
   */
  let pendingSearch: { needle: string; answer: Promise<number[] | null> } | null = null

  /** The previous search's matched lines, when the new needle can only match among them. */
  async function narrowingFor(needle: string): Promise<number[] | null> {
    const previous = lastSearch
    if (previous && previous.needle.length >= 2 && needle.includes(previous.needle)) return previous.flatIndexes
    const pending = pendingSearch
    if (!pending || pending.needle.length < 2 || !needle.includes(pending.needle)) return null
    return pending.answer
  }

  /** The results of an answer, placed in the panel: each matching line's rows and its count. */
  function matchesOf(answer: { bookIds: number[]; lineIndexes: number[]; counts: number[] }, flatIndexOf: Map<string, number[]>): SearchMatchIndex {
    const rows: number[] = []
    const counts: number[] = []
    for (let i = 0; i < answer.lineIndexes.length; i++) {
      for (const flatIndex of flatIndexOf.get(`${answer.bookIds[i]}:${answer.lineIndexes[i]}`) ?? []) {
        rows.push(flatIndex)
        counts.push(answer.counts[i]!)
      }
    }
    return SearchMatchIndex.fromRowCounts(rows, counts)
  }

  async function runSearch(searchQuery: string) {
    const token = ++currentSearchToken
    const isStale = () => token !== currentSearchToken
    const needle = removeDiacriticsForSearch(searchQuery.trim())
    const { flatIndexOf, placeAt } = placement.value
    if (!needle || !placeAt.size) {
      matches.value = SearchMatchIndex.EMPTY
      resultsQuery.value = ''
      currentMatchIdx.value = 0
      return
    }

    // The lines to search: the previous search's matches when the query only grew, else all.
    const narrowing = await narrowingFor(needle)
    if (isStale()) return
    const places = narrowing ? narrowing.map((flatIndex) => placeAt.get(flatIndex)!) : [...placeAt.values()]
    const request = places.length
      ? linesSubstringSearch(searchQuery, runsOf(places), 0).catch(() => null)
      : Promise.resolve({ bookIds: [], lineIndexes: [], counts: [] })
    // Whatever this search finds is what an extended query narrows from — keep it reachable
    // for the next keystroke even if this one is superseded before it lands.
    const matchedFlatOf = (answer: Awaited<typeof request>) => {
      if (!answer) return null
      return Array.from(matchesOf(answer, flatIndexOf).rows)
    }
    const pending = { needle, answer: request.then(matchedFlatOf) }
    pendingSearch = pending
    const answer = await request
    if (pendingSearch === pending) pendingSearch = null
    if (isStale() || !answer) return
    const found = matchesOf(answer, flatIndexOf)
    lastSearch = { needle, flatIndexes: matchedFlatOf(answer)! }
    publish(found, searchQuery)
  }

  function publish(found: SearchMatchIndex, forQuery: string) {
    resultsQuery.value = forQuery
    matches.value = found
    const nearest = found.firstAtOrAfter(currentFlatIndex())
    currentMatchIdx.value = nearest >= found.total ? 0 : nearest
  }

  watch(debouncedQuery, (searchQuery) => { void runSearch(searchQuery) })
  // The panel shows other lines (a new base line, a filter change): new lines to search.
  watch(
    () => groups().map((group) => `${group.bookId}:${group.lines.length}:${group.lines[0]?.lineId ?? 0}`).join('|'),
    () => {
      lastSearch = null
      pendingSearch = null
      if (debouncedQuery.value) void runSearch(debouncedQuery.value)
      else { matches.value = SearchMatchIndex.EMPTY; resultsQuery.value = '' }
    },
  )

  const matchCount = computed(() => matches.value.total)
  const currentMatch = computed(() => matches.value.at(currentMatchIdx.value))
  const currentMatchFlatIndex = computed(() => currentMatch.value?.row ?? -1)
  const currentMatchOccurrence = computed(() => currentMatch.value?.occurrence ?? 0)

  /**
   * Re-anchor to wherever the reader has scrolled this panel, in the direction
   * they asked to travel. Mirrors the book text's gotoNearestMatch.
   */
  function gotoNearestMatch(direction: 'forward' | 'backward' = 'forward') {
    const found = matches.value
    if (!found.total) return
    const cur = currentFlatIndex()
    if (direction === 'backward') {
      const before = found.lastBefore(cur)
      currentMatchIdx.value = before < 0 ? found.total - 1 : before
      return
    }
    const nearest = found.firstAtOrAfter(cur)
    currentMatchIdx.value = nearest >= found.total ? 0 : nearest
  }

  function next() {
    if (matchCount.value) currentMatchIdx.value = (currentMatchIdx.value + 1) % matchCount.value
  }
  function prev() {
    if (matchCount.value)
      currentMatchIdx.value = (currentMatchIdx.value - 1 + matchCount.value) % matchCount.value
  }
  function clear() {
    if (debounceTimer) { clearTimeout(debounceTimer); debounceTimer = null }
    debouncedQuery.value = ''
    resultsQuery.value = ''   // the highlights go with the closed bar, at once
    query.value = ''
    currentSearchToken++
    lastSearch = null
    pendingSearch = null
    matches.value = SearchMatchIndex.EMPTY
    currentMatchIdx.value = 0
  }

  return {
    query,
    /**
     * What the panel is HIGHLIGHTED with: the query of the current results, on their rows only
     * (`matches.has`). Not the raw `query`: every change of that re-rendered the rows in the
     * window, 50-100 ms per keystroke, delaying the next keystroke and the search behind it.
     */
    highlightQuery: readonly(resultsQuery),
    /** The results: the rows the panel is highlighted on (`has`), and the match navigation. A computed, not readonly(): readonly would deep-proxy the typed arrays. */
    matches: computed(() => matches.value),
    matchCount,
    currentMatchIdx,
    currentMatchFlatIndex,
    currentMatchOccurrence,
    next,
    prev,
    clear,
    gotoNearestMatch,
  }
}
