/**
 * Manages user notes for all commentary books visible in the commentary panel.
 *
 * Loading strategy — lazy, viewport-driven, non-blocking:
 *   The caller provides visible lineIds via scheduleNotesLoad (called from the
 *   component's virtualizer watcher). A 100ms debounce batches rapid scroll
 *   events into a single DB query per commentary book.
 *
 *   lineIdToBookId is populated from getGroups() eagerly so createNote() knows
 *   which bookId to use before the async load completes.
 */
import { ref, watch } from 'vue'
import { queryUserSettings, executeUserSettings } from '@/webview-host/userSettingsDb'
import { USER_SETTINGS_SQL } from '@/webview-host/userSettingsDb.sql'
import type { Note } from '../lines/useBookViewNotes'
import type { CommentaryGroup } from './useCommentary'

type NotesByLine = Map<number, Note[]>

export function useCommentaryNotes(getGroups: () => CommentaryGroup[]) {
  const notesByLine = ref<NotesByLine>(new Map())
  const loadedLineIds = new Set<number>()
  const lineIdToBookId = new Map<number, number>()
  let debounceTimer: ReturnType<typeof setTimeout> | null = null

  // ── Keep lineIdToBookId current as groups change ──────────────────────────

  watch(
    getGroups,
    (groups) => {
      for (const group of groups) {
        if (group.bookId > 0) {
          for (const line of group.lines) {
            if (line.lineId > 0) lineIdToBookId.set(line.lineId, group.bookId)
          }
        }
      }
    },
    { immediate: true },
  )

  // ── Lazy load ─────────────────────────────────────────────────────────────

  function scheduleLoad(lineIds: number[]) {
    const pending = lineIds.filter((id) => id > 0 && !loadedLineIds.has(id))
    if (!pending.length) return

    if (debounceTimer !== null) clearTimeout(debounceTimer)
    debounceTimer = setTimeout(() => {
      debounceTimer = null
      // Mark all pending lines before the async call to prevent duplicate queries
      for (const id of pending) loadedLineIds.add(id)
      // Group by commentary bookId and issue one query per book
      const byBook = new Map<number, number[]>()
      for (const lineId of pending) {
        const bookId = lineIdToBookId.get(lineId)
        if (bookId == null) continue
        const list = byBook.get(bookId) ?? []
        list.push(lineId)
        byBook.set(bookId, list)
      }
      for (const [bookId, ids] of byBook) {
        void _loadForLines(bookId, ids)
      }
    }, 100)
  }

  async function _loadForLines(bookId: number, lineIds: number[]): Promise<void> {
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

  // Watch visible lineIds and schedule a load whenever the set changes.
  // Called by the component's virtualizer watcher — not driven from here.
  function scheduleNotesLoad(lineIds: number[]) {
    scheduleLoad(lineIds)
  }

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
    const commentaryBookId = lineIdToBookId.get(lineId)
    if (commentaryBookId == null) throw new Error('Unknown lineId')

    const now = Date.now()
    const insertedId = await executeUserSettings(USER_SETTINGS_SQL.INSERT_NOTE, [
      commentaryBookId,
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
      bookId: commentaryBookId,
      lineId,
      startOffset,
      endOffset,
      note: '',
      quote,
      createdAt: now,
      updatedAt: now,
    }
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
    getNotesForLine,
    scheduleNotesLoad,
    createNote,
    updateNote,
    deleteNote,
  }
}
