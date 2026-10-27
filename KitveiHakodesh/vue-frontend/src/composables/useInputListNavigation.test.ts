// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { useInputListNavigation } from './useInputListNavigation'

/**
 * The combobox keyboard model, from the angle the full-text search results list
 * needs it: a WINDOWED list, where the highlight has to stay an index and the
 * reveal has to go through the list's own scroller, and a STREAMING list, whose
 * count grows while the user is holding an arrow down.
 *
 * No `containerElement` anywhere below, on purpose: these cases go through the
 * `scrollIntoView` hook, which is the path a windowed list takes — there the nth
 * mounted element is not the nth item.
 */

/** `cancelable` so preventDefault actually records, as it does in the browser. */
function keydown(code: string, modifiers: KeyboardEventInit = {}) {
  return new KeyboardEvent('keydown', { code, cancelable: true, ...modifiers })
}

function setup(count: number) {
  let liveCount = count
  const revealed: number[] = []
  const activated: { index: number; openInNewTab: boolean }[] = []
  const nav = useInputListNavigation({
    getCount: () => liveCount,
    onActivate: (index, openInNewTab) => activated.push({ index, openInNewTab }),
    scrollIntoView: (index) => revealed.push(index),
  })
  return {
    ...nav,
    revealed,
    activated,
    setCount: (next: number) => { liveCount = next },
  }
}

describe('useInputListNavigation', () => {
  it('enters the list at the first item on ArrowDown and the last on ArrowUp', () => {
    const down = setup(5)
    expect(down.onKeydown(keydown('ArrowDown'))).toBe(true)
    expect(down.activeIndex.value).toBe(0)

    const up = setup(5)
    expect(up.onKeydown(keydown('ArrowUp'))).toBe(true)
    expect(up.activeIndex.value).toBe(4)
  })

  it('reveals the highlighted item by its DATA index, through the given hook', () => {
    const list = setup(500)
    list.onKeydown(keydown('ArrowDown'))
    list.onKeydown(keydown('ArrowDown'))
    list.onKeydown(keydown('PageDown'))
    // Indexes into the data, not into whatever rows happen to be mounted — the
    // whole reason a windowed list supplies the hook.
    expect(list.revealed).toEqual([0, 1, 11])
    expect(list.activeIndex.value).toBe(11)
  })

  it('leaves the caret keys alone: ArrowLeft/ArrowRight and plain Home/End', () => {
    const list = setup(5)
    list.onKeydown(keydown('ArrowDown'))
    for (const code of ['ArrowLeft', 'ArrowRight', 'Home', 'End']) {
      const event = keydown(code)
      expect(list.onKeydown(event)).toBe(false)
      expect(event.defaultPrevented).toBe(false)
    }
    // ...and none of them moved the highlight.
    expect(list.activeIndex.value).toBe(0)
  })

  it('takes Enter only once something is highlighted', () => {
    const list = setup(5)
    // Nothing highlighted: the field keeps its own Enter (run the search).
    const beforeEntering = keydown('Enter')
    expect(list.onKeydown(beforeEntering)).toBe(false)
    expect(beforeEntering.defaultPrevented).toBe(false)
    expect(list.activated).toEqual([])

    list.onKeydown(keydown('ArrowDown'))
    list.onKeydown(keydown('ArrowDown'))
    expect(list.onKeydown(keydown('Enter'))).toBe(true)
    expect(list.activated).toEqual([{ index: 1, openInNewTab: false }])
  })

  it('stops at the last loaded item and moves on when more stream in', () => {
    const list = setup(2)
    list.onKeydown(keydown('ArrowDown'))
    list.onKeydown(keydown('ArrowDown'))
    list.onKeydown(keydown('ArrowDown'))
    expect(list.activeIndex.value).toBe(1)

    // A streaming flush appends; the next ArrowDown continues from where it stopped.
    list.setCount(4)
    list.onKeydown(keydown('ArrowDown'))
    expect(list.activeIndex.value).toBe(2)
  })

  it('does nothing at all while the list is empty', () => {
    const list = setup(0)
    for (const code of ['ArrowDown', 'ArrowUp', 'Enter', 'PageDown']) {
      expect(list.onKeydown(keydown(code))).toBe(false)
    }
    expect(list.activeIndex.value).toBe(-1)
    expect(list.revealed).toEqual([])
  })

  it('jumps to the ends on Ctrl+Home / Ctrl+End', () => {
    const list = setup(100)
    expect(list.onKeydown(keydown('End', { ctrlKey: true }))).toBe(true)
    expect(list.activeIndex.value).toBe(99)
    expect(list.onKeydown(keydown('Home', { ctrlKey: true }))).toBe(true)
    expect(list.activeIndex.value).toBe(0)
  })
})
