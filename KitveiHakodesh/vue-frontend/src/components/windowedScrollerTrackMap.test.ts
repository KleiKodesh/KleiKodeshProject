import { describe, expect, it } from 'vitest'
import { buildTrackMap, positionForTrackOffset, trackOffsetForPosition } from './windowedScrollerTrackMap'

describe('buildTrackMap', () => {
  it('is plain proportion without landmarks', () => {
    const map = buildTrackMap([], 1000, 500, 12)
    expect(trackOffsetForPosition(map, 250)).toBe(125)
    expect(positionForTrackOffset(map, 125)).toBe(250)
  })

  it('lifts a thin section to the minimum span and shares the rest in proportion', () => {
    // A 10px section in a 1000px list on a 500px track: proportion would give it 5px.
    const map = buildTrackMap([[10]], 1000, 500, 12)
    expect(trackOffsetForPosition(map, 10)).toBe(12)
    expect(trackOffsetForPosition(map, 1000)).toBe(500)
    expect(positionForTrackOffset(map, 12)).toBe(10)
  })

  it('adds a finer tier only while every span still gets its minimum', () => {
    const coarse = [500]
    const fine = Array.from({ length: 99 }, (_, index) => (index + 1) * 10)
    // 100 spans at 12px need 1200px of travel; only 500px available → coarse tier only.
    const map = buildTrackMap([coarse, fine], 1000, 500, 12)
    expect(map.positions).toEqual([0, 500, 1000])
    // With a track that fits them all, the fine tier is used.
    const roomy = buildTrackMap([coarse, fine], 1000, 2000, 12)
    expect(roomy.positions.length).toBe(101)
  })

  it('ends exactly at the travel and stays monotonic', () => {
    const map = buildTrackMap([[1, 2, 3, 900]], 1000, 300, 10)
    expect(map.offsets[0]).toBe(0)
    expect(map.offsets[map.offsets.length - 1]).toBe(300)
    for (let index = 1; index < map.offsets.length; index++) {
      expect(map.offsets[index]!).toBeGreaterThanOrEqual(map.offsets[index - 1]!)
      expect(map.offsets[index]! - map.offsets[index - 1]!).toBeGreaterThanOrEqual(10 - 1e-9)
    }
  })

  it('ignores landmarks outside the range and duplicates', () => {
    const map = buildTrackMap([[-5, 0, 500, 500, 1000, 2000]], 1000, 500, 12)
    expect(map.positions).toEqual([0, 500, 1000])
  })
})
