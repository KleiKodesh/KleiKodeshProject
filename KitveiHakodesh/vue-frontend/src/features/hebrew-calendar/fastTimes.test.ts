import { describe, expect, it } from 'vitest'
import type { ZmanName } from 'kleikodesh-zmanim'
import { altFastTime, FAST_EDGE_ROUNDING, fastLine, fastTime } from './fastTimes'
import type { DayTimedZmanim } from './zmanimPlaces'

const TZ = 'Asia/Jerusalem'
/** A zman at hh:mm:30 Israel summer time (UTC+3), so up/down rounding differ by a minute. */
const at = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number)
  return { time: new Date(Date.UTC(2026, 6, 1, h! - 3, m!, 30)), printed: '' }
}
const zmanim = {
  alot90: at('04:01'), alot72z: at('04:20'), sunset: at('19:47'), tzeit18Sun: at('20:23'), tzeit13p5z: at('20:01'), alot: at('04:11'), tzeit7083: at('20:15'),
} as unknown as DayTimedZmanim
const mizrach = { alot: 'alot72z', tzeit: 'tzeit13p5z' } as Partial<Record<'alot' | 'tzeit', ZmanName>>

describe('fast times follow the preset lines', () => {
  it('minor fast: preset alot (rounded down, it ends the night) to preset tzeit (rounded up)', () => {
    expect(fastTime('minorStart', zmanim, mizrach, TZ)).toBe('04:20')
    expect(fastTime('end', zmanim, mizrach, TZ)).toBe('20:02')
  })
  it('preset without alot/tzeit lines uses the default list', () => {
    expect(fastTime('minorStart', zmanim, {}, TZ)).toBe('04:01')
    expect(fastTime('end', zmanim, {}, TZ)).toBe('20:24')
  })
  it('Tisha B\'Av / Yom Kippur start at sunset, rounded down', () => {
    expect(fastLine('majorStart', {})).toBe('sunset')
    expect(fastTime('majorStart', zmanim, {}, TZ)).toBe('19:47')
  })
  it('starts round down, the end rounds up', () => expect(FAST_EDGE_ROUNDING).toEqual({ minorStart: 'down', majorStart: 'down', end: 'up' }))
  it('second line: alot 16.05 deg (rounded down) and tzeit 7.083 deg (rounded up)', () => {
    expect(altFastTime('minorStart', zmanim, {}, TZ)).toEqual({ name: 'alot', time: '04:11' })
    expect(altFastTime('end', zmanim, mizrach, TZ)).toEqual({ name: 'tzeit7083', time: '20:16' })
    expect(altFastTime('majorStart', zmanim, {}, TZ)).toBeNull()
  })
  it('no second line when the preset already uses it', () => {
    expect(altFastTime('minorStart', zmanim, { alot: 'alot' }, TZ)).toBeNull()
    expect(altFastTime('end', zmanim, { tzeit: 'tzeit7083' }, TZ)).toBeNull()
  })
  it('no time until the preset is loaded', () => expect(fastTime('end', zmanim, null, TZ)).toBeNull())
})
