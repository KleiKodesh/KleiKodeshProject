/**
 * @vitest-environment jsdom
 *
 * The copy builder is what makes a selection spanning many search results paste as
 * every one of those results — including the ones the windowed scroller never mounted, which
 * is the whole reason it reads the model instead of the DOM selection.
 *
 * These tests pin the two rules that a live probe cannot pin cheaply: that the row
 * range is honoured exactly (no off-by-one at either end, unmounted rows included),
 * and that a single-row selection is REFUSED so the native copy keeps it.
 *
 * jsdom rather than the suite-wide node environment (see vitest.config.ts), so the
 * builder runs its REAL entity decoding — htmlToText needs a DOM. A hand-written
 * stand-in decoder was the alternative, and that is exactly the kind of thing that
 * diverges from the browser at the cases that matter: the spurious-space bug the
 * abutting-mark test below guards against lived in this very step.
 */
import { describe, it, expect } from 'vitest'
import { buildSelectedResultsHtml, resultReference } from './fullTextSearchResultsCopy'
import type { FullTextSearchResult } from './fullTextSearchTypes'

/** A result carrying just the fields the copy path reads, plus its (already rendered)
 *  snippet HTML and citation parts, which the real list resolves on demand and hands in
 *  through the renderers. */
type TestResult = FullTextSearchResult & { snippet: string; bookTitle: string; tocText: string }
function makeResult(lineId: number, bookTitle: string, tocText: string, snippet: string) {
  return { lineId, bookId: 1, bookTitle, tocText, snippet } as TestResult
}

const RESULTS = [
  makeResult(1, 'BookOne', 'ChapterA', 'first snippet'),
  makeResult(2, 'BookOne', 'ChapterB', 'second snippet'),
  makeResult(3, 'BookTwo', '', 'third snippet'),
  makeResult(4, 'BookTwo', 'ChapterC', 'fourth snippet'),
]

/** Identity renderer — the censor is exercised by its own suite. */
const passThrough = (result: FullTextSearchResult) => (result as TestResult).snippet
const referenceOf = (result: FullTextSearchResult) => {
  const r = result as TestResult
  return resultReference(r.bookTitle, r.tocText)
}

function build(range: { firstRowIndex: number; lastRowIndex: number } | null) {
  return buildSelectedResultsHtml(RESULTS, range, passThrough, referenceOf, false)
}

describe('resultReference', () => {
  it('joins the book title and TOC path the way the rows display them', () => {
    expect(resultReference('BookOne', 'ChapterA')).toBe('BookOne › ChapterA')
  })

  it('is the title alone when the result has no TOC path', () => {
    expect(resultReference('BookTwo', '')).toBe('BookTwo')
  })
})

