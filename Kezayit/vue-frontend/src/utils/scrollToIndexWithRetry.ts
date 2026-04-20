import type { Virtualizer } from '@tanstack/vue-virtual'

/**
 * Scrolls a virtualizer to a target index. If the item is already fully visible
 * (accounting for topReserved), does nothing. Otherwise scrolls it to the top
 * with a topReserved gap so it isn't hidden behind a floating element (e.g. search bar).
 * Retries if the item hasn't rendered yet.
 */
export function scrollToIndexWithRetry(
  virtualizer: Virtualizer<Element, Element>,
  scrollerEl: HTMLElement,
  index: number,
  topReserved = 0,
  maxRetries = 3,
): void {
  let attempts = 0

  function attempt() {
    requestAnimationFrame(() => {
      const m = virtualizer.measurementsCache.find((c) => c.index === index)
      if (!m) {
        virtualizer.scrollToIndex(index, { align: 'start' })
        if (++attempts < maxRetries) attempt()
        return
      }

      const viewTop = scrollerEl.scrollTop
      const viewBottom = viewTop + scrollerEl.clientHeight
      const effectiveTop = viewTop + topReserved
      const isVisible = m.start >= effectiveTop && m.end <= viewBottom

      if (isVisible) return

      if (!isVisible && ++attempts < maxRetries) {
        // Scroll so the item sits just below the reserved zone
        scrollerEl.scrollTop = m.start - topReserved - 8
        attempt()
      } else {
        scrollerEl.scrollTop = m.start - topReserved - 8
      }
    })
  }

  // Kick off with an initial scrollToIndex to get the item rendered
  virtualizer.scrollToIndex(index, { align: 'start' })
  attempt()
}
