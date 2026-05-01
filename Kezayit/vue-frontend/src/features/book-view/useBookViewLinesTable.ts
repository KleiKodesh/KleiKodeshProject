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

  let fetchQueue: number[] = []
  let fetching = false
  let currentBookId: number | undefined

  async function processQueue() {
    if (fetching || !fetchQueue.length) return
    fetching = true
    const bookIdAtStart = currentBookId
    while (fetchQueue.length > 0) {
      if (currentBookId !== bookIdAtStart) break
      const offset = fetchQueue.shift()!
      const rows = await query<{ id: number; lineIndex: number; content: string }>(
        SQL.GET_LINES_PAGED,
        [bookIdAtStart, CHUNK_SIZE, offset],
      )
      if (currentBookId !== bookIdAtStart) break
      for (const row of rows)
        lines.value[row.lineIndex] = { id: row.id, lineIndex: row.lineIndex, content: row.content }
    }
    fetching = false
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

    const [book] = await query<{
      totalLines: number
      hasTargumConnection: number
      hasReferenceConnection: number
      hasSourceConnection: number
      hasCommentaryConnection: number
      hasOtherConnection: number
    }>(SQL.GET_BOOK_BY_ID, [id])
    const totalLines = book?.totalLines ?? 0
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
    lines.value = Array.from({ length: totalLines }, (_, i) => ({
      id: -(i + 1),
      lineIndex: i,
      content: null,
    }))

    for (let offset = 0; offset < totalLines; offset += CHUNK_SIZE) fetchQueue.push(offset)
    processQueue()
  }

  watch(
    () => bookId(),
    (id) => {
      if (id != null) load(id)
    },
    { immediate: true },
  )

  return { lines, prioritise, hasCommentaries, hasRelatedBooks }
}
