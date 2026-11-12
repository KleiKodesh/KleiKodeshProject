import { onScopeDispose, ref, shallowRef, triggerRef, watch } from 'vue'
import { getBookById, getBookVersions, getHebrewBooksEditions, getLineCharCounts, getLineIds, getLinesPaged } from '@/webview-host/seforimApi'
import type { BookVersionRow, HebrewBooksEdition } from '@/webview-host/queries.types'
import { LinesTable } from './linesTable'
import type { LineItem } from './linesTable'

export { LinesTable }
export type { LineItem }

// Text is fetched ON DEMAND and kept only near the reader: the windowed scroller asks
// for the range it is about to mount (ensureLines); the text of the lines around that
// window, plus the retained ranges (mounted rows, pinned rows, the selection), stays and
// everything else is released (ids kept). Copy and export read the ranges they need
// through readLines, which holds nothing once they are done.

// The most lines one request asks for.
const MAX_FETCH_LINES = 2000
// Text kept on each side of the reader's latest window.
const KEEP_MARGIN_LINES = 1000
// The fewest lines one request asks for: a short run grows into the missing lines on
// either side, so scrolling costs one request per this many lines, not one per extension.
const MIN_FETCH_LINES = 200
// Opening window the restored position prefetches, before the scroller asks.
const PREFETCH_LINES = 200
// Pages readLines has in flight at once.
const CONCURRENT_READS = 3

export interface UseLinesOptions {
  /** [start, end) ranges whose text must stay: the mounted window, pinned rows, the selection. */
  retainedRanges?: () => ReadonlyArray<readonly [number, number]>
  /** The ids of lines whose text was just released — for per-line stores keyed by line id. */
  onTextEvicted?: (lineIds: number[]) => void
}

