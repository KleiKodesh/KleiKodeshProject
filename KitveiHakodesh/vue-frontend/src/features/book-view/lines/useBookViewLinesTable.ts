import { onScopeDispose, ref, watch } from 'vue'
import { getBookById, getLinesPaged } from '@/webview-host/seforimApi'

export interface LineItem {
  id: number
  lineIndex: number
  content: string | null // null = placeholder, not yet loaded
}

// Chunk size for user-facing fetches (first paint, scroll target, TOC jump).
// Small so the visible window arrives fast.
const CHUNK_SIZE = 200

// Chunk size for the background backfill that loads the rest of the book
// (needed for in-book search and instant scrolling). Large so a 190k-line
// book takes ~100 round trips instead of ~1000 — per-query transport
// overhead dominated the old 200-line backfill.
const BACKFILL_CHUNK_SIZE = 2000

// Maximum number of chunk queries that run concurrently.
// 3 is the sweet spot: enough to saturate the WebView2 bridge without
// overwhelming it. The prioritised chunk always gets one of these slots
// immediately because prioritise() pushes it to the front of the queue
// before any worker picks the next item.
const CONCURRENT_CHUNKS = 3

// Slot states for 200-line-aligned windows (CHUNK_SIZE granularity).
const SLOT_UNFETCHED = 0
const SLOT_PENDING = 1

