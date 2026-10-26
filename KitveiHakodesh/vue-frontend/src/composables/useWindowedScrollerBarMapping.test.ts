import { describe, expect, it } from 'vitest'
import { ref } from 'vue'
import { useWindowedScrollerBarMapping } from './useWindowedScrollerBarMapping'

/** A list of `count` rows, `rowHeight` tall, with the window [start, end) mounted. */
function makeList(count: number, rowHeight: number, start: number, end: number) {
  const viewport = { scrollTop: 0, scrollHeight: (end - start) * rowHeight, clientHeight: 400 }
  const windowStart = ref(start)
  const windowEnd = ref(end)
  const tiers = ref<number[][]>([[0, Math.floor(count / 2)]])
  const mapping = useWindowedScrollerBarMapping({
    count: () => count,
    viewport: () => viewport as unknown as HTMLElement,
    windowStart,
    windowEnd,
    rowElement: (index) =>
      index >= windowStart.value && index < windowEnd.value
        ? ({ offsetTop: (index - windowStart.value) * rowHeight, offsetHeight: rowHeight } as HTMLElement)
        : null,
    findRowIndexAt: (top) => windowStart.value + Math.floor(top / rowHeight),
    landAt: async () => {},
    landmarkTiers: () => tiers.value,
    rowWeights: () => null,
  })
  return { viewport, windowStart, windowEnd, tiers, mapping }
}

describe('useWindowedScrollerBarMapping.syncFromViewport', () => {
  it('keeps the total and the landmark tiers (same array) across scroll ticks within one window', () => {
    const { viewport, mapping } = makeList(1000, 20, 100, 140)
    mapping.syncFromViewport()
    const total = mapping.totalHeight.value
    const tiers = mapping.landmarkTiers.value
    expect(total).toBe(1000 * 20)
    expect(tiers).toEqual([[0, 500 * 20]])
    for (let tick = 1; tick <= 50; tick++) {
      viewport.scrollTop = tick * 7
      mapping.syncFromViewport()
      expect(mapping.totalHeight.value).toBe(total)
      expect(mapping.landmarkTiers.value).toBe(tiers)
      expect(mapping.position.value).toBe(100 * 20 + tick * 7)
    }
  })

  it('recomputes when the window moves, the rows resize or the landmarks change', () => {
    const { viewport, windowStart, windowEnd, tiers, mapping } = makeList(1000, 20, 100, 140)
    mapping.syncFromViewport()
    const first = mapping.landmarkTiers.value

    windowStart.value = 120
    windowEnd.value = 160
    mapping.syncFromViewport()
    const afterWindowMove = mapping.landmarkTiers.value
    expect(afterWindowMove).not.toBe(first)

    viewport.scrollHeight = 40 * 20 + 100
    mapping.syncFromViewport()
    const afterResize = mapping.landmarkTiers.value
    expect(afterResize).not.toBe(afterWindowMove)
    expect(mapping.totalHeight.value).toBeGreaterThan(1000 * 20)

    tiers.value = [[0, 250, 750]]
    mapping.syncFromViewport()
    expect(mapping.landmarkTiers.value).not.toBe(afterResize)
    expect(mapping.landmarkTiers.value[0]).toHaveLength(3)
  })

  it('treats a fresh empty tier list as unchanged', () => {
    const { viewport, tiers, mapping } = makeList(1000, 20, 100, 140)
    tiers.value = []
    mapping.syncFromViewport()
    const empty = mapping.landmarkTiers.value
    tiers.value = []
    viewport.scrollTop = 5
    mapping.syncFromViewport()
    expect(mapping.landmarkTiers.value).toBe(empty)
  })
})
