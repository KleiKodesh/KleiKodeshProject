import { ref, computed, watch, onScopeDispose } from 'vue'
import { refDebounced, useTimeoutFn } from '@vueuse/core'
import { removeDiacriticsForSearch, stripHtmlForSearch } from '@/utils/hebrewTextProcessing'
import { ftsLinesSearch, getLineContents } from '@/webview-host/seforimApi'
import type { CommentaryGroup } from './useCommentary'

export interface CommentaryMatch {
  flatIndex: number
  occurrenceInLine: number
}

/**
 * The commentary panel's search: every occurrence of the query as a SUBSTRING of the
 * panel's lines (diacritics and markup ignored), in panel order.
 *
 * Commentary text is fetched on demand for reading, so the search has sources of its own:
 *
 *  1. The full-text index (ftsLinesSearch), scoped to the panel's lines — one range per
 *     commentator group (its book's lines are one block in the index). Only the text of
 *     the lines it names is fetched, and each is checked here for the exact substring.
 *  2. The panel's lines themselves, fetched, when the index cannot answer (still being
 *     built, a fragment too common). That text is held for the next searches — typing
 *     refines a query keystroke by keystroke — and dropped after FALLBACK_TEXT_IDLE_MS
 *     without a search, when the panel's lines change, or when the panel goes away.
 */

/** How long the fetched text of a panel's lines outlives the last search that used it. */
const FALLBACK_TEXT_IDLE_MS = 2 * 60 * 1000
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
  const matches = ref<CommentaryMatch[]>([])

  // ── Where each line sits in the panel ────────────────────────────────────────

  /** Flat index of every real line, by `bookId:lineIndex`; rebuilt with the groups. */
  const placement = computed(() => {
    const flatIndexOf = new Map<string, number[]>()
    const lineIdAt = new Map<number, number>()
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
        }
        flatIndex++
      }
    }
    return { flatIndexOf, lineIdAt }
  })

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

  // ── The fetched panel text (fallback source) ─────────────────────────────────

  /** Stripped text by line id, for the panel's current lines. */
  let fallbackText: Map<number, string> | null = null
  const dropFallbackTimer = useTimeoutFn(() => { fallbackText = null }, FALLBACK_TEXT_IDLE_MS, { immediate: false })
  onScopeDispose(() => {
    dropFallbackTimer.stop()
    fallbackText = null
  })

  /** Stripped text for these line ids: the held text, the model's loaded text, else fetched. */
  async function textsFor(lineIds: number[], hold: boolean): Promise<Map<number, string>> {
    const texts = hold ? (fallbackText ??= new Map()) : new Map<number, string>()
    const loaded = new Map<number, string>()
    for (const group of groups()) for (const line of group.lines) if (line.content) loaded.set(line.lineId, line.content)
    const missing: number[] = []
    for (const id of lineIds) {
      if (texts.has(id)) continue
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
    const { flatIndexOf, lineIdAt } = placement.value
    if (!needle || !lineIdAt.size) {
      matches.value = []
      currentMatchIdx.value = 0
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
    const answered = flatIndexes != null
    if (!flatIndexes) flatIndexes = [...lineIdAt.keys()]

    // The index's candidates are few and fetched per search; the whole panel's text is
    // held (see above) for the searches that follow.
    if (!answered) dropFallbackTimer.stop()
    let texts: Map<number, string>
    try {
      texts = await textsFor(flatIndexes.map((flatIndex) => lineIdAt.get(flatIndex)!), !answered)
    } catch {
      texts = new Map()
    }
    if (!answered) dropFallbackTimer.start()
    if (isStale()) return

    const found: CommentaryMatch[] = []
    for (const flatIndex of flatIndexes) {
      const text = texts.get(lineIdAt.get(flatIndex)!)
      if (text) collect(found, flatIndex, text, needle)
    }
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
      fallbackText = null
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
