import { describe, expect, it } from 'vitest'
import { fitFactor, parsePolyline, pointAt } from './fastDocViewFontwork'

describe('fastDocViewFontwork', () => {
  it('reads the outline the library writes', () => {
    expect(parsePolyline('M0 18L72 18L144 18')).toEqual([{ x: 0, y: 18 }, { x: 72, y: 18 }, { x: 144, y: 18 }])
  })

  it('finds the point at a fraction of the length', () => {
    const outline = parsePolyline('M0 0L100 0L100 100')
    expect(pointAt(outline, 0.25)).toEqual({ x: 50, y: 0 })
    expect(pointAt(outline, 0.75)).toEqual({ x: 100, y: 50 })
    expect(pointAt(outline, 2)).toEqual({ x: 100, y: 100 })
  })

  it('takes the smallest length / width ratio over the lines, as LibreOffice CalculateHorizontalScalingFactor does', () => {
    expect(fitFactor([{ length: 200, width: 100 }, { length: 200, width: 400 }])).toBe(0.5)
    expect(fitFactor([{ length: 300, width: 100 }])).toBe(3)
    expect(fitFactor([{ length: 300, width: 0 }])).toBe(1)
  })
})
