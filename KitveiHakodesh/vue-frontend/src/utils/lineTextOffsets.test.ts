/**
 * @vitest-environment jsdom
 *
 * The two walks that let a text selection survive a re-render: a DOM position becomes
 * a character offset into the line's diacritic-stripped text, and back again. They are
 * used together — a selection's boundaries are captured with the forward walk and
 * re-created with the inverse — so anything they disagree about shows up as a
 * selection that shifts, shrinks or lands in the wrong place after a highlight.
 *
 * The round trip is the broadest property here and it needs a real DOM: the forward
 * walk uses a TreeWalker over text nodes, so a line's markup — several text nodes, an
 * empty marker element, diacritics split across nodes — is the input, not a string.
 * It is not sufficient on its own, though; see the note above the round-trip block.
 */
import { describe, it, expect } from 'vitest'
import {
  strippedLineText,
  strippedOffsetFromDomPosition,
  domPositionFromStrippedOffset,
} from './lineTextOffsets'

/** Builds a `.line` element from raw HTML, the way the renderer produces one. */
function lineFrom(html: string): HTMLElement {
  const line = document.createElement('div')
  line.className = 'line'
  line.innerHTML = html
  return line
}

/** Every text node in the line, in document order. */
function textNodesOf(line: HTMLElement): Text[] {
  const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT)
  const nodes: Text[] = []
  let node: Text | null
  while ((node = walker.nextNode() as Text | null)) nodes.push(node)
  return nodes
}

// A vowel point and a cantillation mark — both inside the stripped range (U+0591-U+05C7)
// and both skipped by the offset space.
const VOWEL = 'ָ'
const CANTILLATION = '֖'
/** The maqaf, the word-joining hyphen — stripped too, unlike in the search space. */
const MAQAF = '־'

describe('strippedLineText', () => {
  it('drops the diacritics from the line text', () => {
    const line = lineFrom(`a${VOWEL}b${CANTILLATION}c`)
    expect(strippedLineText(line)).toBe('abc')
  })

  it('reads across element boundaries and ignores empty markers', () => {
    const line = lineFrom('ab<sup class="user-note-marker" data-note-id="7"></sup><b>cd</b>')
    expect(strippedLineText(line)).toBe('abcd')
  })
})

/**
 * The round trip alone is a WEAKER guard than it looks, so do not lean on it for more
 * than it proves. It is blind to which of two equivalent caret positions the inverse
 * picks, and to whether trailing diacritics are consumed — both choices survive the
 * round trip because the forward walk maps the alternatives to the same offset. Those
 * two behaviors decide where a restored selection actually lands, and each rests on
 * its own named test below; keep them.
 */
describe('the two walks are inverses', () => {
  const cases: Array<[string, string]> = [
    ['plain text', 'abcdef'],
    ['nested markup', 'a<b>bc</b>d<i>ef</i>'],
    ['an empty note marker mid-line', 'ab<sup class="user-note-marker"></sup>cd'],
    ['a highlight wrapper', 'a<mark class="user-highlight">bc</mark>d'],
    ['diacritics', `a${VOWEL}b${CANTILLATION}c${VOWEL}d`],
    ['a maqaf', `ab${MAQAF}cd`],
    ['diacritics split across elements', `<b>a${VOWEL}</b>b${CANTILLATION}<i>c</i>`],
    ['a leading diacritic', `${VOWEL}abc`],
    ['adjacent text nodes', 'ab<span></span>cd'],
  ]

  for (const [description, html] of cases) {
    it(`round-trips every offset — ${description}`, () => {
      const line = lineFrom(html)
      const length = strippedLineText(line).length
      // Every position a selection boundary can hold, the end included.
      for (let offset = 0; offset <= length; offset++) {
        const position = domPositionFromStrippedOffset(line, offset)
        expect(position, `offset ${offset} resolved to no position`).not.toBeNull()
        const roundTripped = strippedOffsetFromDomPosition(line, position!.node, position!.offset)
        expect(roundTripped, `offset ${offset} did not survive the round trip`).toBe(offset)
      }
    })
  }
})

describe('strippedOffsetFromDomPosition', () => {
  it('counts from the start of the line, not the node', () => {
    const line = lineFrom('ab<b>cd</b>ef')
    const nodes = textNodesOf(line)
    // Offset 1 inside the third text node ("ef") is offset 5 in the line.
    expect(strippedOffsetFromDomPosition(line, nodes[2]!, 1)).toBe(5)
  })

  it('skips the diacritics before the position', () => {
    const line = lineFrom(`a${VOWEL}b${VOWEL}c`)
    const node = textNodesOf(line)[0]!
    // Raw index 4 sits after "a", its vowel, "b" and its vowel — two counted chars.
    expect(strippedOffsetFromDomPosition(line, node, 4)).toBe(2)
  })

  it('returns the full stripped length for a node outside the line', () => {
    // A Range boundary can land on an element, or on a node the line does not contain;
    // the walk runs off the end rather than throwing, and callers clamp.
    const line = lineFrom('abc')
    const stranger = document.createTextNode('xyz')
    expect(strippedOffsetFromDomPosition(line, stranger, 1)).toBe(3)
  })
})

describe('domPositionFromStrippedOffset', () => {
  it('returns null for a line with no text at all', () => {
    expect(domPositionFromStrippedOffset(lineFrom(''), 0)).toBeNull()
    expect(domPositionFromStrippedOffset(lineFrom('<b></b>'), 0)).toBeNull()
  })

  it('clamps an offset past the end to the end of the last text node', () => {
    // A stale offset — a line whose text shrank — degrades to a shorter selection
    // rather than throwing.
    const line = lineFrom('abc')
    const position = domPositionFromStrippedOffset(line, 99)!
    expect(position.node.textContent).toBe('abc')
    expect(position.offset).toBe(3)
  })

  it('keeps a letter together with the diacritics that follow it', () => {
    // Landing between a letter and its own vowel point would render as a stray
    // floating mark on the far side of the selection boundary.
    const line = lineFrom(`a${VOWEL}${CANTILLATION}b`)
    const position = domPositionFromStrippedOffset(line, 1)!
    // Raw index 3 is past "a" and both of its marks, at "b".
    expect(position.offset).toBe(3)
  })

  it('resolves a node-boundary offset to the end of the earlier node', () => {
    // Both are the same caret position to the browser; pinning which one is returned
    // keeps the function deterministic.
    const line = lineFrom('ab<b>cd</b>')
    const position = domPositionFromStrippedOffset(line, 2)!
    expect(position.node.textContent).toBe('ab')
    expect(position.offset).toBe(2)
  })

  it('reaches into a later element for an offset beyond the first node', () => {
    const line = lineFrom('ab<b>cd</b>')
    const position = domPositionFromStrippedOffset(line, 3)!
    expect(position.node.textContent).toBe('cd')
    expect(position.offset).toBe(1)
  })
})
