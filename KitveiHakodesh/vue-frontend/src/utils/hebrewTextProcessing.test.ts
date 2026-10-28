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

// ── stripHtmlForSearch: the bulk-run version must equal the char-by-char one ──────
import { stripHtmlForSearch as stripFast, MAQAF as MAQAF_CH } from './hebrewTextProcessing'

function stripSlow(html: string): string {
  const QUOTE_ENTITIES: Record<string, string> = { '&quot;': '"', '&#34;': '"', '&#x22;': '"', '&apos;': "'", '&#39;': "'", '&#x27;': "'" }
  const fold = (t: string) => t.replace(/["״“”„]/g, '"').replace(/['׳‘’‚`]/g, "'")
  const ignored = (ch: string) => { const c = ch.charCodeAt(0); return c >= 0x0591 && c <= 0x05C7 && c !== 0x05BE }
  let result = ''; let inTag = false; let i = 0
  while (i < html.length) {
    const ch = html[i]!
    if (ch === '<') { inTag = true; i++; continue }
    if (ch === '>') { inTag = false; i++; continue }
    if (inTag) { i++; continue }
    if (ch === '&') {
      let entityEnd = -1
      for (let j = i + 1; j < html.length && j <= i + 12; j++) { const c = html[j]!; if (c === ';') { entityEnd = j; break } if (c === ' ' || c === '\t' || c === '\n' || c === '<') break }
      if (entityEnd !== -1) { result += QUOTE_ENTITIES[html.slice(i, entityEnd + 1).toLowerCase()] ?? '\x00'; i = entityEnd + 1; continue }
      result += ch; i++; continue
    }
    if (ch === MAQAF_CH) result += ' '
    else if (!ignored(ch)) result += fold(ch)
    i++
  }
  return result
}

describe('stripHtmlForSearch parity with the char-by-char version', () => {
  const samples = [
    '',
    'plain',
    '<b>בְּרֵאשִׁית</b> בָּרָא &quot;אלקים&quot; &amp; את <br/>השמים',
    'a &#39;b&#x27; c &bogus d & e &amp',
    'שו״ע או"ח <span class="x">סי׳ א</span>',
    'מלך־ישראל <i>x</i>',
    'unterminated <tag',
    'stray > sign > here',
    '<p>&nbsp;</p><p>&#1234;</p>',
    'tab&a\tb; nl&a\nb; lt&a<b;',
  ]
  it.each(samples)('%s', (html) => { expect(stripFast(html)).toBe(stripSlow(html)) })
  it('random', () => {
    const alphabet = 'אבגדהוזחטיךכלםמןנסעףפץצקרשת ְִּ־"\'״׳<>&;#xqbnpr/ \t\n'
    let seed = 7
    const rnd = () => (seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff
    for (let n = 0; n < 2000; n++) {
      let html = ''
      const len = Math.floor(rnd() * 40)
      for (let i = 0; i < len; i++) html += alphabet[Math.floor(rnd() * alphabet.length)]
      expect(stripFast(html)).toBe(stripSlow(html))
    }
  })
})
