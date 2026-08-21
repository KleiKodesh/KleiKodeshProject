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
import { onScopeDispose, ref, watch } from 'vue'
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

/**
 * Chunk size for the immediate export load. The viewport path asks for a screenful,
 * well under SQLite's bound-parameter limit; a select-all export asks for the whole
 * book and would blow past it in one statement.
 */
const LOAD_CHUNK = 400

export function useBookViewNotes(bookId: number, getVisibleLineIds: () => number[]) {
  const notesByLine = ref<NotesByLine>(new Map())
  // lineIds for which we have already issued a DB query (success or pending)
  const loadedLineIds = new Set<number>()
  // In-flight query per lineId. `loadedLineIds` is marked BEFORE a query resolves, so
  // "already requested" does not mean "already available" — an export that must be
  // able to await the data needs the promise, not just the flag.
  const inFlight = new Map<number, Promise<void>>()
  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  // A select-all warm-up walks the WHOLE book in sequential chunks. Nothing about closing
  // the tab stops that on its own, so the chunk loop checks this between chunks and the
  // pending debounce is dropped outright.
  let disposed = false
  onScopeDispose(() => {
    disposed = true
    if (debounceTimer !== null) clearTimeout(debounceTimer)
    debounceTimer = null
  })

  /** Registers `work` as the in-flight load for `ids` and clears it when it settles. */
  function track(ids: number[], work: Promise<void>): Promise<void> {
    for (const id of ids) inFlight.set(id, work)
    return work.finally(() => {
      for (const id of ids) if (inFlight.get(id) === work) inFlight.delete(id)
    })
  }

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
      // Mark all as loaded before the async call so concurrent scroll events
      // don't issue duplicate queries for the same lines
      for (const id of stillPending) loadedLineIds.add(id)
      void track(stillPending, _loadForLines(stillPending))
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

  /**
   * Immediate, awaitable load — for the export paths, which need the notes of every
   * selected line (a select-all covers lines that were never rendered) and cannot
   * wait on the scroll debounce. Skips lines already loaded or in flight, exactly
   * like scheduleLoad, so the two paths never double-query the same line.
   */
  async function loadForLines(lineIds: number[]): Promise<void> {
    const ids = lineIds.filter((id) => id > 0)
    // Work the viewport path already started for these lines: awaiting it is the
    // whole point of this method, since the skip-set alone cannot tell "requested"
    // from "arrived".
    const waits = [...new Set(ids.filter((id) => inFlight.has(id)).map((id) => inFlight.get(id)!))]
    const pending = ids.filter((id) => !loadedLineIds.has(id))
    for (const id of pending) loadedLineIds.add(id)

    // Register the WHOLE pending set against ONE promise covering every chunk, before the
    // first await. Marking `loadedLineIds` per id but `inFlight` per chunk let a second
    // caller compute an empty `pending`, wait only on the chunk that happened to be in
    // flight, and return while chunks 2..N were still unloaded — an export of a book over
    // one chunk then wrote most of its lines with no notes at all.
    const whole = (async () => {
      await Promise.all(waits)
      for (let i = 0; i < pending.length; i += LOAD_CHUNK) {
        if (disposed) return
        await _loadForLines(pending.slice(i, i + LOAD_CHUNK))
      }
    })()
    await track(pending, whole)
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
    loadNotesForLines: loadForLines,
    createNote,
    updateNote,
    deleteNote,
  }
}