describe('buildSelectedResultsHtml', () => {
  it('returns null without a row range, leaving the copy to the browser', () => {
    expect(build(null)).toBeNull()
  })

  it('refuses a selection confined to one row', () => {
    // The DOM holds exactly what the user highlighted there — substituting the whole
    // result would over-copy a double-clicked word or a selected book title.
    expect(build({ firstRowIndex: 1, lastRowIndex: 1 })).toBeNull()
  })

  it('emits every result in the range, both ends included', () => {
    const html = build({ firstRowIndex: 0, lastRowIndex: 3 })!
    for (const result of RESULTS) {
      expect(html).toContain(result.snippet)
      expect(html).toContain(referenceOf(result))
    }
  })

  it('emits exactly the rows in the range and no neighbours', () => {
    const html = build({ firstRowIndex: 1, lastRowIndex: 2 })!
    expect(html).toContain('second snippet')
    expect(html).toContain('third snippet')
    expect(html).not.toContain('first snippet')
    expect(html).not.toContain('fourth snippet')
  })

  it('gives each result its own reference, so a range reads as a citation list', () => {
    const html = build({ firstRowIndex: 0, lastRowIndex: 3 })!
    // One bold reference per result — this is what makes a multi-result paste
    // attributable rather than a wall of unlabelled text.
    expect(html.match(/<b>/g)).toHaveLength(RESULTS.length)
  })

  it('keeps document order regardless of how the range is expressed', () => {
    const html = build({ firstRowIndex: 0, lastRowIndex: 2 })!
    expect(html.indexOf('first snippet')).toBeLessThan(html.indexOf('second snippet'))
    expect(html.indexOf('second snippet')).toBeLessThan(html.indexOf('third snippet'))
  })

  it('skips a row index the result set does not have', () => {
    // The render window can outrun the results array while a search streams in.
    const html = build({ firstRowIndex: 2, lastRowIndex: 99 })
    expect(html).toContain('third snippet')
    expect(html).toContain('fourth snippet')
  })

  it('returns null when every row in the range is missing', () => {
    expect(build({ firstRowIndex: 50, lastRowIndex: 99 })).toBeNull()
  })

  it('strips the search-match markup out of a snippet', () => {
    // A highlight is a reading aid for this search, not part of the quoted source —
    // and carrying <mark> into Word imports its styling.
    const results = [
      makeResult(1, 'BookOne', 'ChapterA', 'plain <mark class="search-match">hit</mark> text'),
      makeResult(2, 'BookOne', 'ChapterB', 'second'),
    ]
    const html = buildSelectedResultsHtml(
      results,
      { firstRowIndex: 0, lastRowIndex: 1 },
      passThrough,
      referenceOf,
      false,
    )!
    expect(html).not.toContain('<mark')
    expect(html).toContain('plain hit text')
  })

  it('does not insert a space where a mark abuts a character', () => {
    // The bug this guards: replacing each tag with a SPACE splits a word wherever a
    // match boundary sits mid-word. Real snippets do this constantly — the tokenizer
    // treats the Hebrew word-joining hyphen as a token boundary that still renders,
    // so a match beginning after one puts <mark> flush against a visible character.
    // A space there breaks a word that belongs together.
    const results = [
      makeResult(1, 'BookOne', 'ChapterA', 'wor<mark class="search-match">d</mark>s and mor<mark>e</mark>'),
      makeResult(2, 'BookOne', 'ChapterB', 'second'),
    ]
    const html = buildSelectedResultsHtml(
      results,
      { firstRowIndex: 0, lastRowIndex: 1 },
      passThrough,
      referenceOf,
      false,
    )!
    expect(html).toContain('words and more')
    expect(html).not.toContain('wor d')
  })

  it('leaves the source text spacing alone', () => {
    // No whitespace collapsing: the snippet is emitted verbatim by the renderer, so a
    // run of spaces in the output is a run in the source. Squeezing them here would
    // also have hidden the spurious-space bug above behind a plausible single space.
    const results = [
      makeResult(1, 'BookOne', 'ChapterA', 'two  spaces'),
      makeResult(2, 'BookOne', 'ChapterB', 'second'),
    ]
    const html = buildSelectedResultsHtml(
      results,
      { firstRowIndex: 0, lastRowIndex: 1 },
      passThrough,
      referenceOf,
      false,
    )!
    expect(html).toContain('two  spaces')
  })

  it('decodes the entities a snippet carries, then escapes once', () => {
    // The renderer collapses thin and non-breaking spaces to entities, and the
    // tokenizer treats them as separators sitting right against a match — so they
    // reach this path routinely. They must arrive in the clipboard as the characters
    // they stand for, not as their entity source, and the ampersand of a real `&amp;`
    // must not be re-escaped into `&amp;amp;` on the way out.
    const results = [
      makeResult(1, 'BookOne', 'ChapterA', 'a&thinsp;b&nbsp;c &amp; d'),
      makeResult(2, 'BookOne', 'ChapterB', 'second'),
    ]
    const html = buildSelectedResultsHtml(
      results,
      { firstRowIndex: 0, lastRowIndex: 1 },
      passThrough,
      referenceOf,
      false,
    )!
    // Asserts the CHARACTERS, not merely that the entity source is gone: a
    // hand-rolled decoder that maps both of these to an ordinary space would satisfy
    // a "no longer contains &thinsp;" check while quietly flattening two distinct
    // characters. Only real decoding produces the thin and non-breaking spaces.
    expect(html).toContain('a b c')
    expect(html).not.toContain('&thinsp;')
    expect(html).not.toContain('&nbsp;')
    // Escaped exactly once on the way out — never re-escaped into &amp;amp;.
    expect(html).toContain('&amp; d')
    expect(html).not.toContain('&amp;amp;')
  })

  it('escapes text that would otherwise be read as markup', () => {
    const results = [
      makeResult(1, 'Book <One>', 'A & B', 'snippet with < and &'),
      makeResult(2, 'BookTwo', 'C', 'second'),
    ]
    const html = buildSelectedResultsHtml(
      results,
      { firstRowIndex: 0, lastRowIndex: 1 },
      passThrough,
      referenceOf,
      false,
    )!
    expect(html).toContain('Book &lt;One&gt;')
    expect(html).toContain('A &amp; B')
    expect(html).not.toContain('<One>')
  })

  it('omits the snippet line for a result whose snippet is empty', () => {
    const results = [
      makeResult(1, 'BookOne', 'ChapterA', ''),
      makeResult(2, 'BookOne', 'ChapterB', 'second'),
    ]
    const html = buildSelectedResultsHtml(
      results,
      { firstRowIndex: 0, lastRowIndex: 1 },
      passThrough,
      referenceOf,
      false,
    )!
    // The reference still lands; only the empty text block is skipped.
    expect(html).toContain('BookOne › ChapterA')
    expect(html.match(/<b>/g)).toHaveLength(2)
  })

  it('routes the snippet through the caller-supplied renderer', () => {
    // The component passes its censoring renderer here, so a copied snippet is
    // censored the same way the row on screen is.
    const html = buildSelectedResultsHtml(
      RESULTS,
      { firstRowIndex: 0, lastRowIndex: 1 },
      (result) => passThrough(result).replace("first", "RENDERED"),
      referenceOf,
      false,
    )!
    expect(html).toContain('RENDERED snippet')
  })
})
