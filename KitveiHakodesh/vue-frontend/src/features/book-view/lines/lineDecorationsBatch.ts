/**
 * Joins the lines view's two per-window decoration loads - word-link anchors
 * (useWordLinkAnchors) and inline notes (useLineNotes) - into ONE service call.
 *
 * Both loaders debounce on the same mounted-lines change, so their requests arrive in the
 * same turn; a request waits one macrotask for its partner, then a single
 * getLineDecorationsForLines answers both. A request with no partner (an export's anchor
 * warm-up) still goes out alone, one macrotask later. Each loader keeps its own
 * bookkeeping (what it asked, what is in flight, eviction) - only the transport is shared.
 */
import { getLineDecorationsForLines } from '@/webview-host/seforimApi'
import type { LineNote, WordLinkAnchor } from '@/webview-host/queries.types'

interface Waiter<T> {
  ids: number[]
  resolve: (rows: T[]) => void
  reject: (error: Error) => void
}

export interface LineDecorationsBatch {
  anchorsFor(lineIds: number[]): Promise<WordLinkAnchor[]>
  notesFor(lineIds: number[]): Promise<LineNote[]>
}

function rowsOf<T extends { lineId: number }>(rows: T[], ids: number[]): T[] {
  const wanted = new Set(ids)
  return rows.filter((row) => wanted.has(row.lineId))
}

export function createLineDecorationsBatch(): LineDecorationsBatch {
  let anchorWaiters: Waiter<WordLinkAnchor>[] = []
  let noteWaiters: Waiter<LineNote>[] = []
  let timer: ReturnType<typeof setTimeout> | null = null

  async function flush() {
    timer = null
    const anchors = anchorWaiters
    const notes = noteWaiters
    anchorWaiters = []
    noteWaiters = []
    const union = (waiters: readonly { ids: number[] }[]) => [...new Set(waiters.flatMap((w) => w.ids))]
    const answer = await getLineDecorationsForLines(union(anchors), union(notes))
    for (const w of anchors) w.resolve(anchors.length === 1 ? answer.anchors : rowsOf(answer.anchors, w.ids))
    for (const w of notes) {
      if (answer.notes instanceof Error) w.reject(answer.notes)
      else w.resolve(notes.length === 1 ? answer.notes : rowsOf(answer.notes, w.ids))
    }
  }

  function enqueue<T>(list: Waiter<T>[], ids: number[]): Promise<T[]> {
    if (!ids.length) return Promise.resolve([])
    return new Promise<T[]>((resolve, reject) => {
      list.push({ ids, resolve, reject })
      timer ??= setTimeout(() => void flush(), 0)
    })
  }

  return {
    anchorsFor: (lineIds) => enqueue(anchorWaiters, lineIds),
    notesFor: (lineIds) => enqueue(noteWaiters, lineIds),
  }
}
