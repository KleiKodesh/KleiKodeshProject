import { ref, shallowRef, computed, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { removeDiacriticsForSearch, stripHtmlForSearch } from '@/utils/hebrewTextProcessing'
import { ftsLinesSearch, getLineContents, linesTextScan } from '@/webview-host/seforimApi'
import type { CommentaryGroup } from './useCommentary'

export interface CommentaryMatch {
  flatIndex: number
  occurrenceInLine: number
}

/**
 * The commentary panel's search: every occurrence of the query as a SUBSTRING of the
 * panel's lines (diacritics and markup ignored), in panel order.
 *
 * Commentary text is fetched on demand for reading, so the search has sources of its own,
 * both answering with numbers only:
 *
 *  1. The full-text index (ftsLinesSearch), scoped to the panel's lines — one range per
 *     commentator group (its book's lines are one block in the index). Only the text of
 *     the lines it names is fetched, and each is checked here for the exact substring.
 *  2. The service's native scan (linesTextScan) of exactly the panel's lines when the
 *     index cannot answer (still being built, a fragment too common): it returns the
 *     matching lines and their counts, and keeps nothing.
 *
 * Typing narrows: a query that CONTAINS the previous one can only match lines the
 * previous one matched, so only those are checked again — from their text kept from
 * the last search when it was fetched here, or by a scan of just those lines.
 */

/** Line ids per text request. */
const TEXT_BATCH = 1000

/** Lines that are not real lines (the injected no-text rows) have no text to search. */
const isRealLine = (lineId: number) => lineId > 0

export function useCommentarySearch(
  groups: () => CommentaryGroup[],
  currentFlatIndex: () => number = () => 0,
) {
  const query = ref('')
  const debouncedQuery = refDebounced(query, 150)
  const currentMatchIdx = ref(0)
  const matches = shallowRef<CommentaryMatch[]>([])

  // ── Where each line sits in the panel ────────────────────────────────────────

  /** Flat index of every real line, by `bookId:lineIndex`; rebuilt with the groups. */
  const placement = computed(() => {
    const flatIndexOf = new Map<string, number[]>()
    const lineIdAt = new Map<number, number>()
    const placeAt = new Map<number, { bookId: number; lineIndex: number }>()
    let flatIndex = 0
    for (const group of groups()) {
      flatIndex++ // header
      for (const line of group.lines) {
        if (isRealLine(line.lineId)) {
          const key = `${group.bookId}:${line.lineIndex}`
          const at = flatIndexOf.get(key)
          if (at) at.push(flatIndex)
          else flatIndexOf.set(key, [flatIndex])
          lineIdAt.set(flatIndex, line.lineId)
          placeAt.set(flatIndex, { bookId: group.bookId, lineIndex: line.lineIndex })
        }
        flatIndex++
      }
    }
    return { flatIndexOf, lineIdAt, placeAt }
  })

  /** Ascending (book, lineIndex) places as runs of consecutive lines — the ranges a search takes. */
  function runsOf(places: Iterable<{ bookId: number; lineIndex: number }>) {
    const sorted = [...places].sort((a, b) => a.bookId - b.bookId || a.lineIndex - b.lineIndex)
    const ranges: { bookId: number; firstLineIndex: number; lastLineIndex: number }[] = []
    for (const { bookId, lineIndex } of sorted) {
      const last = ranges[ranges.length - 1]
      if (last && last.bookId === bookId && last.lastLineIndex >= lineIndex - 1) { if (lineIndex > last.lastLineIndex) last.lastLineIndex = lineIndex }
      else ranges.push({ bookId, firstLineIndex: lineIndex, lastLineIndex: lineIndex })
    }
    return ranges
  }

  /** The panel's lines as index ranges: per group, its book's lowest to highest lineIndex. */
  function lineRanges() {
    const ranges: { bookId: number; firstLineIndex: number; lastLineIndex: number }[] = []
    for (const group of groups()) {
      let first = Infinity
      let last = -Infinity
      for (const line of group.lines) {
        if (!isRealLine(line.lineId)) continue
        if (line.lineIndex < first) first = line.lineIndex
        if (line.lineIndex > last) last = line.lineIndex
      }
      if (first <= last) ranges.push({ bookId: group.bookId, firstLineIndex: first, lastLineIndex: last })
    }
    return ranges
  }

  /** Stripped text for these line ids: the model's loaded text, else fetched. */
  async function textsFor(lineIds: number[]): Promise<Map<number, string>> {
    const texts = new Map<number, string>()
    const loaded = new Map<number, string>()
    for (const group of groups()) for (const line of group.lines) if (line.content) loaded.set(line.lineId, line.content)
    const missing: number[] = []
    for (const id of lineIds) {
      const content = loaded.get(id)
      if (content !== undefined) texts.set(id, stripHtmlForSearch(content))
      else missing.push(id)
    }
    for (let i = 0; i < missing.length; i += TEXT_BATCH) {
      const batch = missing.slice(i, i + TEXT_BATCH)
      const rows = await getLineContents(batch)
      for (const row of rows) texts.set(row.id, stripHtmlForSearch(row.content ?? ''))
      for (const id of batch) if (!texts.has(id)) texts.set(id, '')
    }
    return texts
  }

  // ── Searching ────────────────────────────────────────────────────────────────

  let currentSearchToken = 0

  /**
   * The last complete search on these groups: its needle and every matched flat index —
   * with the line's stripped text when it was fetched here, null when the service scanned it.
   */
  let lastSearch: { needle: string; lines: Map<number, string | null> } | null = null

  /** Matches from a native scan's answer, placed in the panel; `into` is sorted by the caller. */
  function collectScanned(into: CommentaryMatch[], matched: Map<number, string | null>, flatIndexOf: Map<string, number[]>, scanned: { bookIds: number[]; lineIndexes: number[]; counts: number[] }) {
    for (let i = 0; i < scanned.lineIndexes.length; i++) {
      for (const flatIndex of flatIndexOf.get(`${scanned.bookIds[i]}:${scanned.lineIndexes[i]}`) ?? []) {
        matched.set(flatIndex, null)
        for (let occurrenceInLine = 0; occurrenceInLine < scanned.counts[i]!; occurrenceInLine++) into.push({ flatIndex, occurrenceInLine })
      }
    }
  }

  function collect(into: CommentaryMatch[], flatIndex: number, stripped: string, needle: string) {
    let characterIndex = 0
    let occurrenceInLine = 0
    while ((characterIndex = stripped.indexOf(needle, characterIndex)) !== -1) {
      into.push({ flatIndex, occurrenceInLine })
      occurrenceInLine++
      characterIndex++
    }
  }

  async function runSearch(searchQuery: string) {
    const token = ++currentSearchToken
    const isStale = () => token !== currentSearchToken
    const needle = removeDiacriticsForSearch(searchQuery.trim())
    const { flatIndexOf, lineIdAt, placeAt } = placement.value
    if (!needle || !lineIdAt.size) {
      matches.value = []
      currentMatchIdx.value = 0
      return
    }

    const previous = lastSearch
    if (previous && previous.needle.length >= 2 && needle.includes(previous.needle)) {
      const found: CommentaryMatch[] = []
      const matched = new Map<number, string | null>()
      const toScan: { bookId: number; lineIndex: number }[] = []
      for (const [flatIndex, text] of previous.lines) {
        if (text == null) toScan.push(placeAt.get(flatIndex)!)
        else if (text.includes(needle)) { matched.set(flatIndex, text); collect(found, flatIndex, text, needle) }
      }
      if (toScan.length) {
        const scanned = await linesTextScan(searchQuery, runsOf(toScan), 0).catch(() => null)
        if (isStale()) return
        if (scanned) collectScanned(found, matched, flatIndexOf, scanned)
        found.sort((a, b) => a.flatIndex - b.flatIndex || a.occurrenceInLine - b.occurrenceInLine)
      }
      lastSearch = { needle, lines: matched }
      publish(found)
      return
    }

    // The lines to check, as flat indexes: the index's candidates, or every line.
    let flatIndexes: number[] | null = null
    try {
      const candidates = await ftsLinesSearch(needle, lineRanges())
      if (isStale()) return
      if (candidates.answered) {
        const set = new Set<number>()
        for (let i = 0; i < candidates.lineIds.length; i++) {
          for (const flatIndex of flatIndexOf.get(`${candidates.bookIds[i]}:${candidates.lineIndexes[i]}`) ?? []) set.add(flatIndex)
        }
        flatIndexes = [...set].sort((first, second) => first - second)
      }
    } catch { /* the index is unavailable: read the lines */ }
    const found: CommentaryMatch[] = []
    const matched = new Map<number, string | null>()
    if (!flatIndexes) {
      // The index could not answer: the service scans exactly the panel's lines.
      const scanned = await linesTextScan(searchQuery, runsOf(placeAt.values()), 0).catch(() => null)
      if (isStale()) return
      if (scanned) collectScanned(found, matched, flatIndexOf, scanned)
      found.sort((a, b) => a.flatIndex - b.flatIndex || a.occurrenceInLine - b.occurrenceInLine)
      lastSearch = { needle, lines: matched }
      publish(found)
      return
    }

    let texts: Map<number, string>
    try {
      texts = await textsFor(flatIndexes.map((flatIndex) => lineIdAt.get(flatIndex)!))
    } catch {
      texts = new Map()
    }
    if (isStale()) return
    for (const flatIndex of flatIndexes) {
      const text = texts.get(lineIdAt.get(flatIndex)!)
      if (!text || !text.includes(needle)) continue
      matched.set(flatIndex, text)
      collect(found, flatIndex, text, needle)
    }
    lastSearch = { needle, lines: matched }
    publish(found)
  }

  function publish(found: CommentaryMatch[]) {
    matches.value = found
    const cursor = currentFlatIndex()
    const nearestIdx = found.findIndex((m) => m.flatIndex >= cursor)
    currentMatchIdx.value = nearestIdx === -1 ? 0 : nearestIdx
  }

  watch(debouncedQuery, (searchQuery) => { void runSearch(searchQuery) })
  // The panel shows other lines (a new base line, a filter change): new text to search,
  // and the held text belongs to the old lines.
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
