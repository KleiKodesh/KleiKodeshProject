import { describe, it, expect } from 'vitest'
import {
  keysToCollapse,
  widthOf,
  TOOLBAR_OVERFLOW_ORDER,
  type ToolbarOverflowKey,
} from './useBookViewToolbarOverflow'

/**
 * The toolbar's fit arithmetic.
 *
 * These exist because it failed in a way review could not see and only the screen could:
 * comparing a floored button COUNT against a cost carrying the separators' fraction threw
 * away up to a whole button, so a second control collapsed alongside the first. The
 * boundary cases below pin exactly how much has to go at each width.
 */

const BUTTON = 28
const ALL = [...TOOLBAR_OVERFLOW_ORDER] as ToolbarOverflowKey[]

/** Room a toolbar needs for `pinned` button-widths plus every collapsible control. */
function roomForEverything(pinned: number) {
  return (pinned + widthsOf(ALL)) * BUTTON
}

/** What a set of controls costs, in button-widths, straight from the rule under test. */
function widthsOf(keys: readonly ToolbarOverflowKey[]) {
  return keys.reduce((sum, key) => sum + widthOf(key), 0)
}

describe('collapse order', () => {
  it('is the order the product fixed, worst-first', () => {
    expect(ALL).toEqual([
      'book-info',
      'hebrewbooks-editions',
      'versions',
      'word-link-markers',
      'export-to-word',
      'sync-commentaries',
      'related-books',
      'clean-text',
      'zoom',
      'side-commentaries',
      'bottom-commentary',
      'bookmarks',
      'search',
      'section-nav',
      'toc',
    ])
  })
})

describe('widthOf', () => {
  const PAIRS: ToolbarOverflowKey[] = ['zoom', 'side-commentaries', 'section-nav']

  it('charges each pair for both buttons', () => {
    for (const key of PAIRS) expect(widthOf(key)).toBe(2)
  })

  it('charges the clean-text combo for its eraser plus chevron', () => {
    expect(widthOf('clean-text')).toBeGreaterThan(1)
    expect(widthOf('clean-text')).toBeLessThan(2)
  })

  it('charges every other control one button', () => {
    for (const key of ALL.filter((k) => !PAIRS.includes(k) && k !== 'clean-text')) expect(widthOf(key)).toBe(1)
  })
})

describe('keysToCollapse', () => {
  const PINNED = 8

  it('collapses nothing when everything fits exactly', () => {
    expect(keysToCollapse(roomForEverything(PINNED), PINNED, ALL)).toEqual([])
  })

  it('collapses nothing on a toolbar with room to spare', () => {
    expect(keysToCollapse(roomForEverything(PINNED) + 200, PINNED, ALL)).toEqual([])
  })

  it('assumes everything fits before it has been measured', () => {
    expect(keysToCollapse(0, PINNED, ALL)).toEqual([])
  })

  // The regression this file exists for. The more button is anchored outside the flex flow
  // and its room comes out of the toolbar's padding, so the length passed in here is already
  // net of it and one button of deficit costs exactly one control. While the button was laid
  // out in the row and charged a slot on first collapse, the control that collapsed freed a
  // button and the more button took it straight back - the deficit never moved and a second
  // control went with it.
  it('collapses exactly one control when the toolbar is one button short', () => {
    const room = roomForEverything(PINNED) - BUTTON
    expect(keysToCollapse(room, PINNED, ALL)).toEqual(['book-info'])
  })

  it('collapses one control for a toolbar barely short of fitting', () => {
    const room = roomForEverything(PINNED) - 1
    expect(keysToCollapse(room, PINNED, ALL)).toEqual(['book-info'])
  })

  it('takes the next control only when one is no longer enough', () => {
    const room = roomForEverything(PINNED) - 2 * BUTTON
    expect(keysToCollapse(room, PINNED, ALL)).toEqual(['book-info', 'hebrewbooks-editions'])
  })

  it('adds one control per button of deficit', () => {
    const full = roomForEverything(PINNED)
    expect(keysToCollapse(full - 3 * BUTTON, PINNED, ALL)).toEqual([
      'book-info',
      'hebrewbooks-editions',
      'versions',
    ])
  })

  it('counts zoom as the two buttons it is', () => {
    // Book info, editions, export and sync free one each, and zoom's two clear the rest.
    // Were zoom counted as one, the toolbar would still be a button over.
    const present: ToolbarOverflowKey[] = ['book-info', 'hebrewbooks-editions', 'export-to-word', 'sync-commentaries', 'zoom']
    const room = (PINNED + widthsOf(present)) * BUTTON - 6 * BUTTON
    expect(keysToCollapse(room, PINNED, present)).toEqual(present)
  })

  it('holds a collapsed control collapsed until there is real room again', () => {
    // Room came back, but only just: a control that is already collapsed stays collapsed
    // until the surplus clears the hysteresis margin, so a pane parked on the boundary does
    // not flip it in and out.
    const barelyBack = roomForEverything(PINNED)
    expect(keysToCollapse(barelyBack, PINNED, ALL, ['book-info'])).toEqual([
      'book-info',
    ])
  })

  it('lets a collapsed control back once the room clears the margin', () => {
    const clearlyBack = roomForEverything(PINNED) + BUTTON
    expect(keysToCollapse(clearlyBack, PINNED, ALL, ['book-info'])).toEqual([])
  })

  it('collapses without delay in the other direction', () => {
    // Only coming back is damped. A control that no longer fits goes now - a button left
    // overflowing the pane for a few pixels of resize would be the bug, not the fix.
    const short = roomForEverything(PINNED) - BUTTON
    expect(keysToCollapse(short, PINNED, ALL, [])).toEqual(['book-info'])
  })

  it('never collapses more than what is present', () => {
    const present: ToolbarOverflowKey[] = ['export-to-word', 'sync-commentaries']
    expect(keysToCollapse(1, PINNED, present)).toEqual(present)
  })

  it('leaves a book without the sync control unaffected by its absence', () => {
    // A book with no commentaries renders no sync button, so the control after it in the
    // order is the second concession rather than the third.
    const present: ToolbarOverflowKey[] = ['book-info', 'export-to-word', 'zoom']
    const room = (PINNED + widthsOf(present)) * BUTTON - 2 * BUTTON
    expect(keysToCollapse(room, PINNED, present)).toEqual(['book-info', 'export-to-word'])
  })
})
