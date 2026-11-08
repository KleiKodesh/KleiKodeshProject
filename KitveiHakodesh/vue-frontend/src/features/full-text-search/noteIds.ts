/**
 * Virtual "notes book" ids (issue #13), mirror of SeforimDbNoteIds.cs. On a schema-7 library
 * every footnote (line_note) is indexed as a line of a virtual notes book placed right after
 * its book:
 *   R = 2^30;  virtual line id = R + (lineId << 6) + noteIndex;  virtual book id = R + bookId
 * A hit at or above R opens its SOURCE book at the source line.
 */
export const NOTE_ID_RESERVED_START = 1 << 30
const NOTE_INDEX_BITS = 6
const NOTE_INDEX_MASK = (1 << NOTE_INDEX_BITS) - 1

export function isNoteId(id: number): boolean {
  return id >= NOTE_ID_RESERVED_START
}

/** The source line and note index of a virtual line id; null for a real line. */
export function decodeNoteLine(id: number): { sourceLineId: number; noteIndex: number } | null {
  if (!isNoteId(id)) return null
  const v = id - NOTE_ID_RESERVED_START
  return { sourceLineId: Math.floor(v / (1 << NOTE_INDEX_BITS)), noteIndex: v & NOTE_INDEX_MASK }
}

/** The line a hit opens: a note's source line, else the id itself. */
export function sourceLineOf(lineId: number): number {
  return decodeNoteLine(lineId)?.sourceLineId ?? lineId
}

/** The book a hit opens: a notes book's source book, else the id itself. */
export function sourceBookOf(bookId: number): number {
  return isNoteId(bookId) ? bookId - NOTE_ID_RESERVED_START : bookId
}
