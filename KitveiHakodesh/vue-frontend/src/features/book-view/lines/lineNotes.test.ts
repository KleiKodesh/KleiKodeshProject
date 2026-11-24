/**
 * @vitest-environment jsdom
 */
import { describe, it, expect } from 'vitest'
import { appendUnmarkedNotesMarker, lineNotesSig, markerForNote, notesForMarker } from './lineNotes'
import type { LineNote } from '@/webview-host/queries.types'

const note = (noteIndex: number, hasMarker: boolean, body = `body${noteIndex}`): LineNote => ({
  lineId: 1,
  noteIndex,
  charPos: hasMarker ? 2 : -1,
  hasMarker,
  body,
})

function row(html: string): HTMLElement {
  const element = document.createElement('div')
  element.innerHTML = html
  return element
}

describe('lineNotes', () => {
  it('maps the k-th footnote marker to the k-th note with a marker', () => {
    const line = row('ab<sup class="footnote-marker">1</sup>cd<sup>x</sup>ef<sup class="footnote-marker">2</sup>')
    const notes = [note(0, true), note(1, false), note(2, true)]
    const markers = line.querySelectorAll('sup.footnote-marker')
    expect(notesForMarker(markers[0]!, line, notes).map((n) => n.noteIndex)).toEqual([0])
    expect(notesForMarker(markers[1]!, line, notes).map((n) => n.noteIndex)).toEqual([2])
  })

  it('skips a marker whose body follows inline', () => {
    const line = row('a<sup class="footnote-marker">1</sup> <i class="footnote">inline</i>b<sup class="footnote-marker">2</sup>')
    const markers = line.querySelectorAll('sup.footnote-marker')
    expect(notesForMarker(markers[0]!, line, [note(0, true)])).toEqual([])
    expect(notesForMarker(markers[1]!, line, [note(0, true)]).map((n) => n.noteIndex)).toEqual([0])
  })

  it('a marker with no note left shows nothing', () => {
    const line = row('<sup class="footnote-marker">1</sup><sup class="footnote-marker">2</sup>')
    expect(notesForMarker(line.querySelectorAll('sup.footnote-marker')[1]!, line, [note(0, true)])).toEqual([])
  })

  it('appends one empty marker for unmarked notes, after all text', () => {
    const html = 'ab<b>cd</b>'
    expect(appendUnmarkedNotesMarker(html, [note(0, true)])).toBe(html)
    const withMarker = appendUnmarkedNotesMarker(html, [note(0, false), note(1, false)])
    expect(withMarker).toBe(`${html}<sup class="line-note-marker"></sup>`)
    const line = row(withMarker)
    expect(line.textContent).toBe('abcd')
    const notes = [note(0, false), note(1, true), note(2, false)]
    expect(notesForMarker(line.querySelector('sup.line-note-marker')!, line, notes).map((n) => n.noteIndex)).toEqual([0, 2])
  })

  it('signature changes when notes arrive', () => {
    expect(lineNotesSig([])).toBe('')
    expect(lineNotesSig([note(0, true)])).not.toBe('')
  })
  it('finds the marker of a note (inverse of notesForMarker)', () => {
    const line = row('ab<sup class="footnote-marker">1</sup>cd<sup class="footnote-marker">2</sup><sup class="line-note-marker"></sup>')
    const notes = [note(0, true), note(1, false), note(2, true)]
    const markers = line.querySelectorAll('sup.footnote-marker')
    expect(markerForNote(line, notes, 0)).toBe(markers[0])
    expect(markerForNote(line, notes, 2)).toBe(markers[1])
    expect(markerForNote(line, notes, 1)).toBe(line.querySelector('sup.line-note-marker'))
    expect(markerForNote(line, notes, 7)).toBeNull()
  })
})
