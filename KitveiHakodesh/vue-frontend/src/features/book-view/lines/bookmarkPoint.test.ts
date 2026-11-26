// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { bookmarkFromPoint, countLetters, selectionPointInRow } from './bookmarkPoint'

describe('bookmarkFromPoint', () => {
  it('maps a point on a line to the stored bookmark, named by its TOC path', () => {
    const bookmark = bookmarkFromPoint(
      { id: 42, lineIndex: 7 },
      { letterOffset: 3, selectionLetterCount: 5, snippet: 'abc' },
      '  Chapter 1 / Verse 2 ',
      1000,
    )
    expect(bookmark).toEqual({
      id: 1000, lineId: 42, lineIndex: 7, letterOffset: 3, selectionLetterCount: 5,
      label: 'Chapter 1 / Verse 2', tocPath: 'Chapter 1 / Verse 2', snippet: 'abc',
    })
  })

  it('falls back to the line number without a TOC path', () => {
    const bookmark = bookmarkFromPoint({ id: 1, lineIndex: 0 }, null, '  ', 5)
    expect(bookmark?.label).toBe('1')
    expect(bookmark?.tocPath).toBeUndefined()
    expect(bookmark?.letterOffset).toBeUndefined()
  })

  it('refuses placeholder lines', () => {
    expect(bookmarkFromPoint({ id: -1, lineIndex: 3 }, null, 'x')).toBeNull()
    expect(bookmarkFromPoint({ id: 9, lineIndex: -1 }, null, 'x')).toBeNull()
  })
})

describe('selectionPointInRow', () => {
  function rig() {
    const scroller = document.createElement('div')
    scroller.innerHTML = '<div data-index="4"><div class="line">ab, <b>cd</b> ef</div></div><div data-index="5"><div class="line">gh ij</div></div>'
    document.body.appendChild(scroller)
    return scroller
  }

  it('counts letters before and inside a selection within one row', () => {
    const scroller = rig()
    const bold = scroller.querySelector('b')!.firstChild!
    const range = document.createRange()
    range.setStart(bold, 1)
    range.setEnd(bold, 2)
    const result = selectionPointInRow(range, scroller)
    expect(result?.point).toEqual({ lineIndex: 4, letterOffset: 3, selectionLetterCount: 1, snippet: 'd' })
    expect(countLetters('ab, c')).toBe(3)
  })

  it('cuts a selection running past its row at the row end', () => {
    const scroller = rig()
    const first = scroller.querySelector('.line')!.firstChild!
    const second = scroller.querySelectorAll('.line')[1]!.firstChild!
    const range = document.createRange()
    range.setStart(first, 1)
    range.setEnd(second, 2)
    const result = selectionPointInRow(range, scroller)
    expect(result?.point).toMatchObject({ lineIndex: 4, letterOffset: 1, selectionLetterCount: 5 })
  })
})
