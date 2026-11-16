import { describe, expect, it } from 'vitest'
import { contextAround, mergeDictaRanking, normalizeForMatch, optionsAt } from './abbrevDicta'

describe('mergeDictaRanking', () => {
  it('orders matching senses by Dicta, adds unmatched options, keeps the rest after', () => {
    const senses = ['בית רבינו', 'בראשית רבה', 'ברוך רוצה']
    const options = ['ב"ר', 'בְּרֵאשִׁית רַבָּה', 'בר רב', 'בית רבינו']
    expect(mergeDictaRanking('ב"ר', senses, options)).toEqual([
      { text: 'בראשית רבה', fromDicta: false },
      { text: 'בר רב', fromDicta: true },
      { text: 'בית רבינו', fromDicta: false },
      { text: 'ברוך רוצה', fromDicta: false },
    ])
  })
  it('matches comma/semicolon parts and final letters; ignores headword prefix', () => {
    const senses = ['רשב"י — רבי שמעון בר יוחאי; רשב"י', 'אחר']
    const r = mergeDictaRanking('רשב"י', senses, ['רַבִּי שִׁמְעוֹן בַּר יוֹחָאי'])
    expect(r.map((x) => x.text)).toEqual(senses)
    expect(r.every((x) => !x.fromDicta)).toBe(true)
    expect(normalizeForMatch('שָׁלוֹם,')).toBe(normalizeForMatch('שלומ'))
  })
  it('no options keeps dictionary order', () => {
    expect(mergeDictaRanking('ב"ר', ['א', 'ב'], []).map((x) => x.text)).toEqual(['א', 'ב'])
  })
})

describe('context helpers', () => {
  it('trims long lines around the word', () => {
    const line = 'א '.repeat(400) + 'ב"ר' + ' ג'.repeat(400)
    const start = 800
    const c = contextAround(line, start, start + 3)
    expect(c.text.length).toBeLessThanOrEqual(400)
    expect(c.text.slice(c.start, c.end)).toBe('ב"ר')
  })
  it('finds the covering token', () => {
    const tokens = [{ str: 'אמר ' }, { str: 'ב"ר', abbreviation: { options: ['x'] } }]
    expect(optionsAt(tokens, 4, 7)).toEqual(['x'])
    expect(optionsAt(tokens, 0, 3)).toBeNull()
  })
})
