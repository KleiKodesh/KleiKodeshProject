import { onScopeDispose, ref, shallowRef, triggerRef, watch } from 'vue'
import { getBookById, getBookVersions, getHebrewBooksEditions, getLineCharCounts, getLineIds, getLinesPaged } from '@/webview-host/seforimApi'
import type { BookVersionRow, HebrewBooksEdition } from '@/webview-host/queries.types'

export interface LineItem {
  id: number
  lineIndex: number
  /** null = not fetched yet. Never rendered: the scroller asks for a range before mounting it. */
  content: string | null
}

// Lines are fetched ON DEMAND only — the windowed scroller asks for the range it is
// about to mount (ensureLines), and a whole-book action (select-all copy, export)
// asks for everything (ensureAllLines). Nothing downloads the book in the background:
// in-book search has its own source (useBookViewSearch: the full-text index, or a
// short-lived scan of its own).

// Slot granularity: what the bookkeeping tracks as fetched or in flight.
const CHUNK_SIZE = 200

// The most lines one request asks for: consecutive missing slots are fetched together
// up to this, so a large range costs a handful of round trips, not one per slot.
const MAX_FETCH_LINES = 2000

// Whole-book fetches in flight at once (ensureAllLines). Enough to keep the bridge busy
// without flooding it ahead of the reader's own window requests.
const CONCURRENT_WHOLE_BOOK_FETCHES = 3

// Text kept in memory, in lines: the slots nearest the reader's window stay, the rest
// go back to unfetched (content null, ids kept) and are fetched again when the reader
// returns. A slot that is mounted, pinned, selected or being fetched for is never
// evicted, so the budget is a target, not a cap.
const TEXT_BUDGET_LINES = 15000

// Slot states for 200-line-aligned windows (CHUNK_SIZE granularity).
const SLOT_UNFETCHED = 0
// In flight.
const SLOT_PENDING = 1
// Text is in `lines`: the only state eviction releases.
const SLOT_FETCHED = 2

export interface UseLinesOptions {
  /**
   * [start, end) line ranges whose text must stay in memory whatever the budget: the
   * scroller's mounted window, its pinned rows and the text selection. Read on every
   * eviction pass.
   */
  retainedRanges?: () => ReadonlyArray<readonly [number, number]>
  /** The ids of lines whose text was just released — for per-line stores keyed by line id. */
  onTextEvicted?: (lineIds: number[]) => void
}

/** Uint16 when every count fits (the usual case), Int32 for a line longer than 65535 characters. */
function packCounts(counts: number[]): Uint16Array | Int32Array {
  let max = 0
  for (let i = 0; i < counts.length; i++) if (counts[i]! > max) max = counts[i]!
  return max <= 0xffff ? Uint16Array.from(counts) : Int32Array.from(counts)
}

