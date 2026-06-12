/**
 * Manages user notes for all commentary books visible in the commentary panel.
 *
 * Mirrors useCommentaryHighlights exactly, but for user_notes.
 *
 * Notes must be stored using the commentary book's bookId, not the parent book's id,
 * so they appear correctly when the commentary book is opened directly in the book viewer.
 *
 * Loads lazily per commentary bookId as groups become visible.
 * Routes createNote/updateNote/deleteNote to the correct commentary bookId via lineIdToBookId.
 */
import { ref, watch } from 'vue'
import { queryUserSettings, executeUserSettings } from '@/webview-host/userSettingsDb'
import { USER_SETTINGS_SQL } from '@/webview-host/userSettingsDb.sql'
import type { Note } from '../lines/useBookViewNotes'
import type { CommentaryGroup } from './useCommentary'

type NotesByLine = Map<number, Note[]>

export function useCommentaryNotes(getGroups: () => CommentaryGroup[]) {
  const notesByLine = ref<NotesByLine>(new Map())
  const loadedBookIds = new Set<number>()
  const lineIdToBookId = new Map<number, number>()

  // ── Load ──────────────────────────────────────────────────────────────────

  async function loadNotesForBook(commentaryBookId: number): Promise<void> {
    if (loadedBookIds.has(commentaryBookId)) return
    loadedBookIds.add(commentaryBookId)
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
      }>(USER_SETTINGS_SQL.GET_NOTES_FOR_BOOK, [commentaryBookId])

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
        _addToMap(note)
      }
    } catch {
      loadedBookIds.delete(commentaryBookId)
    }
  }

  watch(
    getGroups,
    (groups) => {
      for (const group of groups) {
        if (group.bookId > 0) {
          for (const line of group.lines) {
            if (line.lineId > 0) lineIdToBookId.set(line.lineId, group.bookId)
          }
          if (!loadedBookIds.has(group.bookId)) void loadNotesForBook(group.bookId)
        }
      }
    },
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
    createNote,
    updateNote,
    deleteNote,
  }
}
