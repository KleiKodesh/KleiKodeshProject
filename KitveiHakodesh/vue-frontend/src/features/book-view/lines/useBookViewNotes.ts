/**
 * Manages user notes for the currently open book.
 *
 * Loading strategy — lazy, viewport-driven, non-blocking:
 *   Rather than fetching all notes for the whole book on mount, notes are loaded
 *   only for the lineIds currently visible in the scroller. The caller provides a
 *   `getVisibleLineIds` callback. Whenever the visible set changes, a 100ms debounce
 *   fires a background DB query for any lineIds not yet fetched. This keeps the initial
 *   render instant and avoids loading notes for thousands of lines the user never sees.
 *
 *   Loaded lineIds are tracked in `loadedLineIds`. A lineId is only queried once.
 *   If the DB is unavailable the fetch is silently skipped (the lineId stays unloaded
 *   and will be retried on the next visible-set change).
 *
 * Mutations (create / update / delete) are immediate — they always write to the DB
 * and update the in-memory map synchronously from the caller's perspective.
 */
import { ref, watch } from 'vue'
import { queryUserSettings, executeUserSettings } from '@/webview-host/userSettingsDb'
import { USER_SETTINGS_SQL } from '@/webview-host/userSettingsDb.sql'

export interface Note {
  id: number
  bookId: number
  lineId: number
  startOffset: number
  endOffset: number
  note: string
  quote: string
  createdAt: number
  updatedAt: number
}

type NotesByLine = Map<number, Note[]>

export function useBookViewNotes(bookId: number, getVisibleLineIds: () => number[]) {
  const notesByLine = ref<NotesByLine>(new Map())
  // lineIds for which we have already issued a DB query (success or pending)
  const loadedLineIds = new Set<number>()
  let debounceTimer: ReturnType<typeof setTimeout> | null = null

  // ── Lazy load ─────────────────────────────────────────────────────────────

  function scheduleLoad(lineIds: number[]) {
    const pending = lineIds.filter((id) => id > 0 && !loadedLineIds.has(id))
    if (!pending.length) return

    if (debounceTimer !== null) clearTimeout(debounceTimer)
    debounceTimer = setTimeout(() => {
      debounceTimer = null
      // Mark all as loaded before the async call so concurrent scroll events
      // don't issue duplicate queries for the same lines
      for (const id of pending) loadedLineIds.add(id)
      void _loadForLines(pending)
    }, 100)
  }

  async function _loadForLines(lineIds: number[]): Promise<void> {
    try {
      const rows = await queryUserSettings<{
        id: number
        bookId: number
        lineId: number
        startOffset: number
        endOffset: number
        note: string
        quote: string
        createdAt: number
        updatedAt: number
      }>(USER_SETTINGS_SQL.GET_NOTES_FOR_LINES(lineIds.length), [bookId, ...lineIds])

      for (const row of rows) {
        _addToMap({
          id: row.id,
          bookId: row.bookId,
          lineId: row.lineId,
          startOffset: Number(row.startOffset),
          endOffset: Number(row.endOffset),
          note: row.note,
          quote: row.quote,
          createdAt: Number(row.createdAt),
          updatedAt: Number(row.updatedAt),
        })
      }
    } catch {
      // DB not ready — un-mark the lines so they are retried on next scroll
      for (const id of lineIds) loadedLineIds.delete(id)
    }
  }

  // Watch visible lineIds and schedule a load whenever the set changes
  watch(
    getVisibleLineIds,
    (ids) => scheduleLoad(ids),
    { immediate: true },
  )

  // ── Per-line lookup ────────────────────────────────────────────────────────

  function getNotesForLine(lineId: number): Note[] {
    return notesByLine.value.get(lineId) ?? []
  }

  // ── Internal map helpers ───────────────────────────────────────────────────

  function _addToMap(note: Note) {
    const list = notesByLine.value.get(note.lineId) ?? []
    list.push(note)
    list.sort((a, b) => a.startOffset - b.startOffset)
    notesByLine.value.set(note.lineId, list)
  }

  function _removeFromMap(note: Note) {
    const list = notesByLine.value.get(note.lineId)
    if (!list) return
    const index = list.findIndex((n) => n.id === note.id)
    if (index !== -1) list.splice(index, 1)
    if (list.length === 0) notesByLine.value.delete(note.lineId)
  }

  function _updateInMap(note: Note) {
    const list = notesByLine.value.get(note.lineId)
    if (!list) return
    const index = list.findIndex((n) => n.id === note.id)
    if (index !== -1) list[index] = { ...note }
  }

  // ── Mutations ──────────────────────────────────────────────────────────────

  async function createNote(
    lineId: number,
    startOffset: number,
    endOffset: number,
    quote: string,
  ): Promise<Note> {
    const now = Date.now()
    const insertedId = await executeUserSettings(USER_SETTINGS_SQL.INSERT_NOTE, [
      bookId,
      lineId,
      startOffset,
      endOffset,
      '',
      quote,
      now,
      now,
    ])
    const note: Note = {
      id: insertedId,
      bookId,
      lineId,
      startOffset,
      endOffset,
      note: '',
      quote,
      createdAt: now,
      updatedAt: now,
    }
    // Mark the line as loaded so the lazy loader doesn't overwrite the new note
    loadedLineIds.add(lineId)
    _addToMap(note)
    return note
  }

  async function updateNote(note: Note, newText: string): Promise<void> {
    if (note.note === newText) return
    const updatedAt = Date.now()
    await executeUserSettings(USER_SETTINGS_SQL.UPDATE_NOTE, [newText, updatedAt, note.id])
    _updateInMap({ ...note, note: newText, updatedAt })
  }

  async function deleteNote(note: Note): Promise<void> {
    await executeUserSettings(USER_SETTINGS_SQL.DELETE_NOTE, [note.id])
    _removeFromMap(note)
  }

  return {
    notesByLine,
    getNotesForLine,
    createNote,
    updateNote,
    deleteNote,
  }
}
