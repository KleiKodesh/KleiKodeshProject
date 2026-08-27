import { describe, it, expect } from 'vitest'
import { stripNikkudFromHtml } from './hebrewTextProcessing'

// Latin stand-ins for the Hebrew lemma/body: the rules under test key on
// punctuation and structure, never on the letters themselves.
describe('stripNikkudFromHtml — dibbur hamatchil separator', () => {
  it('turns the first dash of a line into a full stop', () => {
    expect(stripNikkudFromHtml('AAA BBB - body')).toBe('AAA BBB. body')
  })

  it('drops later dashes but keeps the word gap', () => {
    expect(stripNikkudFromHtml('AAA - body - tail')).toBe('AAA. body tail')
  })

  it('converts every dash type, since the corpus is inconsistent', () => {
    for (const dash of ['-', '\u2012', '\u2013', '\u2014', '\u2015', '\u2212']) {
      expect(stripNikkudFromHtml(`AAA ${dash} body`)).toBe('AAA. body')
    }
  })

  it('gives each line its own separator across block tags', () => {
    expect(stripNikkudFromHtml('<div>A - b</div><div>C - d</div>')).toBe('<div>A. b</div><div>C. d</div>')
    expect(stripNikkudFromHtml('A - b<br>C - d')).toBe('A. b<br>C. d')
  })

  it('keeps the separator when the lemma is wrapped in an inline tag', () => {
    expect(stripNikkudFromHtml('<b>A</b> - body - tail')).toBe('<b>A</b>. body tail')
  })

  it('absorbs a period already ending the lemma rather than doubling it', () => {
    expect(stripNikkudFromHtml('AAA. - body')).toBe('AAA. body')
  })

  it('drops a dash with no lemma in front of it instead of opening with a dot', () => {
    expect(stripNikkudFromHtml('- A - B')).toBe('A. B')
  })

  it('leaves a dash between two letters alone', () => {
    expect(stripNikkudFromHtml('AA-BB and CC - body')).toBe('AA-BB and CC. body')
  })

  it('never touches a dash inside a tag', () => {
    expect(stripNikkudFromHtml('<a href="x-y z">A</a> B - c')).toBe('<a href="x-y z">A</a> B. c')
  })
})

describe('stripNikkudFromHtml — marks and punctuation', () => {
  it('collapses a run of ! and ? to one dot', () => {
    expect(stripNikkudFromHtml('A?! B')).toBe('A. B')
    expect(stripNikkudFromHtml('A!! B')).toBe('A. B')
  })

  it('strips U+05BF RAFE, which would otherwise read as a stray dash', () => {
    expect(stripNikkudFromHtml('A\u05BFB')).toBe('AB')
  })

  it('keeps U+05BE MAQAF, which is a separator rather than a mark', () => {
    expect(stripNikkudFromHtml('A\u05BEB')).toBe('A\u05BEB')
  })

  it('strips nikkud arriving as a numeric HTML entity', () => {
    expect(stripNikkudFromHtml('A&#x5B8;B')).toBe('AB')
    expect(stripNikkudFromHtml('A&#1464;B')).toBe('AB')
  })

  it('leaves named entities alone', () => {
    expect(stripNikkudFromHtml('A&nbsp;B')).toBe('A&nbsp;B')
  })
})
