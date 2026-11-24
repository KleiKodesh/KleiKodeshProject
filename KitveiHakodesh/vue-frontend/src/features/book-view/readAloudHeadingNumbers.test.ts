// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { withSpelledHeadingNumbers } from './readAloudHeadingNumbers'
import { readAloudTextOfLine, readAloudTextOfPlain } from './readAloudSkip'

const spoken = (s: string, heading = true) => withSpelledHeadingNumbers(readAloudTextOfPlain(s, null), heading).spoken

describe('withSpelledHeadingNumbers', () => {
  it('spells the number after a known label', () => {
    expect(spoken('פרק ג')).toBe('פרק גימל')
    expect(spoken('סימן י"ב')).toBe('סימן יוד בית')
    expect(spoken('פרק ג פסוק א')).toBe('פרק גימל פסוק אלף')
    expect(spoken('סימן, טו')).toBe('סימן, טית וו')
    expect(spoken("דף ב'")).toBe('דף בית')
  })
  it('leaves words and other lines alone', () => {
    expect(spoken('פרק שירה')).toBe('פרק שירה')
    expect(spoken('הלכות שבת פרק ג')).toBe('הלכות שבת פרק ג')
    expect(spoken('ומשה היה רועה', false)).toBe('ומשה היה רועה')
    expect(spoken('פרק ג', false)).toBe('פרק גימל')
  })
  it('reads the heading tag and keeps the map to the shown text', () => {
    const t = readAloudTextOfLine('<h2>פרק ג</h2>', null)
    expect(t.heading).toBe(true)
    const out = withSpelledHeadingNumbers(t, true)
    expect(out.spoken).toBe('פרק גימל')
    expect(out.toFull[out.spoken.length - 1]).toBe(4)
  })
})
