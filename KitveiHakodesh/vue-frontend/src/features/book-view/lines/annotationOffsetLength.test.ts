/**
 * annotationOffsetLength must agree exactly with the offset space applyUserHighlights
 * walks, because a whole-line annotation on a line the virtualizer never mounted is
 * measured by the former and rendered by the latter (see appendUnmountedSelectedLines).
 *
 * The check used throughout is behavioural rather than a hardcoded number: highlight
 * the whole line by its measured length and assert the mark wraps ALL of the text and
 * leaves nothing outside it. A length that is off by even one leaves a stray character
 * beyond the closing tag, which is exactly the bug this guards against.
 */
import { describe, it, expect } from 'vitest'
import {
  annotationOffsetLength,
  applyUserHighlights,
  contentInAnnotationOffsetSpace,
} from './useBookViewLineRenderer'
import {
  DIVINE_NAME_MODES,
  ELOKIM_MODES,
  type DivineNameMode,
  type ElokimMode,
} from '@/utils/censorDivineNames'
import type { Highlight } from './useBookViewHighlights'

const YELLOW_ARGB = 0xffffeb3b

function highlightWholeLine(content: string): string {
  const highlight = {
    id: 1,
    lineId: 1,
    startOffset: 0,
    endOffset: annotationOffsetLength(content),
    colorArgb: YELLOW_ARGB,
  } as Highlight
  return applyUserHighlights(content, [highlight])
}

/** Text that ended up outside the <mark> — always a length disagreement. */
function textOutsideHighlight(highlighted: string): string {
  const withoutMarkedRun = highlighted.replace(/<mark\b[^>]*>[\s\S]*<\/mark>/, '')
  return withoutMarkedRun.replace(/<[^>]*>/g, '')
}

describe('annotationOffsetLength', () => {
  it('counts plain text one position per character', () => {
    expect(annotationOffsetLength('abc')).toBe(3)
  })

  it('ignores tag characters', () => {
    expect(annotationOffsetLength('<b>abc</b>')).toBe(3)
    expect(annotationOffsetLength('a<span class="word-link">b</span>c')).toBe(3)
  })

  it('counts each HTML entity as exactly one position', () => {
    expect(annotationOffsetLength('a&thinsp;b')).toBe(3)
    expect(annotationOffsetLength('&nbsp;&nbsp;')).toBe(2)
  })

  it('treats a bare ampersand as an ordinary character', () => {
    // No terminating semicolon within the lookahead, so it is not an entity.
    expect(annotationOffsetLength('a & b')).toBe(5)
  })

  it('skips Hebrew diacritics, the maqaf included', () => {
    // Two base letters, each carrying one vowel point.
    expect(annotationOffsetLength('אָבָ')).toBe(2)
    // The maqaf is inside the annotation-stripped range (unlike the search space,
    // which keeps it as a space) — see isDiacriticChar.
    expect(annotationOffsetLength('א־ב')).toBe(2)
  })

  it('counts an empty-marker <sup> as nothing', () => {
    // Note and word-link markers render their glyph from CSS ::before, so they hold
    // no text and must not occupy an offset.
    const content = 'ab<sup class="user-note-marker" data-note-id="7"></sup>c'
    expect(annotationOffsetLength(content)).toBe(3)
  })

  it('returns 0 for content with no countable characters', () => {
    expect(annotationOffsetLength('')).toBe(0)
    expect(annotationOffsetLength('<b></b>')).toBe(0)
  })
})

describe('annotationOffsetLength agrees with applyUserHighlights', () => {
  const cases: Array<[string, string]> = [
    ['plain text', 'abc'],
    ['inline markup', 'a<b>bc</b>d'],
    ['an entity', 'a&thinsp;b'],
    ['adjacent entities', '&nbsp;&nbsp;x'],
    ['diacritics', 'אָבָג'],
    ['a maqaf', 'א־ב'],
    ['an empty marker', 'ab<sup class="user-note-marker"></sup>c'],
    ['markup, entity and diacritics together', '<b>אָ</b>&thinsp;ב'],
  ]

  for (const [description, content] of cases) {
    it(`leaves nothing outside a whole-line highlight — ${description}`, () => {
      const highlighted = highlightWholeLine(content)
      expect(highlighted).toContain('<mark')
      expect(textOutsideHighlight(highlighted)).toBe('')
    })
  }

  it('does not over-count, which would drop the closing tag', () => {
    // An endOffset past the true end never closes the mark; every case above would
    // pass the "nothing outside" check while producing unbalanced HTML, so assert
    // the mark is closed too.
    for (const [, content] of cases) {
      const highlighted = highlightWholeLine(content)
      expect(highlighted).toContain('</mark>')
    }
  })
})

