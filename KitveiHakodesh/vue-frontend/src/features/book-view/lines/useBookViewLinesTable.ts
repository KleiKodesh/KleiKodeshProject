import { ref, watch } from 'vue'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import { bookViewPerf } from '@/utils/bookViewPerf'

export interface LineItem {
  id: number
  lineIndex: number
  content: string | null // null = placeholder, not yet loaded
}

const CHUNK_SIZE = 200

// Maximum number of chunk queries that run concurrently.
// 3 is the sweet spot: enough to saturate the WebView2 bridge without
// overwhelming it. The prioritised chunk always gets one of these slots
// immediately because prioritise() pushes it to the front of the queue
// before any worker picks the next item.
const CONCURRENT_CHUNKS = 3

export function useLines(bookId: () => number | undefined) {
  const lines = ref<LineItem[]>([])
  const hasCommentaries = ref(false)
  const hasRelatedBooks = ref(false)
  const hasTeamim = ref(false)

  let fetchQueue: number[] = []
  let activeWorkers = 0
  let currentBookId: number | undefined
  // Tracks offsets that have an active out-of-band prefetch in flight so
  // prioritise() and the IDB-restore watcher never fire duplicate queries
  // for the same offset.
  const activePrefetches = new Set<number>()
  // Perf tracking
  let chunksProcessed = 0
  let chunk0StartTime = 0
  let allChunksStartTime = 0

  // Write a completed chunk's rows into lines.value.
  function writeRows(rows: { id: number; lineIndex: number; content: string }[]) {
    for (const row of rows) {
      if (row.lineIndex >= lines.value.length) {
        const extra = Array.from({ length: row.lineIndex - lines.value.length + 1 }, (_, i) => ({
          id: -(lines.value.length + i + 1),
          lineIndex: lines.value.length + i,
          content: null,
        }))
        lines.value = [...lines.value, ...extra]
      }
      lines.value[row.lineIndex] = { id: row.id, lineIndex: row.lineIndex, content: row.content ?? '' }
    }
  }

  // A single worker: pulls offsets off the shared queue and fetches them until
  // the queue is empty or the book changes. Multiple workers run concurrently.
  async function runWorker(bookIdAtStart: number) {
    activeWorkers++
    try {
      while (fetchQueue.length > 0) {
        if (currentBookId !== bookIdAtStart) break
        const offset = fetchQueue.shift()!
        const isFirstChunk = offset === 0

        let chunkStart = 0
        if (isFirstChunk) {
          chunkStart = bookViewPerf.mark('lines:chunk0:queryStart')
          chunk0StartTime = chunkStart
        }

        let rows: { id: number; lineIndex: number; content: string }[]
        try {
          rows = await query<{ id: number; lineIndex: number; content: string }>(
            SQL.GET_LINES_PAGED,
            [bookIdAtStart, CHUNK_SIZE, offset],
          )
        } catch {
          // DB error on this chunk — skip and continue
          chunksProcessed++
          continue
        }

        if (currentBookId !== bookIdAtStart) break

        if (isFirstChunk) {
          bookViewPerf.measure('lines:chunk0:query', chunk0StartTime)
        }

        writeRows(rows)
        chunksProcessed++

        if (isFirstChunk) {
          bookViewPerf.mark(`lines:chunk0:done (${rows.length} rows)`)
        }
      }
    } finally {
      activeWorkers--
      // When the last worker exits and the queue is truly empty, all chunks are done.
      if (activeWorkers === 0 && fetchQueue.length === 0) {
        bookViewPerf.measure('lines:allChunks', allChunksStartTime)
        bookViewPerf.mark(`lines:allChunksDone (${chunksProcessed} chunks, ${lines.value.length} total lines)`)
      }
    }
  }

  // Spawn workers up to the CONCURRENT_CHUNKS cap.
  function spawnWorkers() {
    const id = currentBookId
    if (id == null) return
    while (activeWorkers < CONCURRENT_CHUNKS && fetchQueue.length > 0) {
      void runWorker(id)
    }
  }

  // Moves the chunk containing lineIndex to the front of the queue so it loads next.
  // For non-zero offsets that are still in the queue, also fires an out-of-band
  // prefetch so the chunk races the background workers rather than waiting for a
  // slot to free up — critical when navigating to a mid-book position from a TOC
  // entry, where the target chunk may be 100+ positions deep in the queue.
  function prioritise(lineIndex: number) {
    const offset = Math.floor(lineIndex / CHUNK_SIZE) * CHUNK_SIZE

    // Chunk 0 already has its own dedicated worker from load() — no prefetch needed.
    // For all other offsets: if the chunk is still in the queue, fire it out-of-band
    // immediately rather than waiting for a worker slot to become available.
    if (offset !== 0) {
      const position = fetchQueue.indexOf(offset)
      if (position !== -1) {
        // prefetch() removes it from the queue itself — just call it directly.
        prefetch(lineIndex)
        return
      }
      // Already removed from queue (prefetch already fired or workers already fetched it).
      return
    }

    // offset === 0: just promote in queue and try to spawn a worker.
    const position = fetchQueue.indexOf(offset)
    if (position === -1) return
    if (position > 0) {
      fetchQueue.splice(position, 1)
      fetchQueue.unshift(offset)
    }
    spawnWorkers()
  }

  // Out-of-band fast-path for a known scroll target (e.g. from IDB restore).
  // Fires an immediate query for the chunk containing lineIndex, completely
  // independent of the worker pool — no waiting for a slot to free up.
  // Removes the offset from the background queue so the same chunk is not
  // fetched a second time by a background worker.
  // No-ops if a prefetch for the same offset is already in flight.
  function prefetch(lineIndex: number) {
    const id = currentBookId
    if (id == null) return
    const offset = Math.floor(lineIndex / CHUNK_SIZE) * CHUNK_SIZE

    // Guard: skip if already in flight for this offset.
    if (activePrefetches.has(offset)) return
    activePrefetches.add(offset)

    // Remove from queue so background workers don't duplicate the fetch.
    const position = fetchQueue.indexOf(offset)
    if (position !== -1) fetchQueue.splice(position, 1)

    const prefetchStart = bookViewPerf.mark(`lines:prefetch:queryStart (offset=${offset})`)
    void query<{ id: number; lineIndex: number; content: string }>(
      SQL.GET_LINES_PAGED,
      [id, CHUNK_SIZE, offset],
    )
      .then((rows) => {
        activePrefetches.delete(offset)
        bookViewPerf.measure('lines:prefetch:query', prefetchStart)
        if (currentBookId !== id) return
        writeRows(rows)
        chunksProcessed++
        bookViewPerf.mark(`lines:prefetch:done (${rows.length} rows, offset=${offset})`)
      })
      .catch(() => {
        activePrefetches.delete(offset)
        // Prefetch failed — re-add to queue so background workers retry it.
        fetchQueue.push(offset)
      })
  }

  async function load(id: number) {
    currentBookId = id
    lines.value = []
    fetchQueue = []
    activeWorkers = 0
    chunksProcessed = 0
    activePrefetches.clear()

    // Reset is called by useBookView at setup time (before both useToc and useLines
    // are constructed) so the session origin is shared across all perf marks.
    // We still reset here as a safety net when the book changes mid-session.
    bookViewPerf.reset()
    allChunksStartTime = bookViewPerf.mark('lines:loadStart')

    type BookRow = {
      totalLines: number
      hasTeamim: number
      hasTargumConnection: number
      hasReferenceConnection: number
      hasSourceConnection: number
      hasCommentaryConnection: number
      hasOtherConnection: number
    }

    const metadataStart = bookViewPerf.mark('lines:metadata:queryStart')
    const metadataPromise = query<BookRow>(SQL.GET_BOOK_BY_ID, [id])
      .then((rows) => {
        bookViewPerf.measure('lines:metadata:query', metadataStart)
        return rows[0]
      })
      .catch(() => undefined)

    // Kick off the first chunk immediately alongside metadata — don't wait.
    fetchQueue.push(0)
    spawnWorkers()

    const book = await metadataPromise
    if (currentBookId !== id) return

    bookViewPerf.mark('lines:metadata:done')

    const totalLines = book?.totalLines ?? 0
    hasTeamim.value = !!(book?.hasTeamim)
    hasCommentaries.value = !!(
      book?.hasTargumConnection ||
      book?.hasReferenceConnection ||
      book?.hasSourceConnection ||
      book?.hasCommentaryConnection ||
      book?.hasOtherConnection
    )
    hasRelatedBooks.value = !!(
      book?.hasSourceConnection ||
      book?.hasTargumConnection ||
      book?.hasCommentaryConnection
    )

    if (totalLines > lines.value.length) {
      const extra = Array.from({ length: totalLines - lines.value.length }, (_, i) => ({
        id: -(lines.value.length + i + 1),
        lineIndex: lines.value.length + i,
        content: null,
      }))
      lines.value = [...lines.value, ...extra]
    }

    bookViewPerf.mark(`lines:placeholdersReady (${totalLines} total lines)`)

    // Queue remaining chunks and fill up to CONCURRENT_CHUNKS workers.
    const chunkCount = totalLines > 0 ? Math.ceil(totalLines / CHUNK_SIZE) : 1
    for (let i = 1; i < chunkCount; i++) fetchQueue.push(i * CHUNK_SIZE)
    spawnWorkers()
  }

  watch(
    () => bookId(),
    (id) => {
      if (id != null) load(id)
    },
    { immediate: true },
  )

  return { lines, prioritise, prefetch, hasCommentaries, hasRelatedBooks, hasTeamim }
}
