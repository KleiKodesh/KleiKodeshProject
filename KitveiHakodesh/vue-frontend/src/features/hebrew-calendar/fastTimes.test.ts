import { describe, expect, it } from 'vitest'
import type { ZmanName } from 'kleikodesh-zmanim'
import type { CalendarZmanim } from './calendarTypes'
import { altFastTime, fastLine, fastTime } from './fastTimes'

const zmanim = { alot90: '04:01', alot72z: '04:20', sunset: '19:47', tzeit18Sun: '20:23', tzeit13p5z: '20:01', alot: '04:11', tzeit7083: '20:15' } as unknown as CalendarZmanim
const mizrach = { alot: 'alot72z', tzeit: 'tzeit13p5z' } as Partial<Record<'alot' | 'tzeit', ZmanName>>

describe('fast times follow the preset lines', () => {
  it('minor fast: preset alot to preset tzeit', () => {
    expect(fastTime('minorStart', zmanim, mizrach)).toBe('04:20')
    expect(fastTime('end', zmanim, mizrach)).toBe('20:01')
  })
  it('preset without alot/tzeit lines uses the default list', () => {
    expect(fastTime('minorStart', zmanim, {})).toBe('04:01')
    expect(fastTime('end', zmanim, {})).toBe('20:23')
  })
  it('Tisha B\'Av / Yom Kippur start at sunset', () => expect(fastLine('majorStart', {})).toBe('sunset'))
  it('second line: alot 16.1 deg and tzeit 7.083 deg', () => {
    expect(altFastTime('minorStart', zmanim, {})).toEqual({ name: 'alot', time: '04:11' })
    expect(altFastTime('end', zmanim, mizrach)).toEqual({ name: 'tzeit7083', time: '20:15' })
    expect(altFastTime('majorStart', zmanim, {})).toBeNull()
  })
  it('no second line when the preset already uses it', () => {
    expect(altFastTime('minorStart', zmanim, { alot: 'alot' })).toBeNull()
    expect(altFastTime('end', zmanim, { tzeit: 'tzeit7083' })).toBeNull()
  })
  it('no time until the preset is loaded', () => expect(fastTime('end', zmanim, null)).toBeNull())
})