export function useLines(bookId: () => number | undefined, options: UseLinesOptions = {}) {
  // Shallow: 190k reactive proxies for a big book cost memory and every read; the
  // lines are written here only, in chunks, and each write triggers once (triggerRef).
  const lines = shallowRef<LineItem[]>([])
  // Bumped with every write to `lines`. The lines view reads it in its row slot, so
  // the mounted rows re-render when their text changes under the same index (a
  // version swap) — a shallow ref cannot tell them, and the scroller only re-fetches.
  const linesRevision = shallowRef(0)
  function linesWritten() {
    linesRevision.value++
    triggerRef(lines)
  }
  // Start OPEN: the toolbar hides these controls when false, and metadata only
  // arrives after an await. Starting false would render the toolbar without them
  // for the first frames, then pop them in and shift every icon in the centred
  // row. False is set once metadata says the book genuinely has no connections.
  const hasCommentaries = ref(true)
  const hasRelatedBooks = ref(true)
  // Every line's character count, by lineIndex: the scrollbar's static per-row height
  // estimate, fetched once per book with no text. null until it arrives, and stays
  // null on a library whose line table has no charCount column — the bar then
  // estimates from the heights it measures. A typed array: 2 or 4 bytes per line
  // instead of a boxed number each, for a value that is only ever read by index.
  const lineCharacterCounts = ref<Uint16Array | Int32Array | null>(null)
  const hasTeamim = ref(false)

  // ── Alternate versions ──────────────────────────────────────────────────────
  //
  // A version is an OVERLAY over the same line ids, not a separate book: only the
  // text of each line changes. Line ids, lineIndex, totalLines, the TOC, links and
  // highlights all stay valid, so switching re-fetches the text in place and leaves
  // the reader where they were.
  //
  // Unlike hasCommentaries/hasRelatedBooks this starts EMPTY and fails CLOSED. Those
  // two gate controls that are almost always present, so they start open to avoid a
  // visible pop-in; versions exist for a small minority of books, so showing the
  // control by default would flash it onto the toolbar of every book that has none.
  const versions = ref<BookVersionRow[]>([])
  // null = the book's own merged text. Otherwise a book_version.id.
  const activeVersionId = ref<number | null>(null)

  // The book's scanned editions on HebrewBooks. Same rules as versions: starts empty and
  // fails closed, because only some books have editions and the control must not flash in.
  const hebrewBooksEditions = ref<HebrewBooksEdition[]>([])

  let currentBookId: number | undefined
  // Per-CHUNK_SIZE-slot state — SLOT_PENDING while in flight, SLOT_FETCHED with text.
  // Prevents duplicate fetches between ensureLines(), prefetch() and ensureAllLines().
  let slotState: Uint8Array = new Uint8Array(0)
  // The fetch each in-flight slot is riding on, so ensureLines can await a range
  // that someone else (a prefetch, another window) is already loading.
  let slotFetches = new Map<number, Promise<boolean>>()
  // Where the reader is: the middle of the latest range the scroller asked for.
  // Eviction keeps the fetched slots nearest to it.
  let focusLineIndex = 0
  // Ranges an ensureLines is working on right now — never evicted mid-ensure, or the
  // check at its end would find the text it just fetched gone.
  const activeEnsures = new Set<{ start: number; end: number }>()
  // Whole-book fetches in progress: nothing is evicted until they are done.
  let wholeBookFetches = 0

  function slotOf(lineIndex: number): number {
    return Math.floor(lineIndex / CHUNK_SIZE)
  }

  function ensureSlotCapacity(count: number) {
    if (slotState.length >= count) return
    const next = new Uint8Array(count)
    next.set(slotState)
    slotState = next
  }

  function markRange(offset: number, limit: number, state: number) {
    const first = slotOf(offset)
    const last = slotOf(offset + limit - 1)
    ensureSlotCapacity(last + 1)
    for (let s = first; s <= last; s++) slotState[s] = state
  }

  function rangeFullyPending(offset: number, limit: number): boolean {
    const first = slotOf(offset)
    const last = slotOf(offset + limit - 1)
    if (slotState.length <= last) return false
    for (let s = first; s <= last; s++) if (slotState[s] === SLOT_UNFETCHED) return false
    return true
  }

  // Write a completed chunk's rows into lines.value.
  function writeRows(rows: { id: number; lineIndex: number; content: string }[]) {
    // Grow ONCE to fit the whole chunk before writing. Growing per row re-copied the entire
    // array for each of up to MAX_FETCH_LINES rows — and the opening window's
    // prefetch can land before load() pre-sizes to totalLines.
    let maxLineIndex = -1
    for (const row of rows) if (row.lineIndex > maxLineIndex) maxLineIndex = row.lineIndex
    growTo(maxLineIndex + 1)

    for (const row of rows) {
      lines.value[row.lineIndex] = { id: row.id, lineIndex: row.lineIndex, content: row.content ?? '' }
    }
    linesWritten()
  }

  /** Extend `lines` with unfetched entries up to `count`. No-op if already that long. */
  function growTo(count: number) {
    const from = lines.value.length
    if (count <= from) return
    const extra = Array.from({ length: count - from }, (_, i) => ({
      id: -(from + i + 1),
      lineIndex: from + i,
      content: null,
    }))
    lines.value = [...lines.value, ...extra]
  }

  /** Fetches a range and registers the fetch on its slots, so ensureLines can ride on it. */
  function fetchRange(bookIdAtStart: number, offset: number, limit: number): Promise<boolean> {
    const fetching = fetchAndWriteRange(bookIdAtStart, offset, limit)
    const first = slotOf(offset)
    const last = slotOf(offset + limit - 1)
    for (let slot = first; slot <= last; slot++) slotFetches.set(slot, fetching)
    void fetching.finally(() => {
      for (let slot = first; slot <= last; slot++) {
        if (slotFetches.get(slot) === fetching) slotFetches.delete(slot)
      }
    })
    return fetching
  }

  async function fetchAndWriteRange(bookIdAtStart: number, offset: number, limit: number): Promise<boolean> {
    // Captured with the book id: a version swap keeps currentBookId the same, so the
    // book guard alone would let a chunk fetched under the OLD version write its text
    // into the new one — the two versions differ line for line, which would show as a
    // page of mixed text that reads as a single version.
    const versionAtStart = activeVersionId.value
    try {
      const rows = await getLinesPaged(bookIdAtStart, limit, offset, versionAtStart ?? 0)
      if (currentBookId !== bookIdAtStart) return false
      if (activeVersionId.value !== versionAtStart) {
        // Fetched under the version just switched away from: discarded, and the slots
        // released (setVersion reset them; whoever asks next fetches the new text).
        markRange(offset, limit, SLOT_UNFETCHED)
        return false
      }
      writeRows(rows)
      markRange(offset, limit, SLOT_FETCHED)
      evictFarText()
      return true
    } catch {
      // DB error — release the slots so a later request can retry.
      if (currentBookId === bookIdAtStart && activeVersionId.value === versionAtStart) {
        markRange(offset, limit, SLOT_UNFETCHED)
      }
      return false
    }
  }

  // The data exists for THIS mounted view. Clearing currentBookId stops in-flight
  // fetches of an unmounted view from writing their rows.
  onScopeDispose(() => {
    currentBookId = undefined
  })

  // ── Eviction ────────────────────────────────────────────────────────────────

  function slotRetained(slot: number): boolean {
    const start = slot * CHUNK_SIZE
    const end = start + CHUNK_SIZE
    for (const range of activeEnsures) if (range.start < end && range.end > start) return true
    const retained = options.retainedRanges?.()
    if (retained) for (const [s, e] of retained) if (s < end && e > start) return true
    return false
  }

  /** Releases one slot's text: content back to null, ids kept, slot back to unfetched. */
  function releaseSlot(slot: number) {
    slotState[slot] = SLOT_UNFETCHED
    const all = lines.value
    const end = Math.min(all.length, (slot + 1) * CHUNK_SIZE)
    const ids: number[] = []
    for (let index = slot * CHUNK_SIZE; index < end; index++) {
      const line = all[index]
      if (line && line.content !== null) {
        line.content = null
        ids.push(line.id)
      }
    }
    if (ids.length) options.onTextEvicted?.(ids)
  }

  /**
   * Brings the fetched text back under TEXT_BUDGET_LINES, releasing the slots farthest
   * from the reader first. Runs when a fetch lands and when the scroller asks for a
   * range, so text left behind by a jump or a whole-book action goes once the reader
   * moves on.
   */
  function evictFarText() {
    if (wholeBookFetches > 0) return
    const budgetSlots = TEXT_BUDGET_LINES / CHUNK_SIZE
    const fetched: number[] = []
    for (let slot = 0; slot < slotState.length; slot++) if (slotState[slot] === SLOT_FETCHED) fetched.push(slot)
    let excess = fetched.length - budgetSlots
    if (excess <= 0) return
    const focusSlot = slotOf(focusLineIndex)
    fetched.sort((a, b) => Math.abs(b - focusSlot) - Math.abs(a - focusSlot))
    let released = false
    for (const slot of fetched) {
      if (excess <= 0) break
      if (slotRetained(slot)) continue
      releaseSlot(slot)
      released = true
      excess--
    }
    if (released) linesWritten()
  }

  // Fast path for a known scroll target (the restored position): fires the query for
  // the slot containing lineIndex the moment the target is known, before the view has
  // mounted and asked. Slot tracking makes the view's own request ride on it.
  function prefetch(lineIndex: number) {
    const id = currentBookId
    if (id == null) return
    const offset = Math.floor(lineIndex / CHUNK_SIZE) * CHUNK_SIZE
    focusLineIndex = lineIndex

    const slot = slotOf(offset)
    ensureSlotCapacity(slot + 1)
    if (slotState[slot] !== SLOT_UNFETCHED) return
    markRange(offset, CHUNK_SIZE, SLOT_PENDING)

    void fetchRange(id, offset, CHUNK_SIZE)
  }

  /**
   * Resolves once every line in [start, end) has its text — fetching whatever is
   * not loaded or in flight yet. The windowed scroller calls this BEFORE it mounts a
   * range, so a row is never on screen without its text. REJECTS when a line is
   * still without text afterwards (a failed query; the slots are released for a
   * retry), so the scroller mounts nothing rather than empty rows.
   */
  async function ensureLines(start: number, end: number): Promise<void> {
    if (end <= start) return
    focusLineIndex = (start + end) >> 1
    evictFarText()
    await ensureRange(start, end)
  }

  /** ensureLines without moving the reader's focus — whole-book fetches use it. */
  async function ensureRange(start: number, end: number): Promise<void> {
    const id = currentBookId
    if (id == null || end <= start) return
    const active = { start, end }
    activeEnsures.add(active)
    try {
      await fetchMissing(id, start, end)
    } finally {
      activeEnsures.delete(active)
    }
  }

  async function fetchMissing(id: number, start: number, end: number): Promise<void> {
    const waits: Promise<boolean>[] = []
    const lastSlot = slotOf(Math.max(start, end - 1))
    ensureSlotCapacity(lastSlot + 1)
    const maxSlotsPerFetch = MAX_FETCH_LINES / CHUNK_SIZE
    let runStart = -1
    const flushRun = (runEnd: number) => {
      if (runStart < 0) return
      const offset = runStart * CHUNK_SIZE
      const limit = (runEnd - runStart) * CHUNK_SIZE
      markRange(offset, limit, SLOT_PENDING)
      waits.push(fetchRange(id, offset, limit))
      runStart = -1
    }
    for (let slot = slotOf(start); slot <= lastSlot; slot++) {
      const inFlight = slotFetches.get(slot)
      if (inFlight || slotState[slot] !== SLOT_UNFETCHED) {
        flushRun(slot)
        if (inFlight) waits.push(inFlight)
        continue
      }
      if (runStart < 0) runStart = slot
      else if (slot - runStart >= maxSlotsPerFetch) { flushRun(slot); runStart = slot }
    }
    flushRun(lastSlot + 1)
    if (waits.length) await Promise.all(waits)
    if (currentBookId !== id) return
    for (let index = start; index < Math.min(end, lines.value.length); index++) {
      if (lines.value[index]?.content == null) throw new Error(`lines ${start}-${end} unavailable`)
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
    while (first < end && lines.value[first]!.id > 0) first++
    let last = end
    while (last > first && lines.value[last - 1]!.id > 0) last--
    if (first >= last) return
    const ids = await getLineIds(id, first, last)
    if (currentBookId !== id) return
    for (let i = 0; i < ids.length; i++) {
      const line = lines.value[first + i]
      if (line && line.id <= 0 && ids[i]! > 0) line.id = ids[i]!
    }
    linesWritten()
    for (let index = start; index < end; index++) {
      if (lines.value[index]!.id <= 0) throw new Error(`line ids ${start}-${end} unavailable`)
    }
  }

  /**
   * Resolves once EVERY line of the book has its text — for the actions that work on
   * the whole book (select-all copy, export to Word), and only when one is used.
   * Fetched in MAX_FETCH_LINES requests, a few at a time. Nothing is evicted while it
   * runs; afterwards the text stays as long as the caller's selection retains it
   * (select-all covers the whole book) and goes once the reader moves on.
   */
  async function ensureAllLines(): Promise<void> {
    const id = currentBookId
    const total = lines.value.length
    if (id == null || !total) return
    let next = 0
    const worker = async () => {
      while (next < total && currentBookId === id) {
        const start = next
        next = Math.min(total, next + MAX_FETCH_LINES)
        await ensureRange(start, next).catch(() => {})
      }
    }
    wholeBookFetches++
    try {
      await Promise.all(Array.from({ length: CONCURRENT_WHOLE_BOOK_FETCHES }, worker))
    } finally {
      wholeBookFetches--
    }
  }

  /**
   * Switch the text to an alternate version (or back to the merged text with null).
   *
   * Re-fetches every line's CONTENT while keeping the line skeleton — ids, lineIndex
   * and the array's length — exactly as it is. That is what lets the reader stay put:
   * the TOC, commentary anchors, highlights and the scroll position all key off line
   * ids and indices, none of which a version changes.
   *
   * The old text STAYS in place until the new text replaces it, line by line. The
   * mounted rows keep their height and the reader's row stays under their eyes; a
   * row that went blank would collapse, and with it the scroll position. Every slot
   * is marked unfetched, so whoever asks for a range next — the scroller re-asking for
   * the window it shows (its `rowSourceVersion`), first of all — gets the new text.
   */
  function setVersion(versionId: number | null) {
    if (activeVersionId.value === versionId) return
    // Set BEFORE resetting the slots: in-flight fetches fence on this value, so one
    // that returns under the old version is discarded instead of written.
    activeVersionId.value = versionId
    if (currentBookId == null) return
    slotState = new Uint8Array(0)
    slotFetches = new Map()
    ensureSlotCapacity(slotOf(Math.max(lines.value.length - 1, 0)) + 1)
  }

  async function load(id: number) {
    currentBookId = id
    lines.value = []
    slotState = new Uint8Array(0)
    slotFetches = new Map()
    // A new book's versions are its own; carrying the previous book's selection over
    // would read its text through an id belonging to a different book entirely.
    versions.value = []
    activeVersionId.value = null
    hebrewBooksEditions.value = []
    // Alongside the metadata, never gating it: the bar estimates from measured rows
    // until the counts land, and keeps doing so if the library has none.
    lineCharacterCounts.value = null
    void getLineCharCounts(id)
      .then((counts) => { if (currentBookId === id) lineCharacterCounts.value = counts?.length ? packCounts(counts) : null })
      .catch(() => { /* the bar's measured fallback stands */ })

    let metadataFailed = false
    const metadataPromise = getBookById(id).catch(() => {
      metadataFailed = true
      return undefined
    })

    // No text is fetched here: the view asks for the window it opens on (the
    // restored position, a TOC target, or the start) through ensureLines, and
    // useBookView prefetches that same window as soon as it is known — so the
    // first fetch is always the right one, and never line 0 on spec.
    const book = await metadataPromise
    if (currentBookId !== id) return

    const totalLines = book?.totalLines ?? 0
    hasTeamim.value = !!(book?.hasTeamim)
    // Fail OPEN on a metadata error: false would silently remove the commentary
    // buttons and force-close the panel with no indication anything went wrong
    // (the panel itself surfaces load errors). False is reserved for a book that
    // genuinely has no connections.
    hasCommentaries.value = metadataFailed || !!(
      book?.hasTargumConnection ||
      book?.hasReferenceConnection ||
      book?.hasSourceConnection ||
      book?.hasCommentaryConnection ||
      book?.hasOtherConnection
    )
    // Fails open on a metadata error for the same reason as above: the dropdown
    // is hidden when false, and a transient error should not make it vanish.
    hasRelatedBooks.value = metadataFailed || !!(
      book?.hasSourceConnection ||
      book?.hasTargumConnection ||
      book?.hasCommentaryConnection
    )

    growTo(totalLines)

    ensureSlotCapacity(slotOf(Math.max(totalLines - 1, 0)) + 1)

    // Versions are toolbar-only and most books have none, so this trails the text
    // rather than gating first paint. Failure leaves the list empty and simply hides
    // the control — nothing about reading the book depends on it. Editions are the same
    // kind of toolbar-only extra, fetched alongside rather than after.
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
    lines, linesRevision, prefetch, ensureLines, ensureLineIds, ensureAllLines,
    hasCommentaries, hasRelatedBooks, hasTeamim, lineCharacterCounts,
    versions, activeVersionId, setVersion,
    hebrewBooksEditions,
  }
}
