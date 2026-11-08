/**
 * Inline notes (footnotes) of a line - Otzaria's `line_note` table (schema 7+).
 *
 * The stored line keeps each note's marker, `<sup class="footnote-marker">1</sup>`, and the
 * note bodies live in line_note. Otzaria matches them in order: the k-th note with
 * `hasMarker` belongs to the k-th footnote marker of the line (a marker directly followed by
 * an inline `<i class="footnote">` body is not a stripped marker). Notes without a marker are
 * placed at the line's end. Otzaria shows the bodies in its notes pane for the line; here a
 * marker previews its note on hover, in the book view's link-preview panel.
 *
 * Nothing here touches the line's text: the markers are already in it, and the one marker
 * added for unmarked notes is an EMPTY element (its glyph comes from CSS) appended after the
 * last character, so every offset (search marks, highlights, user notes, links) is unchanged.
 */
import type { LineNote } from '@/webview-host/queries.types'

export const FOOTNOTE_MARKER_SELECTOR = 'sup.footnote-marker'
export const UNMARKED_NOTES_MARKER_CLASS = 'line-note-marker'
export const LINE_NOTE_SELECTOR = `${FOOTNOTE_MARKER_SELECTOR}, sup.${UNMARKED_NOTES_MARKER_CLASS}`

/** Render-cache signature of a line's notes. */
export function lineNotesSig(notes: LineNote[]): string {
  return notes.length ? notes.map((note) => `${note.noteIndex}:${note.hasMarker ? 1 : 0}:${note.body.length}`).join(',') : ''
}

/** Appends one empty marker for the line's notes that have no marker in the text. */
export function appendUnmarkedNotesMarker(html: string, notes: LineNote[]): string {
  return notes.some((note) => !note.hasMarker) ? `${html}<sup class="${UNMARKED_NOTES_MARKER_CLASS}"></sup>` : html
}

/**
 * The notes a hovered marker stands for: a footnote marker -> the note with a marker at the
 * same ordinal; the end-of-line marker -> every note without one. `row` is the rendered line.
 */
export function notesForMarker(marker: Element, row: Element, notes: LineNote[]): LineNote[] {
  if (!notes.length) return []
  if (marker.classList.contains(UNMARKED_NOTES_MARKER_CLASS)) return notes.filter((note) => !note.hasMarker)
  const markers = [...row.querySelectorAll(FOOTNOTE_MARKER_SELECTOR)].filter((element) => !isInlineBodyMarker(element))
  const ordinal = markers.indexOf(marker)
  if (ordinal < 0) return []
  const note = notes.filter((candidate) => candidate.hasMarker)[ordinal]
  return note ? [note] : []
}

/** A marker whose body follows it inline (older format): not one of line_note's markers. */
function isInlineBodyMarker(marker: Element): boolean {
  let next = marker.nextSibling
  while (next && next.nodeType === Node.TEXT_NODE && !next.textContent?.trim()) next = next.nextSibling
  return next instanceof Element && next.tagName === 'I' && next.classList.contains('footnote')
}
