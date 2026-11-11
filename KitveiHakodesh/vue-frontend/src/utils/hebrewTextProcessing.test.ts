import { describe, it, expect } from 'vitest'
import { stripNikkudFromHtml, CLEAN_TEXT } from './hebrewTextProcessing'

// Latin stand-ins for the Hebrew lemma/body: the rules under test key on
// punctuation and structure, never on the letters themselves.
describe('stripNikkudFromHtml — dibbur hamatchil separator', () => {
  // The dash rule alone: the default flags also remove punctuation dashes.
  const DH = CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH
  const PLAIN = DH | CLEAN_TEXT.PLAIN_DIBBUR_HAMATCHIL

  it('turns the dash after a bold heading into a full stop', () => {
    expect(stripNikkudFromHtml('<b>AAA BBB</b> - body', DH)).toBe('<b>AAA BBB</b>. body')
  })

  it('leaves a mid-sentence pause dash alone (vocalized Talmud)', () => {
    expect(stripNikkudFromHtml('AAA BBB, CCC DDD – EEE.', DH)).toBe('AAA BBB, CCC DDD – EEE.')
  })

  it('judges only the first dash; later ones are punctuation', () => {
    expect(stripNikkudFromHtml('<b>AAA</b> - body - tail', DH)).toBe('<b>AAA</b>. body - tail')
  })

  it('converts every dash type, since the corpus is inconsistent', () => {
    for (const dash of ['-', '‒', '–', '—', '―', '−']) {
      expect(stripNikkudFromHtml(`<b>AAA</b> ${dash} body`, DH)).toBe('<b>AAA</b>. body')
    }
  })

  it('ignores empty tags inside the heading (Vilna Rashi)', () => {
    expect(stripNikkudFromHtml('<b><b></b><i></i>AAA BBB</b> - body', DH)).toBe('<b><b></b><i></i>AAA BBB</b>. body')
  })

  it('accepts numbering before the bold heading', () => {
    expect(stripNikkudFromHtml('(12) <b>AAA</b> - body', DH)).toBe('(12) <b>AAA</b>. body')
  })

  it('keeps a heading period or colon instead of adding a second one', () => {
    expect(stripNikkudFromHtml('<b>AAA BBB.</b> — body', DH)).toBe('<b>AAA BBB.</b> body')
    expect(stripNikkudFromHtml('<b>AAA</b>: - body', DH)).toBe('<b>AAA</b>: body')
  })

  it('rejects text after the bold heading or several bold spans', () => {
    expect(stripNikkudFromHtml('<b>AAA.</b> body more - tail', DH)).toBe('<b>AAA.</b> body more - tail')
    expect(stripNikkudFromHtml('<b>A</b> b <b>C</b> - d', DH)).toBe('<b>A</b> b <b>C</b> - d')
  })

  it('needs the commentary flag for a plain heading', () => {
    expect(stripNikkudFromHtml('AAA BBB - body', DH)).toBe('AAA BBB - body')
    expect(stripNikkudFromHtml('AAA BBB - body', PLAIN)).toBe('AAA BBB. body')
    expect(stripNikkudFromHtml('AAA. - body', PLAIN)).toBe('AAA. body')
  })

  it('rejects a long or punctuated plain heading even in commentary', () => {
    expect(stripNikkudFromHtml('A B C D E F G - body', PLAIN)).toBe('A B C D E F G - body')
    expect(stripNikkudFromHtml('AAA, BBB - body', PLAIN)).toBe('AAA, BBB - body')
  })

  it('gives each line its own judgement across block tags', () => {
    expect(stripNikkudFromHtml('<div><b>A</b> - b</div><div><b>C</b> - d</div>', DH)).toBe('<div><b>A</b>. b</div><div><b>C</b>. d</div>')
    expect(stripNikkudFromHtml('<b>A</b> - b<br><b>C</b> - d', DH)).toBe('<b>A</b>. b<br><b>C</b>. d')
  })

  it('keeps a dash with nothing before it and judges the next one', () => {
    expect(stripNikkudFromHtml('- <b>A</b> - B', DH)).toBe('- <b>A</b>. B')
  })

  it('accepts a bold span still open at the dash only in commentary', () => {
    expect(stripNikkudFromHtml('<b>AAA - body</b>', DH)).toBe('<b>AAA - body</b>')
    expect(stripNikkudFromHtml('<b>AAA - body</b>', PLAIN)).toBe('<b>AAA. body</b>')
  })

  it('leaves a dash between two letters alone', () => {
    expect(stripNikkudFromHtml('<b>AA-BB</b> - body', DH)).toBe('<b>AA-BB</b>. body')
  })

  it('never touches a dash inside a tag', () => {
    expect(stripNikkudFromHtml('<a href="x - y z"><b>A</b></a> - c', DH)).toBe('<a href="x - y z"><b>A</b></a>. c')
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
