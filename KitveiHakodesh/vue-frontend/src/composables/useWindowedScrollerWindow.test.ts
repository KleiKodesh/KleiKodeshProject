import { describe, expect, it } from 'vitest'
import { useWindowedScrollerWindow } from './useWindowedScrollerWindow'

/**
 * A list whose count can change while a landing waits for its rows — a document whose length is
 * an estimate until its rows are read. Rows are 20 px tall and laid out from the window start.
 * Only the landing's own fetches succeed (`landingFetches`); the extension that follows a landing
 * finds its rows unavailable, so the test sees exactly what the landing mounted (this fake has no
 * anchor-holding update hooks to keep an extension's layout honest).
 */
function makeList(initialCount: number, countAfterFetch: number, landingFetches: number) {
  let count = initialCount
  let fetches = 0
  const rowHeight = 20
  const viewport = {
    scrollTop: 0,
    scrollHeight: 0,
    clientHeight: 400,
    getBoundingClientRect: () => ({ top: 0 }),
  }
  const window = useWindowedScrollerWindow({
    count: () => count,
    viewport: () => viewport as unknown as HTMLElement,
    rowElement: (index) =>
      index >= window.windowStart.value && index < window.windowEnd.value && index < count
        ? ({
            offsetTop: (index - window.windowStart.value) * rowHeight,
            offsetHeight: rowHeight,
            getBoundingClientRect: () => ({ bottom: (index - window.windowStart.value + 1) * rowHeight }),
          } as unknown as HTMLElement)
        : null,
    ensureRows: async () => {
      if (++fetches > landingFetches) throw new Error('rows unavailable')
      // Reading the rows tells the real length.
      count = countAfterFetch
    },
  })
  return { window, getCount: () => count }
}

describe('useWindowedScrollerWindow landAt', () => {
  it('re-lands on the new last row when fetching the rows shrinks the count past the target', async () => {
    const { window, getCount } = makeList(1_000, 800, 2)
    await window.landAt(999)            // the estimated end
    expect(getCount()).toBe(800)
    expect(window.windowEnd.value).toBe(800)
    expect(window.windowStart.value).toBeLessThan(800)
    expect(window.isRowInWindow(799)).toBe(true)
  })

  it('never mounts rows past a count that shrank under a landing that still fits', async () => {
    const { window } = makeList(1_000, 790, 1)
    await window.landAt(770)            // still inside the new count, but its rows-below reach past it
    expect(window.windowEnd.value).toBeLessThanOrEqual(790)
    expect(window.isRowInWindow(770)).toBe(true)
  })

  it('lands normally when the count does not change', async () => {
    const { window } = makeList(1_000, 1_000, 1)
    await window.landAt(500)
    expect(window.isRowInWindow(500)).toBe(true)
    expect(window.windowEnd.value).toBeLessThanOrEqual(1_000)
  })
})
