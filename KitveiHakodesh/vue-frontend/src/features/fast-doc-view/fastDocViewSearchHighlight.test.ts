import { describe, expect, it } from 'vitest'
import { highlightFastDocViewSearchMatches } from './fastDocViewSearchHighlight'

// The library's ruby markup (FastDocView WordFieldRuby.ToHtml / the .docx renderer): base first, guide in rt
const plain = 'case ruby: <ruby>base<rt style="font-size:5pt;">gd</rt></ruby> tail'
const comma = 'before <ruby><span class="ruby-guide-rest" style="font-size:10pt;">3</span>,base<rt style="font-size:10pt;">g</rt></ruby> after'
const head = (n: number) => `<mark class="search-match" data-search-part="${n}">`
const part = (n: number) => `<mark class="search-match-continued" data-search-part="${n}">`

describe('highlightFastDocViewSearchMatches', () => {
  it('numbers the marks of a block without ruby in page order', () => {
    expect(highlightFastDocViewSearchMatches('a <b>x</b> b x', 'x')).toBe(`a <b>${head(0)}x</mark></b> b ${head(1)}x</mark>`)
  })

  it('splits a match over two runs instead of letting the parser cut it at the first end tag', () => {
    expect(highlightFastDocViewSearchMatches('<span>ab</span><span>cd</span>', 'bc')).toBe(`<span>a${head(0)}b</mark></span><span>${part(0)}c</mark>d</span>`)
  })

  it('marks a match across guide and base, read guide first (Word\'s Find)', () => {
    expect(highlightFastDocViewSearchMatches(plain, 'gdbase'))
      .toBe(`case ruby: <ruby>${part(0)}base</mark><rt style="font-size:5pt;">${head(0)}gd</mark></rt></ruby> tail`)
  })

  it('does not match base then guide', () => {
    expect(highlightFastDocViewSearchMatches(plain, 'basegd')).toBe(plain)
  })

  it('carries a match out of the ruby without crossing its tags', () => {
    expect(highlightFastDocViewSearchMatches(plain, 'base tail'))
      .toBe(`case ruby: <ruby>${head(0)}base</mark><rt style="font-size:5pt;">gd</rt></ruby>${part(0)} tail</mark>`)
    expect(highlightFastDocViewSearchMatches(plain, ': gd'))
      .toBe(`case ruby${head(0)}: </mark><ruby>base<rt style="font-size:5pt;">${part(0)}gd</mark></rt></ruby> tail`)
  })

  it('reads a comma guide as written (head, comma, rest) and not its base', () => {
    expect(highlightFastDocViewSearchMatches(comma, 'g,3 after'))
      .toBe(`before <ruby><span class="ruby-guide-rest" style="font-size:10pt;">${part(0)}3</mark></span>,base<rt style="font-size:10pt;">${head(0)}g</mark></rt></ruby>${part(0)} after</mark>`)
    expect(highlightFastDocViewSearchMatches(comma, 'base')).toBe(comma)
    expect(highlightFastDocViewSearchMatches(comma, '3,base')).toBe(comma)
  })

  it('numbers first marks in reading order', () => {
    const html = highlightFastDocViewSearchMatches('<ruby>ab<rt>a</rt></ruby>', 'a')
    expect(html).toBe(`<ruby>${head(1)}a</mark>b<rt>${head(0)}a</mark></rt></ruby>`)
  })
})
