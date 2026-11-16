import { describe, expect, it } from 'vitest'
import { withExpansions, withVocalization } from './readAloudDicta'
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
