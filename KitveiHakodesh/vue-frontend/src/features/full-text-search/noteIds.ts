/**
 * Virtual "notes book" ids (issue #13), mirror of SeforimDbNoteIds.cs. On a schema-7 library
 * every footnote (line_note) is indexed as a line of a virtual notes book placed right after
 * its book:
 *   R = 2^52;  virtual line id = R + lineId * 2^16 + noteIndex;  virtual book id = R + bookId
 * (lineId < 2^36, noteIndex < 2^16), so every id stays at or below Number.MAX_SAFE_INTEGER and
 * a JS number holds it exactly. Plain arithmetic only: bit operators would cut to 32 bits.
 * A hit at or above R opens its SOURCE book at the source line.
 */
export const NOTE_ID_RESERVED_START = 2 ** 52
const NOTE_INDEX_RANGE = 2 ** 16

export function isNoteId(id: number): boolean {
  return id >= NOTE_ID_RESERVED_START
}

/** The source line and note index of a virtual line id; null for a real line. */
export function decodeNoteLine(id: number): { sourceLineId: number; noteIndex: number } | null {
  if (!isNoteId(id)) return null
  const v = id - NOTE_ID_RESERVED_START
  return { sourceLineId: Math.floor(v / NOTE_INDEX_RANGE), noteIndex: v % NOTE_INDEX_RANGE }
}

/** The virtual line id of note `noteIndex` of line `lineId` (tests and diagnostics). */
export function encodeNoteLine(lineId: number, noteIndex: number): number {
  return NOTE_ID_RESERVED_START + lineId * NOTE_INDEX_RANGE + noteIndex
}

/** The line a hit opens: a note's source line, else the id itself. */
export function sourceLineOf(lineId: number): number {
  return decodeNoteLine(lineId)?.sourceLineId ?? lineId
}

/** The note index a hit pops open; null for a plain line. */
export function noteIndexOf(lineId: number): number | null {
  return decodeNoteLine(lineId)?.noteIndex ?? null
}

/** The book a hit opens: a notes book's source book, else the id itself. */
export function sourceBookOf(bookId: number): number {
  return isNoteId(bookId) ? bookId - NOTE_ID_RESERVED_START : bookId
}
