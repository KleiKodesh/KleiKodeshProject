/**
 * @vitest-environment jsdom
 *
 * The keyboard caret an opening leaves behind. Caret browsing (F7) navigates from the
 * document's collapsed selection, so an opening that only focuses the viewport leaves
 * the arrows starting from wherever the browser chose — in a windowed scroller, the
 * top of the mounted window rather than the line that was opened. See lineCaret.ts.
 */
import { describe, it, expect, beforeEach } from 'vitest'
import { placeCaretAtRowStart } from './lineCaret'

/** A scroller whose mounted window starts well above the row that was landed on. */
function mountScroller(): { rows: HTMLElement[] } {
  const scroller = document.createElement('div')
  const rows = [0, 1, 2].map((index) => {
    const row = document.createElement('div')
    row.dataset['index'] = String(index)
    const line = document.createElement('div')
    line.className = 'line'
    line.textContent = `שורה ${index}`
    row.appendChild(line)
    scroller.appendChild(row)
    return row
  })
  document.body.appendChild(scroller)
  return { rows }
}

describe('placeCaretAtRowStart', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
    window.getSelection()?.removeAllRanges()
  })

  it('collapses the caret onto the opened row, not the first row mounted above it', () => {
    const { rows } = mountScroller()

    placeCaretAtRowStart(rows[2]!)

    const selection = window.getSelection()!
    expect(selection.isCollapsed).toBe(true)
    expect(selection.anchorOffset).toBe(0)
    expect(rows[2]!.contains(selection.anchorNode)).toBe(true)
  })

  it('leaves a real selection the reader is holding alone', () => {
    const { rows } = mountScroller()
    const readersLine = rows[0]!.querySelector('.line')!
    const readersRange = document.createRange()
    readersRange.selectNodeContents(readersLine)
    const selection = window.getSelection()!
    selection.addRange(readersRange)

    placeCaretAtRowStart(rows[2]!)

    expect(selection.isCollapsed).toBe(false)
    expect(readersLine.contains(selection.anchorNode)).toBe(true)
  })

  it('replaces a collapsed caret left somewhere else in the document', () => {
    const elsewhere = document.createElement('p')
    elsewhere.textContent = 'מסך קודם'
    document.body.appendChild(elsewhere)
    const { rows } = mountScroller()
    const staleRange = document.createRange()
    staleRange.setStart(elsewhere.firstChild!, 2)
    staleRange.collapse(true)
    const selection = window.getSelection()!
    selection.addRange(staleRange)

    placeCaretAtRowStart(rows[1]!)

    expect(selection.isCollapsed).toBe(true)
    expect(rows[1]!.contains(selection.anchorNode)).toBe(true)
  })

  it('does nothing for a row with no rendered line', () => {
    mountScroller()
    const empty = document.createElement('div')

    placeCaretAtRowStart(empty)
    placeCaretAtRowStart(null)

    expect(window.getSelection()!.rangeCount).toBe(0)
  })
})
