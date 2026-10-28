import { ref, shallowRef, computed, watch, onScopeDispose } from 'vue'
import { removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { linesSubstringSearch } from '@/webview-host/seforimApi'
import type { CommentaryGroup } from './useCommentary'

export interface CommentaryMatch {
  flatIndex: number
  occurrenceInLine: number
}

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
  const matches = shallowRef<CommentaryMatch[]>([])

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

  /** Matches from an answer, placed in the panel and in panel order. */
  function matchesOf(answer: { bookIds: number[]; lineIndexes: number[]; counts: number[] }, flatIndexOf: Map<string, number[]>): CommentaryMatch[] {
    const found: CommentaryMatch[] = []
    for (let i = 0; i < answer.lineIndexes.length; i++) {
      for (const flatIndex of flatIndexOf.get(`${answer.bookIds[i]}:${answer.lineIndexes[i]}`) ?? []) {
        for (let occurrenceInLine = 0; occurrenceInLine < answer.counts[i]!; occurrenceInLine++) found.push({ flatIndex, occurrenceInLine })
      }
    }
    found.sort((a, b) => a.flatIndex - b.flatIndex || a.occurrenceInLine - b.occurrenceInLine)
    return found
  }

  async function runSearch(searchQuery: string) {
    const token = ++currentSearchToken
    const isStale = () => token !== currentSearchToken
    const needle = removeDiacriticsForSearch(searchQuery.trim())
    const { flatIndexOf, placeAt } = placement.value
    if (!needle || !placeAt.size) {
      matches.value = []
      currentMatchIdx.value = 0
      return
    }

    // The lines to search: the previous search's matches when the query only grew, else all.
    const previous = lastSearch
    const narrowing = previous && previous.needle.length >= 2 && needle.includes(previous.needle) ? previous.flatIndexes : null
    const places = narrowing ? narrowing.map((flatIndex) => placeAt.get(flatIndex)!) : [...placeAt.values()]
    const answer = places.length
      ? await linesSubstringSearch(searchQuery, runsOf(places), 0).catch(() => null)
      : { bookIds: [], lineIndexes: [], counts: [] }
    if (isStale() || !answer) return
    const found = matchesOf(answer, flatIndexOf)
    const matchedFlat: number[] = []
    for (const match of found) if (match.occurrenceInLine === 0) matchedFlat.push(match.flatIndex)
    lastSearch = { needle, flatIndexes: matchedFlat }
    publish(found)
  }

  function publish(found: CommentaryMatch[]) {
    matches.value = found
    const cursor = currentFlatIndex()
    const nearestIdx = found.findIndex((m) => m.flatIndex >= cursor)
    currentMatchIdx.value = nearestIdx === -1 ? 0 : nearestIdx
  }

  watch(debouncedQuery, (searchQuery) => { void runSearch(searchQuery) })
  // The panel shows other lines (a new base line, a filter change): new lines to search.
  watch(
    () => groups().map((group) => `${group.bookId}:${group.lines.length}:${group.lines[0]?.lineId ?? 0}`).join('|'),
    () => {
      lastSearch = null
      if (debouncedQuery.value) void runSearch(debouncedQuery.value)
      else matches.value = []
    },
  )

  const matchCount = computed(() => matches.value.length)
  const currentMatch = computed(() => matches.value[currentMatchIdx.value] ?? null)
  const currentMatchFlatIndex = computed(() => currentMatch.value?.flatIndex ?? -1)
  const currentMatchOccurrence = computed(() => currentMatch.value?.occurrenceInLine ?? 0)

  /**
   * Re-anchor to wherever the reader has scrolled this panel, in the direction
   * they asked to travel. Mirrors the book text's gotoNearestMatch.
   */
  function gotoNearestMatch(direction: 'forward' | 'backward' = 'forward') {
    const newMatches = matches.value
    if (!newMatches.length) return
    const cur = currentFlatIndex()
    if (direction === 'backward') {
      let found = -1
      for (let i = newMatches.length - 1; i >= 0; i--) {
        if (newMatches[i]!.flatIndex < cur) { found = i; break }
      }
      currentMatchIdx.value = found === -1 ? newMatches.length - 1 : found
      return
    }
    const nearestIdx = newMatches.findIndex((m) => m.flatIndex >= cur)
    currentMatchIdx.value = nearestIdx === -1 ? 0 : nearestIdx
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
    currentSearchToken++
    lastSearch = null
    matches.value = []
    currentMatchIdx.value = 0
  }

  return {
    query,
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
