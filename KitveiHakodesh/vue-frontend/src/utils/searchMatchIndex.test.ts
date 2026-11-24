import { describe, it, expect } from 'vitest'
import { SearchMatchIndex } from './searchMatchIndex'

describe('SearchMatchIndex', () => {
  // Rows 3 (x2), 10 (x1), 40 (x3), given out of order: matches 0-1 on row 3, 2 on row 10, 3-5 on row 40.
  const index = SearchMatchIndex.fromRowCounts([40, 3, 10], [3, 2, 1])

  it('counts every occurrence and keeps rows ascending', () => {
    expect(index.total).toBe(6)
    expect(Array.from(index.rows)).toEqual([3, 10, 40])
  })

  it('finds match k as its row and occurrence', () => {
    expect(index.at(0)).toEqual({ row: 3, occurrence: 0 })
    expect(index.at(1)).toEqual({ row: 3, occurrence: 1 })
    expect(index.at(2)).toEqual({ row: 10, occurrence: 0 })
    expect(index.at(5)).toEqual({ row: 40, occurrence: 2 })
    expect(index.at(6)).toBeNull()
    expect(index.at(-1)).toBeNull()
  })

  it('maps (row, occurrence) back to its match number', () => {
    expect(index.indexOf(40, 1)).toBe(4)
    expect(index.indexOf(40, 3)).toBe(-1)
    expect(index.indexOf(5, 0)).toBe(-1)
  })

  it('answers the nearest match at, after and before a row', () => {
    expect(index.firstAtOrAfter(0)).toBe(0)
    expect(index.firstAtOrAfter(10)).toBe(2)
    expect(index.firstAtOrAfter(11)).toBe(3)
    expect(index.firstAtOrAfter(41)).toBe(6)   // none: total
    expect(index.lastBefore(3)).toBe(-1)
    expect(index.lastBefore(11)).toBe(2)
    expect(index.lastBefore(1000)).toBe(5)
  })

  it('knows which rows match, and how often', () => {
    expect(index.has(10)).toBe(true)
    expect(index.has(11)).toBe(false)
    expect(index.countOf(40)).toBe(3)
    expect(index.countOf(11)).toBe(0)
  })

  it('leaves out rows with no occurrence; a row given twice keeps its last count', () => {
    const odd = SearchMatchIndex.fromRowCounts([5, 6, 5], [1, 0, 4])
    expect(Array.from(odd.rows)).toEqual([5])
    expect(odd.total).toBe(4)
  })

  it('merges a later part of a search, the newer count winning', () => {
    const merged = index.mergedWith(SearchMatchIndex.fromRowCounts([1, 10], [1, 5]))
    expect(Array.from(merged.rows)).toEqual([1, 3, 10, 40])
    expect(merged.countOf(10)).toBe(5)
    expect(merged.total).toBe(1 + 2 + 5 + 3)
  })

  it('is empty when nothing matches', () => {
    const empty = SearchMatchIndex.fromRowCounts([], [])
    expect(empty).toBe(SearchMatchIndex.EMPTY)
    expect(empty.total).toBe(0)
    expect(empty.at(0)).toBeNull()
    expect(empty.firstAtOrAfter(0)).toBe(0)
  })
})
