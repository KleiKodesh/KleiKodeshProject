import { describe, it, expect } from 'vitest'
import {
  keysToCollapse,
  widthOf,
  TOOLBAR_OVERFLOW_ORDER,
  TOOLBAR_SEPARATOR_IN_BUTTONS,
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
    expect(ALL).toEqual(['export-to-word', 'sync-commentaries', 'diacritics', 'zoom'])
  })

  it('charges a separator less than a button', () => {
    expect(TOOLBAR_SEPARATOR_IN_BUTTONS).toBeLessThan(1)
    expect(TOOLBAR_SEPARATOR_IN_BUTTONS).toBeGreaterThan(0)
  })
})

describe('widthOf', () => {
  it('charges zoom for both buttons AND the separator that collapses with them', () => {
    expect(widthOf('zoom')).toBe(2 + TOOLBAR_SEPARATOR_IN_BUTTONS)
  })

  it('charges every other control one button', () => {
    for (const key of ALL.filter((k) => k !== 'zoom')) expect(widthOf(key)).toBe(1)
  })
})

describe('keysToCollapse', () => {
  const PINNED = 8 + TOOLBAR_SEPARATOR_IN_BUTTONS

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
    expect(keysToCollapse(room, PINNED, ALL)).toEqual(['export-to-word'])
  })

  it('collapses one control for a toolbar barely short of fitting', () => {
    const room = roomForEverything(PINNED) - 1
    expect(keysToCollapse(room, PINNED, ALL)).toEqual(['export-to-word'])
  })

  it('takes the next control only when one is no longer enough', () => {
    const room = roomForEverything(PINNED) - 2 * BUTTON
    expect(keysToCollapse(room, PINNED, ALL)).toEqual(['export-to-word', 'sync-commentaries'])
  })

  it('adds one control per button of deficit', () => {
    const full = roomForEverything(PINNED)
    expect(keysToCollapse(full - 3 * BUTTON, PINNED, ALL)).toEqual([
      'export-to-word',
      'sync-commentaries',
      'diacritics',
    ])
  })

  it('counts zoom as the two buttons it is', () => {
    // Short by four buttons: export, sync and diacritics free one each, and zoom's two
    // clear the rest. Were zoom counted as one, the toolbar would still be a button over
    // with nothing left to collapse.
    const room = roomForEverything(PINNED) - 4 * BUTTON
    expect(keysToCollapse(room, PINNED, ALL)).toEqual([
      'export-to-word',
      'sync-commentaries',
      'diacritics',
      'zoom',
    ])
  })

  it('holds a collapsed control collapsed until there is real room again', () => {
    // Room came back, but only just: a control that is already collapsed stays collapsed
    // until the surplus clears the hysteresis margin, so a pane parked on the boundary does
    // not flip it in and out.
    const barelyBack = roomForEverything(PINNED)
    expect(keysToCollapse(barelyBack, PINNED, ALL, ['export-to-word'])).toEqual([
      'export-to-word',
    ])
  })

  it('lets a collapsed control back once the room clears the margin', () => {
    const clearlyBack = roomForEverything(PINNED) + BUTTON
    expect(keysToCollapse(clearlyBack, PINNED, ALL, ['export-to-word'])).toEqual([])
  })

  it('collapses without delay in the other direction', () => {
    // Only coming back is damped. A control that no longer fits goes now - a button left
    // overflowing the pane for a few pixels of resize would be the bug, not the fix.
    const short = roomForEverything(PINNED) - BUTTON
    expect(keysToCollapse(short, PINNED, ALL, [])).toEqual(['export-to-word'])
  })

  it('never collapses more than what is present', () => {
    const present: ToolbarOverflowKey[] = ['export-to-word', 'diacritics']
    expect(keysToCollapse(1, PINNED, present)).toEqual(present)
  })

  it('leaves a book without the sync control unaffected by its absence', () => {
    // A book with no commentaries renders no sync button, so the control after it in the
    // order is the second concession rather than the third.
    const present: ToolbarOverflowKey[] = ['export-to-word', 'diacritics', 'zoom']
    const room = (PINNED + widthsOf(present)) * BUTTON - 2 * BUTTON
    expect(keysToCollapse(room, PINNED, present)).toEqual(['export-to-word', 'diacritics'])
  })
})
