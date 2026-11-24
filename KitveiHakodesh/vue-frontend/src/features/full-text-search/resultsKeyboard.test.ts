// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { wantsPreviewToggle } from './resultsKeyboard'

/** A keydown dispatched from `target` (the search field, the scroller, body…). */
function keydown(code: string, target: EventTarget, modifiers: KeyboardEventInit = {}) {
  const event = new KeyboardEvent('keydown', { code, cancelable: true, ...modifiers })
  Object.defineProperty(event, 'target', { value: target })
  return event
}

describe('wantsPreviewToggle', () => {
  it('takes Space from the page when a result is highlighted', () => {
    expect(wantsPreviewToggle(keydown('Space', document.body), true)).toBe(true)
  })

  it('leaves Space alone with nothing highlighted (it is the scroller page-down)', () => {
    expect(wantsPreviewToggle(keydown('Space', document.body), false)).toBe(false)
  })

  it('never swallows a space typed into the search field', () => {
    // The field forwards every keydown to the list; a highlight set by ArrowDown is
    // still there while the user keeps typing the query.
    const input = document.createElement('input')
    expect(wantsPreviewToggle(keydown('Space', input), true)).toBe(false)
    const textarea = document.createElement('textarea')
    expect(wantsPreviewToggle(keydown('Space', textarea), true)).toBe(false)
  })

  it('ignores modified Space and other keys', () => {
    expect(wantsPreviewToggle(keydown('Space', document.body, { ctrlKey: true }), true)).toBe(false)
    expect(wantsPreviewToggle(keydown('Space', document.body, { altKey: true }), true)).toBe(false)
    expect(wantsPreviewToggle(keydown('Enter', document.body), true)).toBe(false)
  })
})
