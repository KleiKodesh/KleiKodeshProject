import { describe, expect, it } from 'vitest'
import { chooseExpansion, withExpansions, withVocalization } from './readAloudDicta'
import type { ReadAloudText } from './readAloudSkip'

const plain = (s: string): ReadAloudText => ({ full: s, spoken: s, toFull: [...s].map((_, i) => i).concat(s.length) })

describe('withExpansions', () => {
  it('speaks the abbreviation in full, mapped onto the abbreviation letters', () => {
    const t = plain('אמר רש"י כאן')
    const out = withExpansions(t, [{ start: 4, end: 8, text: 'רבי שלמה יצחקי' }])
    expect(out.spoken).toBe('אמר רבי שלמה יצחקי כאן')
    expect(out.toFull.length).toBe(out.spoken.length + 1)
    expect(Math.max(...out.toFull.slice(4, 4 + 'רבי שלמה יצחקי'.length))).toBe(7)
    expect(out.toFull[out.spoken.length]).toBe(t.full.length)
  })
})

describe('withVocalization', () => {
  it('keeps points on their letters and refuses different letters', () => {
    const t = plain('שלום')
    const out = withVocalization(t, 'שָׁלוֹם')!
    expect(out.spoken).toBe('שָׁלוֹם')
    expect(out.toFull[out.spoken.length]).toBe(4)
    expect(withVocalization(t, 'שלמה')).toBeNull()
  })
})

describe('with skipped text (parentheses, source links)', () => {
  it('keeps every spoken letter on its place in the full text', () => {
    // full: "אמר (מראה) רש"י" - the parenthesis is skipped, so spoken has a gap in toFull.
    const full = 'אמר (מראה) רש"י'
    const spoken = 'אמר  רש"י'
    const toFull: number[] = []
    for (let i = 0; i < full.length; i++) if (i < 4 || i > 9) toFull.push(i)
    toFull.push(full.length)
    const text: ReadAloudText = { full, spoken, toFull }
    const start = spoken.indexOf('רש')
    const out = withExpansions(text, [{ start, end: start + 4, text: 'רבי שלמה יצחקי' }])
    expect(out.spoken).toBe('אמר  רבי שלמה יצחקי')
    // The expansion lands on the abbreviation in the full text, after the skipped part.
    expect(out.toFull[start]).toBe(full.indexOf('רש'))
    expect(Math.max(...out.toFull.slice(0, out.spoken.length))).toBe(full.length - 1)
    // Nothing of the skipped parenthesis is mapped.
    expect(out.toFull.slice(0, out.spoken.length).some((f) => f >= 4 && f <= 9)).toBe(false)
  })
})

describe('chooseExpansion (Dicta answers as seen live)', () => {
  it('opens what the user expects', () => {
    expect(chooseExpansion("וגו'", ["וגו'", 'וגובה', 'וגומר'])).toBe('וגומר')
    expect(chooseExpansion('רשב"י', ['רשב"י', 'רבי שמעון בר יוחאי'])).toBe('רבי שמעון בר יוחאי')
    expect(chooseExpansion('הקב"ה', ['הקדוש ברוך הוא', 'הקב"ה'])).toBe('הקדוש ברוך הוא')
    expect(chooseExpansion("וכו'", ["וכו'", 'וכור', 'וכו'])).toBe('וכולי')
    expect(chooseExpansion('ז"ל', ['זכרונו לברכה', 'ז"ל'])).toBe('זכרונו לברכה')
  })
  it('keeps numbers and unknowns as written', () => {
    expect(chooseExpansion('ל"ב', ['ל"ב', 'לא בטל'])).toBeNull()
    expect(chooseExpansion('קע"ב', ['קע"ב', 'מאה שבעים ושניים'])).toBeNull()
    expect(chooseExpansion("ג'", ["ג'", 'שלשה'])).toBeNull()
    expect(chooseExpansion('הרמב"ם', ['הרמב"ם'])).toBeNull()
  })
})
