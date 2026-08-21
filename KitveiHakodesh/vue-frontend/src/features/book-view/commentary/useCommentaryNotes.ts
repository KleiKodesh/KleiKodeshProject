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

/**
 * Chunk size for the immediate export load. The viewport path asks for a screenful,
 * well under SQLite's bound-parameter limit; a select-all export asks for the whole
 * document and would blow past it in one statement.
 */
const LOAD_CHUNK = 400

export function useCommentaryNotes(getGroups: () => CommentaryGroup[]) {
  const notesByLine = ref<NotesByLine>(new Map())
  const loadedLineIds = new Set<number>()
  const lineIdToBookId = new Map<number, number>()
  // In-flight query per lineId. `loadedLineIds` is marked BEFORE a query resolves, so
  // "already requested" does not mean "already available" — an export that must be
  // able to await the data needs the promise, not just the flag.
  const inFlight = new Map<number, Promise<void>>()
  let debounceTimer: ReturnType<typeof setTimeout> | null = null

  /** Registers `work` as the in-flight load for `ids` and clears it when it settles. */
  function track(ids: number[], work: Promise<void>): Promise<void> {
    for (const id of ids) inFlight.set(id, work)
    return work.finally(() => {
      for (const id of ids) if (inFlight.get(id) === work) inFlight.delete(id)
    })
  }

  /** Groups line ids by their owning commentary book — one query per book. */
  function groupByBook(lineIds: number[]): Map<number, number[]> {
    const byBook = new Map<number, number[]>()
    for (const lineId of lineIds) {
      const bookId = lineIdToBookId.get(lineId)
      if (bookId == null) continue
      const list = byBook.get(bookId) ?? []
      list.push(lineId)
      byBook.set(bookId, list)
    }
    return byBook
  }

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
      // Re-filter: `pending` was computed before the debounce, and an export's
      // immediate load may have claimed some of these lines in the meantime.
      const stillPending = pending.filter((id) => !loadedLineIds.has(id))
      if (!stillPending.length) return
      // Mark all pending lines before the async call to prevent duplicate queries
      for (const id of stillPending) loadedLineIds.add(id)
      // Group by commentary bookId and issue one query per book
      for (const [bookId, ids] of groupByBook(stillPending)) {
        void track(ids, _loadForLines(bookId, ids))
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

  /**
   * Immediate, awaitable load — for the export paths, which need the notes of every
   * selected line (a select-all covers lines that were never rendered) and cannot
   * wait on the scroll debounce. Same per-book grouping and skip-set as scheduleLoad,
   * chunked because an export can ask for a whole document at once.
   */
  async function loadNotesForLines(lineIds: number[]): Promise<void> {
    const ids = lineIds.filter((id) => id > 0)
    // Work the viewport path already started for these lines: awaiting it is the
    // whole point of this method, since the skip-set alone cannot tell "requested"
    // from "arrived".
    const waits = [...new Set(ids.filter((id) => inFlight.has(id)).map((id) => inFlight.get(id)!))]
    const pending = ids.filter((id) => !loadedLineIds.has(id))
    for (const id of pending) loadedLineIds.add(id)
    await Promise.all(waits)
    for (const [bookId, bookIds] of groupByBook(pending)) {
      for (let i = 0; i < bookIds.length; i += LOAD_CHUNK) {
        const chunk = bookIds.slice(i, i + LOAD_CHUNK)
        await track(chunk, _loadForLines(bookId, chunk))
      }
    }
  }

  // ── Per-line lookup ────────────────────────────────────────────────────────

  function getNotesForLine(lineId: number): Note[] {
    return notesByLine.value.get(lineId) ?? []
  }

  // ── Internal map helpers ───────────────────────────────────────────────────

  function _addToMap(note: Note) {
    const list = notesByLine.value.get(note.lineId) ?? []
    // Idempotent by note id: two loads can legitimately overlap on one line (the
    // viewport path and an export's immediate load), and a note added twice would
    // render two markers and produce two endnotes for the same note.
    const existing = list.findIndex((n) => n.id === note.id)
    if (existing !== -1) list[existing] = note
    else list.push(note)
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
    loadNotesForLines,
    createNote,
    updateNote,
    deleteNote,
  }
}