/**
 * The render pipeline censors divine names BEFORE applyUserHighlights runs, so the
 * offset space a highlight is resolved against is the POST-CENSOR text. Several
 * censor modes change the character count — doubleYud and heApostrophe collapse the
 * four-letter name, the hyphen modes insert U+2011 separators (which sit outside the
 * 0x0591-0x05C7 diacritic range and therefore DO occupy offsets) — so a whole-line
 * highlight measured on raw model text lands short or long once the line is rendered.
 *
 * contentInAnnotationOffsetSpace exists to close that gap; these tests pin it shut for
 * every mode combination, since a single uncovered pairing is a silently misplaced
 * highlight on real text.
 */
describe('annotationOffsetLength in the post-censor offset space', () => {
  // A verse-shaped line carrying the tetragrammaton, an אלהים-family name and three
  // of the other names, lifted verbatim from censorDivineNames.test.ts so the fixture
  // stays in step with whatever that suite already proves the censor does to it.
  const LINE_WITH_DIVINE_NAMES = 'וַיֹּאמֶר יְהוָה אֱלֹהִים אֲדֹנָי שַׁדַּי אֵל יָהּ'

  /** Diacritics untouched — the branch the real renderer takes at the default setting. */
  const KEEP_DIACRITICS = 0

  function offsetSpace(mode: DivineNameMode, elokim: ElokimMode): string {
    return contentInAnnotationOffsetSpace(LINE_WITH_DIVINE_NAMES, KEEP_DIACRITICS, {
      mode,
      elokim,
      otherNames: [],
    })
  }

  it('is not measuring the same thing as the raw content, for at least one mode', () => {
    // Guards the rest of this block from passing vacuously: if the censor ever stopped
    // changing the length of this fixture, every assertion below would hold trivially
    // and the hazard would go unguarded. Prove the hazard is real before relying on it.
    const rawLength = annotationOffsetLength(LINE_WITH_DIVINE_NAMES)
    const censoredLengths = DIVINE_NAME_MODES.flatMap((mode) =>
      ELOKIM_MODES.map((elokim) => annotationOffsetLength(offsetSpace(mode, elokim))),
    )
    expect(censoredLengths.some((length) => length !== rawLength)).toBe(true)
  })

  for (const mode of DIVINE_NAME_MODES) {
    for (const elokim of ELOKIM_MODES) {
      it(`leaves nothing outside a whole-line highlight — mode ${mode}, elokim ${elokim}`, () => {
        const content = offsetSpace(mode, elokim)
        const highlighted = highlightWholeLine(content)
        expect(highlighted).toContain('<mark')
        // An endOffset past the true end never emits the closing tag, which would let
        // the "nothing outside" check below pass on unbalanced HTML.
        expect(highlighted).toContain('</mark>')
        expect(textOutsideHighlight(highlighted)).toBe('')
      })
    }
  }

  it('leaves text outside the mark when the raw content is measured instead', () => {
    // The bug this whole block exists for, asserted rather than described: measuring the
    // model text and then highlighting the censored text under-counts wherever the censor
    // expands the name, stranding the tail of the line beyond the closing tag.
    const rawLength = annotationOffsetLength(LINE_WITH_DIVINE_NAMES)
    const censored = offsetSpace('hyphen', 'hyphen')
    expect(annotationOffsetLength(censored)).toBeGreaterThan(rawLength)

    const highlighted = applyUserHighlights(censored, [
      { id: 1, lineId: 1, startOffset: 0, endOffset: rawLength, colorArgb: YELLOW_ARGB } as Highlight,
    ])
    expect(textOutsideHighlight(highlighted)).not.toBe('')
  })
})
