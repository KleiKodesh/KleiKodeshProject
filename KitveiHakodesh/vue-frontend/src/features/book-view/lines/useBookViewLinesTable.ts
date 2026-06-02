import { ref, watch } from 'vue'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'

export interface LineItem {
  id: number
  lineIndex: number
  content: string | null // null = placeholder, not yet loaded
}

const CHUNK_SIZE = 200

export function useLines(bookId: () => number | undefined) {
  const lines = ref<LineItem[]>([])
  const hasCommentaries = ref(false)
  const hasRelatedBooks = ref(false)
  const hasTeamim = ref(false)

  let fetchQueue: number[] = []
  let fetching = false
  let currentBookId: number | undefined

  async function processQueue() {
    if (fetching || !fetchQueue.length) return
    fetching = true
    const bookIdAtStart = currentBookId
    try {
      while (fetchQueue.length > 0) {
        if (currentBookId !== bookIdAtStart) break
        const offset = fetchQueue.shift()!
        let rows: { id: number; lineIndex: number; content: string }[]
        try {
          rows = await query<{ id: number; lineIndex: number; content: string }>(
            SQL.GET_LINES_PAGED,
            [bookIdAtStart, CHUNK_SIZE, offset],
          )
        } catch {
          // DB error on this chunk — skip it and continue with the rest
          continue
        }
        if (currentBookId !== bookIdAtStart) break
        for (const row of rows) {
          // Guard against stale totalLines: only write into pre-allocated slots.
          // If the row's lineIndex is beyond the array, grow it to fit.
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
    } finally {
      fetching = false
    }
  }

  // Moves the chunk containing lineIndex to the front of the queue so it loads next.
  function prioritise(lineIndex: number) {
    const offset = Math.floor(lineIndex / CHUNK_SIZE) * CHUNK_SIZE
    const pos = fetchQueue.indexOf(offset)
    if (pos === -1) return
    if (pos > 0) {
      fetchQueue.splice(pos, 1)
      fetchQueue.unshift(offset)
    }
    if (!fetching) processQueue()
  }

  async function load(id: number) {
    currentBookId = id
    lines.value = []
    fetchQueue = []
    fetching = false

    let book: {
      totalLines: number
      hasTeamim: number
      hasTargumConnection: number
      hasReferenceConnection: number
      hasSourceConnection: number
      hasCommentaryConnection: number
      hasOtherConnection: number
    } | undefined

    try {
      const rows = await query<typeof book & {}>(SQL.GET_BOOK_BY_ID, [id])
      book = rows[0]
    } catch {
      // DB error reading book metadata — proceed with zero totalLines
    }

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

    // Pre-allocate all slots with placeholders so the virtualizer has the correct count
    // and scroll height from the start. Content fills in as chunks arrive.
    // If totalLines is 0 (missing or stale book row), we still queue one chunk so that
    // any lines that do exist in the DB are discovered and the array grows to fit them.
    lines.value = Array.from({ length: totalLines }, (_, i) => ({
      id: -(i + 1),
      lineIndex: i,
      content: null,
    }))

    const chunkCount = totalLines > 0 ? Math.ceil(totalLines / CHUNK_SIZE) : 1
    for (let i = 0; i < chunkCount; i++) fetchQueue.push(i * CHUNK_SIZE)
    processQueue()
  }

  watch(
    () => bookId(),
    (id) => {
      if (id != null) load(id)
    },
    { immediate: true },
  )

  return { lines, prioritise, hasCommentaries, hasRelatedBooks, hasTeamim }
}
