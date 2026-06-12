/**
 * Manages user notes for the currently open book.
 *
 * Mirrors the structure of useBookViewHighlights exactly, but for user_notes.
 *
 * A note stores:
 *   - lineId, startOffset, endOffset — the text range that was selected
 *   - quote — snapshot of the selected text at creation time
 *   - note  — the user's annotation text (editable after creation)
 *
 * The in-memory store is a Map<lineId, Note[]> for O(1) lookup during rendering.
 * Multiple notes per line are allowed and stored sorted by startOffset.
 */
import { ref } from 'vue'
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

export function useBookViewNotes(bookId: number) {
  const notesByLine = ref<NotesByLine>(new Map())
  const isLoaded = ref(false)

  // ── Load ──────────────────────────────────────────────────────────────────

  async function loadNotes() {
    isLoaded.value = false
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
      }>(USER_SETTINGS_SQL.GET_NOTES_FOR_BOOK, [bookId])

      const map: NotesByLine = new Map()
      for (const row of rows) {
        const note: Note = {
          id: row.id,
          bookId: row.bookId,
          lineId: row.lineId,
          startOffset: Number(row.startOffset),
          endOffset: Number(row.endOffset),
          note: row.note,
          quote: row.quote,
          createdAt: Number(row.createdAt),
          updatedAt: Number(row.updatedAt),
        }
        const existing = map.get(note.lineId) ?? []
        existing.push(note)
        map.set(note.lineId, existing)
      }
      notesByLine.value = map
    } catch {
      // DB not ready — silently skip
    } finally {
      isLoaded.value = true
    }
  }

  loadNotes()

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
      '',   // note text starts empty — user types in the bubble
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
    isLoaded,
    getNotesForLine,
    createNote,
    updateNote,
    deleteNote,
    reloadNotes: loadNotes,
  }
}