export function useLines(bookId: () => number | undefined) {
  const lines = ref<LineItem[]>([])
  // Start OPEN: the toolbar hides these controls when false, and metadata only
  // arrives after an await. Starting false would render the toolbar without them
  // for the first frames, then pop them in and shift every icon in the centred
  // row. False is set once metadata says the book genuinely has no connections.
  const hasCommentaries = ref(true)
  const hasRelatedBooks = ref(true)
  const hasTeamim = ref(false)

  // Queue of backfill ranges: [offset, limit] pairs.
  let fetchQueue: Array<{ offset: number; limit: number }> = []
  let activeWorkers = 0
  let currentBookId: number | undefined
  // While true, spawnWorkers() is a no-op — queued backfill ranges wait until
  // releaseBackfill(). Held during session restore (useBookViewLinesBackfillGate)
  // so the restored commentary panel's queries never queue behind full-book
  // chunk fetches. prefetch()/prioritise() bypass the queue and still run.
  let backfillHeld = false
  // Per-CHUNK_SIZE-slot state — SLOT_PENDING means fetched or in flight.
  // Prevents duplicate fetches between prioritise()/prefetch() and backfill.
  let slotState: Uint8Array = new Uint8Array(0)

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
    // array for each of up to CHUNK_SIZE/BACKFILL_CHUNK_SIZE rows — and since the first chunk
    // is fetched before load() pre-sizes to totalLines, that path runs on every book open.
    let maxLineIndex = -1
    for (const row of rows) if (row.lineIndex > maxLineIndex) maxLineIndex = row.lineIndex
    growTo(maxLineIndex + 1)

    for (const row of rows) {
      lines.value[row.lineIndex] = { id: row.id, lineIndex: row.lineIndex, content: row.content ?? '' }
    }
  }

  /** Extend `lines` with placeholders up to `count` entries. No-op if already that long. */
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

  async function fetchRange(bookIdAtStart: number, offset: number, limit: number): Promise<boolean> {
    try {
      const rows = await getLinesPaged(bookIdAtStart, limit, offset)
      if (currentBookId !== bookIdAtStart) return false
      writeRows(rows)
      return true
    } catch {
      // DB error — release the slots so a later prioritise/backfill can retry.
      if (currentBookId === bookIdAtStart) markRange(offset, limit, SLOT_UNFETCHED)
      return false
    }
  }

  // A single worker: pulls ranges off the shared queue and fetches them until
  // the queue is empty or the book changes. Multiple workers run concurrently.
  async function runWorker(bookIdAtStart: number) {
    activeWorkers++
    try {
      while (fetchQueue.length > 0) {
        if (currentBookId !== bookIdAtStart || backfillHeld) break
        const range = fetchQueue.shift()!
        // Skip ranges whose every slot was already fetched out-of-band.
        if (rangeFullyPending(range.offset, range.limit)) continue
        markRange(range.offset, range.limit, SLOT_PENDING)
        await fetchRange(bookIdAtStart, range.offset, range.limit)
      }
    } finally {
      activeWorkers--
    }
  }

  // Spawn workers up to the CONCURRENT_CHUNKS cap.
  function spawnWorkers() {
    const id = currentBookId
    if (id == null || backfillHeld) return
    while (activeWorkers < CONCURRENT_CHUNKS && fetchQueue.length > 0) {
      void runWorker(id)
    }
  }

  /** Pauses the backfill queue: no new ranges are dequeued (in-flight chunk
   *  fetches — at most CONCURRENT_CHUNKS — still complete). */
  function holdBackfill() {
    backfillHeld = true
  }

  /** Resumes the backfill queue. Safe to call multiple times. */
  function releaseBackfill() {
    if (!backfillHeld) return
    backfillHeld = false
    spawnWorkers()
  }

  // The backfill exists for THIS mounted view (in-book search, instant scroll).
  // Without this, workers of an unmounted book view kept draining their queue —
  // tens of MB for a large book — and every query of the NEXT mount (tab switch
  // back: session restore, commentary load) competed with that zombie flood.
  // Clearing currentBookId also stops in-flight chunks from writing their rows.
  onScopeDispose(() => {
    currentBookId = undefined
    fetchQueue = []
  })

  // Moves the chunk containing lineIndex to the front of the queue so it loads next.
  // Fires an out-of-band fetch for the CHUNK_SIZE window containing lineIndex if that
  // window has not been fetched yet — critical when navigating to a mid-book position
  // from a TOC entry, where the target may be far behind the backfill frontier.
  function prioritise(lineIndex: number) {
    prefetch(lineIndex)
  }

  // Out-of-band fast-path for a known scroll target (e.g. from IDB restore).
  // Fires an immediate CHUNK_SIZE query for the window containing lineIndex,
  // independent of the backfill worker pool. Slot tracking prevents duplicate
  // fetches for windows already loaded or in flight.
  function prefetch(lineIndex: number) {
    const id = currentBookId
    if (id == null) return
    const offset = Math.floor(lineIndex / CHUNK_SIZE) * CHUNK_SIZE

    const slot = slotOf(offset)
    ensureSlotCapacity(slot + 1)
    if (slotState[slot] !== SLOT_UNFETCHED) return
    markRange(offset, CHUNK_SIZE, SLOT_PENDING)

    void fetchRange(id, offset, CHUNK_SIZE)
  }

  async function load(id: number) {
    currentBookId = id
    lines.value = []
    fetchQueue = []
    activeWorkers = 0
    slotState = new Uint8Array(0)

    let metadataFailed = false
    const metadataPromise = getBookById(id).catch(() => {
      metadataFailed = true
      return undefined
    })

    // Kick off the first chunk immediately alongside metadata — don't wait.
    markRange(0, CHUNK_SIZE, SLOT_PENDING)
    void fetchRange(id, 0, CHUNK_SIZE)

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

    // Queue the rest of the book as large backfill ranges (needed for in-book
    // search and instant scrolling) and fill up to CONCURRENT_CHUNKS workers.
    ensureSlotCapacity(slotOf(Math.max(totalLines - 1, 0)) + 1)
    for (let offset = CHUNK_SIZE; offset < totalLines; ) {
      // Align the first backfill range so subsequent ranges start on
      // BACKFILL_CHUNK_SIZE boundaries.
      const limit = Math.min(
        BACKFILL_CHUNK_SIZE - (offset % BACKFILL_CHUNK_SIZE),
        totalLines - offset,
      )
      fetchQueue.push({ offset, limit })
      offset += limit
    }
    spawnWorkers()
  }

  watch(
    () => bookId(),
    (id) => {
      if (id != null) load(id)
    },
    { immediate: true },
  )

  return { lines, prioritise, prefetch, holdBackfill, releaseBackfill, hasCommentaries, hasRelatedBooks, hasTeamim }
}
