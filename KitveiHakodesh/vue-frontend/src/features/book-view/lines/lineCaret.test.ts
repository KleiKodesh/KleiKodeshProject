/**
 * @vitest-environment jsdom
 */
import { describe, it, expect, beforeEach } from 'vitest'
import { mayTakeFocus, placeCaretAtViewportStart } from './lineCaret'

describe('placeCaretAtViewportStart', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
    window.getSelection()?.removeAllRanges()
  })

  it('leaves a real selection the reader is holding alone', () => {
    const viewport = document.createElement('div')
    viewport.innerHTML = '<div class="line">שורה</div>'
    document.body.appendChild(viewport)
    const range = document.createRange()
    range.selectNodeContents(viewport.firstChild!)
    window.getSelection()!.addRange(range)

    placeCaretAtViewportStart(viewport)

    expect(window.getSelection()!.isCollapsed).toBe(false)
  })

  it('does nothing without a viewport', () => {
    placeCaretAtViewportStart(null)
    expect(window.getSelection()!.rangeCount).toBe(0)
  })
})

describe('mayTakeFocus', () => {
  it('refuses when focus is elsewhere', () => {
    document.body.innerHTML = '<div id="v"></div><input id="i">'
    ;(document.getElementById('i') as HTMLElement).focus()
    expect(mayTakeFocus(document.getElementById('v'))).toBe(false)
  })
})
