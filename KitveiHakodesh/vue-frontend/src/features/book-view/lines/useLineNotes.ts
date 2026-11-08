/**
 * Lazy, viewport-driven loader for inline notes (line_note, Otzaria schema 7+).
 *
 * Same shape as useWordLinkAnchors: whenever the mounted lines change, a 100ms debounce
 * fires ONE batched query for the line ids not yet requested. On DBs without line_note
 * the seforimApi layer short-circuits to [] after the first probe, so this is a no-op there.
 */
import { onScopeDispose, ref, watch } from 'vue'
import { getLineNotesForLines } from '@/webview-host/seforimApi'
import type { LineNote } from '@/webview-host/queries.types'

const EMPTY: LineNote[] = []

export function useLineNotes(getVisibleLineIds: () => number[]) {
  const notesByLine = ref<Map<number, LineNote[]>>(new Map())
  // Line ids already sent to (or in flight toward) the DB - each is queried once.
  const requested = new Set<number>()
  const inFlight = new Set<number>()
  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  onScopeDispose(() => {
    if (debounceTimer !== null) clearTimeout(debounceTimer)
    debounceTimer = null
  })

  function scheduleLoad(lineIds: number[]) {
    if (!lineIds.some((id) => id > 0 && !requested.has(id))) return
    if (debounceTimer !== null) clearTimeout(debounceTimer)
    debounceTimer = setTimeout(() => {
      debounceTimer = null
      // Re-read the window: it may have moved during the debounce.
      const pending = getVisibleLineIds().filter((id) => id > 0 && !requested.has(id))
      if (!pending.length) return
      for (const id of pending) {
        requested.add(id)
        inFlight.add(id)
      }
      void load(pending)
    }, 100)
  }

  async function load(lineIds: number[]) {
    try {
      const rows = await getLineNotesForLines(lineIds)
      if (!rows.length) return
      const byLine = new Map<number, LineNote[]>()
      for (const row of rows) {
        let list = byLine.get(row.lineId)
        if (!list) byLine.set(row.lineId, (list = []))
        list.push(row)
      }
      // One reactive write per batch, so the lines re-render once.
      const next = new Map(notesByLine.value)
      for (const [lineId, list] of byLine) next.set(lineId, list)
      notesByLine.value = next
    } catch {
      // DB not ready - un-mark so the lines are retried on the next visible-set change.
      for (const id of lineIds) requested.delete(id)
    } finally {
      for (const id of lineIds) inFlight.delete(id)
    }
  }

  watch(getVisibleLineIds, (ids) => scheduleLoad(ids), { immediate: true })

  function getLineNotesForLine(lineId: number): LineNote[] {
    return notesByLine.value.get(lineId) ?? EMPTY
  }

  /** Drops the notes of lines whose text was evicted; they load again with the text. */
  function evictLineNotesForLines(lineIds: number[]) {
    let next: Map<number, LineNote[]> | null = null
    for (const id of lineIds) {
      if (inFlight.has(id)) continue
      requested.delete(id)
      if (notesByLine.value.has(id)) (next ??= new Map(notesByLine.value)).delete(id)
    }
    if (next) notesByLine.value = next
  }

  return { getLineNotesForLine, evictLineNotesForLines }
}