export function useLines(bookId: () => number | undefined, options: UseLinesOptions = {}) {
  // Shallow: written here only, each write triggers once (triggerRef).
  const lines = shallowRef<LinesTable>(new LinesTable())
  // Bumped with every write: the row slot reads it, so rows re-render when their text
  // changes under the same index (a version swap), which a shallow ref cannot tell.
  const linesRevision = shallowRef(0)
  function linesWritten() {
    linesRevision.value++
    triggerRef(lines)
  }
  // Start OPEN: starting false would pop the toolbar controls in once metadata
  // arrives and shift every icon. False only when the book has no connections.
  const hasCommentaries = ref(true)
  const hasRelatedBooks = ref(true)
  // Every line's character count: the scrollbar's per-row height estimate. null until
  // it arrives, or on a library without the column (the bar then uses measured heights).
  const lineCharacterCounts = ref<Uint16Array | Int32Array | null>(null)
  const hasTeamim = ref(false)
  const isCommentary = ref(false)

  // Alternate versions: an OVERLAY over the same line ids (only the text changes), so
  // ids, the TOC, links and highlights stay valid. Starts EMPTY and fails CLOSED: few
  // books have versions, so the control must not flash onto every toolbar.
  const versions = ref<BookVersionRow[]>([])
  // null = the book's own merged text. Otherwise a book_version.id.
  const activeVersionId = ref<number | null>(null)

  // The book's scanned editions on HebrewBooks: same rules as versions.
  const hebrewBooksEditions = ref<HebrewBooksEdition[]>([])

  let currentBookId: number | undefined
  // The reader's latest window: eviction keeps the text around it.
  let focusStart = 0
  let focusEnd = 0
  // Fetches in flight, so overlapping ensureLines ride on one request. Replaced (not
  // cleared) on a version swap: a fetch under the old version must not be ridden on.
  let inFlight: { start: number; end: number; done: Promise<void> }[] = []
  // Ranges an ensureLines is working on: never evicted before its final check.
  const activeEnsures = new Set<readonly [number, number]>()

  function fetchRun(id: number, start: number, end: number): Promise<void> {
    // Captured with the book id: a version swap keeps the book, so the book guard alone
    // would let text fetched under the OLD version land in the new one.
    const version = activeVersionId.value
    const runs = inFlight
    const entry = { start, end, done: Promise.resolve() }
    entry.done = getLinesPaged(id, end - start, start, version ?? 0)
      .then((rows) => {
        if (currentBookId !== id || activeVersionId.value !== version) return
        const table = lines.value
        let maxIndex = -1
        for (const row of rows) if (row.lineIndex > maxIndex) maxIndex = row.lineIndex
        // The prefetch can land before load() sizes the table.
        table.growTo(maxIndex + 1)
        for (const row of rows) table.setText(row.lineIndex, row.id, row.content ?? '')
        linesWritten()
        evictFarText()
      })
      .catch(() => { /* the ensure's final check rejects; a later ask retries */ })
      .finally(() => { runs.splice(runs.indexOf(entry), 1) })
    runs.push(entry)
    return entry.done
  }

  // The data exists for THIS mounted view: in-flight fetches of an unmounted view are dropped.
  onScopeDispose(() => {
    currentBookId = undefined
  })

  /** Releases all text outside the reader's window + margin and the retained ranges. */
  function evictFarText() {
    const low = focusStart - KEEP_MARGIN_LINES
    const high = focusEnd + KEEP_MARGIN_LINES
    const retained = [...(options.retainedRanges?.() ?? []), ...activeEnsures]
    const released = lines.value.releaseTextsExcept(
      (index) => (index >= low && index < high) || retained.some(([start, end]) => index >= start && index < end),
    )
    if (!released.length) return
    options.onTextEvicted?.(released)
    linesWritten()
  }

  /** Fires the fetch for a known opening position before the view mounts and asks. */
  function prefetch(lineIndex: number) {
    focusStart = Math.max(0, lineIndex - PREFETCH_LINES / 2)
    focusEnd = focusStart + PREFETCH_LINES
    void ensureRange(focusStart, focusEnd).catch(() => {})
  }

  /**
   * Resolves once every line in [start, end) has its text. The windowed scroller calls
   * this BEFORE it mounts a range, so a row is never on screen without its text. REJECTS
   * when a line is still without text afterwards (a failed query), so the scroller mounts
   * nothing rather than empty rows.
   */
  async function ensureLines(start: number, end: number): Promise<void> {
    if (end <= start) return
    focusStart = start
    focusEnd = end
    evictFarText()
    await ensureRange(start, end)
  }

  async function ensureRange(start: number, end: number): Promise<void> {
    const id = currentBookId
    if (id == null || end <= start) return
    const active = [start, end] as const
    activeEnsures.add(active)
    try {
      const table = lines.value
      const ridingOn = (index: number) => inFlight.find((run) => index >= run.start && index < run.end)
      const isMissing = (index: number) =>
        index >= 0 && index < Math.max(table.length, end) && !table.hasText(index) && !ridingOn(index)
      const waits: Promise<void>[] = []
      let index = start
      while (index < end) {
        if (table.hasText(index)) { index++; continue }
        const run = ridingOn(index)
        if (run) { waits.push(run.done); index = run.end; continue }
        let runStart = index
        let runEnd = index + 1
        while (runEnd < end && runEnd - runStart < MAX_FETCH_LINES && isMissing(runEnd)) runEnd++
        index = runEnd
        while (runEnd - runStart < MIN_FETCH_LINES && isMissing(runEnd)) runEnd++
        while (runEnd - runStart < MIN_FETCH_LINES && isMissing(runStart - 1)) runStart--
        waits.push(fetchRun(id, runStart, runEnd))
      }
      await Promise.all(waits)
      if (currentBookId !== id) return
      for (let i = start; i < Math.min(end, lines.value.length); i++) {
        if (!lines.value.hasText(i)) throw new Error(`lines ${start}-${end} unavailable`)
      }
    } finally {
      activeEnsures.delete(active)
    }
  }

  /**
   * Resolves once every line in [start, end) has its ID — fetching ids alone (no text)
   * for whatever is not loaded. A TOC section's commentary and a shift-click range
   * need the ids only; a section can be tens of thousands of lines, and reading its
   * text for the ids was a whole section downloaded per TOC click. Lines that get an
   * id this way stay unfetched for the scroller (content null) and are fetched with
   * their text later as usual. Rejects when ids are still missing afterwards.
   */
  async function ensureLineIds(start: number, end: number): Promise<void> {
    const id = currentBookId
    if (id == null) return
    end = Math.min(end, lines.value.length)
    if (end <= start) return
    let first = start
    while (first < end && lines.value.hasId(first)) first++
    let last = end
    while (last > first && lines.value.hasId(last - 1)) last--
    if (first >= last) return
    const ids = await getLineIds(id, first, last)
    if (currentBookId !== id) return
    for (let i = 0; i < ids.length && first + i < lines.value.length; i++) lines.value.setId(first + i, ids[i]!)
    linesWritten()
    for (let index = start; index < end; index++) {
      if (!lines.value.hasId(index)) throw new Error(`line ids ${start}-${end} unavailable`)
    }
  }

  /**
   * The lines of [start, end) for a copy or an export: the loaded text where it is
   * current, the rest fetched page by page. Nothing is written to the table, so the
   * caller holds the text only as long as it needs it. Rejects when the book or the
   * version changes under it.
   */
  async function readLines(start: number, end: number): Promise<LineItem[]> {
    const id = currentBookId
    const version = activeVersionId.value
    const table = lines.value
    end = Math.min(end, table.length)
    start = Math.max(0, start)
    if (id == null || end <= start) return []
    const out: LineItem[] = new Array(end - start)
    const missing: [number, number][] = []
    for (let i = start; i < end; i++) {
      if (table.hasText(i)) { out[i - start] = table.at(i)!; continue }
      const last = missing[missing.length - 1]
      if (last && last[1] === i && i - last[0] < MAX_FETCH_LINES) last[1]++
      else missing.push([i, i + 1])
    }
    let next = 0
    const worker = async () => {
      while (next < missing.length) {
        const [runStart, runEnd] = missing[next++]!
        const rows = await getLinesPaged(id, runEnd - runStart, runStart, version ?? 0)
        if (currentBookId !== id || activeVersionId.value !== version) throw new Error('book or version changed')
        for (const row of rows) {
          if (row.lineIndex >= start && row.lineIndex < end) out[row.lineIndex - start] = { id: row.id, lineIndex: row.lineIndex, content: row.content ?? '' }
        }
      }
    }
    await Promise.all(Array.from({ length: CONCURRENT_READS }, worker))
    return out.filter(Boolean)
  }

  /**
   * Switch the text to an alternate version (or back to the merged text with null).
   * Ids, lineIndex and the table's length stay, so the reader stays put. The old text
   * stays SHOWN until the new text replaces it (a blank row would collapse and lose the
   * position), but no longer counts as loaded: the scroller re-asking its window (its
   * `rowSourceVersion`) fetches the new text.
   */
  function setVersion(versionId: number | null) {
    if (activeVersionId.value === versionId) return
    // Set first: in-flight fetches fence on it and are discarded when they land.
    activeVersionId.value = versionId
    inFlight = []
    lines.value.markAllStale()
  }

  async function load(id: number) {
    currentBookId = id
    lines.value = new LinesTable()
    inFlight = []
    versions.value = []
    activeVersionId.value = null
    hebrewBooksEditions.value = []
    lineCharacterCounts.value = null
    void getLineCharCounts(id)
      .then((counts) => { if (currentBookId === id) lineCharacterCounts.value = counts?.length ? counts : null })
      .catch(() => { /* the bar's measured fallback stands */ })

    let metadataFailed = false
    const metadataPromise = getBookById(id).catch(() => {
      metadataFailed = true
      return undefined
    })

    // No text here: the view asks for the window it opens on (ensureLines / prefetch).
    const book = await metadataPromise
    if (currentBookId !== id) return

    const totalLines = book?.totalLines ?? 0
    hasTeamim.value = !!(book?.hasTeamim)
    isCommentary.value = !!(book?.isCommentary)
    // Fail OPEN on a metadata error: false would silently hide the controls.
    hasCommentaries.value = metadataFailed || !!(
      book?.hasTargumConnection ||
      book?.hasReferenceConnection ||
      book?.hasSourceConnection ||
      book?.hasCommentaryConnection ||
      book?.hasOtherConnection
    )
    hasRelatedBooks.value = metadataFailed || !!(
      book?.hasSourceConnection ||
      book?.hasTargumConnection ||
      book?.hasCommentaryConnection
    )

    lines.value.growTo(totalLines)
    linesWritten()

    // Toolbar-only extras: trail the text; a failure just hides the control.
    void getHebrewBooksEditions(id).then((rows) => {
      if (currentBookId === id) hebrewBooksEditions.value = rows
    })
    try {
      const rows = await getBookVersions(id)
      if (currentBookId === id) versions.value = rows
    } catch {
      if (currentBookId === id) versions.value = []
    }
  }

  watch(
    () => bookId(),
    (id) => {
      if (id != null) load(id)
    },
    { immediate: true },
  )

  return {
    lines, linesRevision, prefetch, ensureLines, ensureLineIds, readLines,
    hasCommentaries, hasRelatedBooks, hasTeamim, isCommentary, lineCharacterCounts,
    versions, activeVersionId, setVersion,
    hebrewBooksEditions,
  }
}
