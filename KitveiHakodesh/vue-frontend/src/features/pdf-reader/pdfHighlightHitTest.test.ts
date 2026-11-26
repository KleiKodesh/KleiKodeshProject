import { describe, expect, it } from 'vitest'
import { boxesOverlap, highlightBoxes, highlightTouches } from './pdfHighlightHitTest'

describe('pdfHighlightHitTest', () => {
  it('builds one box per quad', () => {
    // Two lines: quads as PDF.js stores them (top-left, top-right, bottom-left, bottom-right).
    const q = [10, 100, 50, 100, 10, 90, 50, 90, 10, 80, 30, 80, 10, 70, 30, 70]
    expect(highlightBoxes(q, null)).toEqual([[10, 90, 50, 100], [10, 70, 30, 80]])
  })
  it('falls back to the rect', () => {
    expect(highlightBoxes(null, [50, 20, 10, 5])).toEqual([[10, 5, 50, 20]])
    expect(highlightBoxes(undefined, undefined)).toEqual([])
  })
  it('point hit only inside a line box, not in the gap', () => {
    const boxes = highlightBoxes([10, 100, 50, 100, 10, 90, 50, 90, 10, 80, 30, 80, 10, 70, 30, 70], null)
    expect(highlightTouches(boxes, [[20, 95, 20, 95]])).toBe(true)
    expect(highlightTouches(boxes, [[40, 75, 40, 75]])).toBe(false)
    expect(highlightTouches(boxes, [[20, 85, 20, 85]])).toBe(false)
  })
  it('partial overlap of a selection counts', () => {
    expect(boxesOverlap([0, 0, 10, 10], [9, 9, 30, 30])).toBe(true)
    expect(boxesOverlap([0, 0, 10, 10], [11, 0, 30, 10])).toBe(false)
  })
})
