import type { Virtualizer } from '@tanstack/vue-virtual'

/**
 * Scrolls a virtualizer to a target index, retries if the item isn't visible
 * (estimated sizes), then scrolls the actual `.search-match.current` mark into
 * view so the highlighted match itself is always visible.
 */
export function scrollToIndexWithRetry(
  virtualizer: Virtualizer<Element, Element>,
  scrollerEl: HTMLElement,
  index: number,
  offset = 0,
  maxRetries = 3,
): void {
  let attempts = 0

  function scrollCurrentMark() {
    const mark = scrollerEl.querySelector('mark.search-match.current') as HTMLElement | null
    if (mark) {
      mark.scrollIntoView({ block: 'nearest' })
    }
  }

  function attempt() {
    virtualizer.scrollToIndex(index, { align: 'start' })

    requestAnimationFrame(() => {
      const m = virtualizer.measurementsCache.find((c) => c.index === index)
      if (!m) {
        if (++attempts < maxRetries) attempt()
        else scrollCurrentMark()
        return
      }

      const target = m.start + offset
      const viewTop = scrollerEl.scrollTop
      const viewBottom = viewTop + scrollerEl.clientHeight
      const isVisible = target >= viewTop && target < viewBottom

      if (!isVisible && ++attempts < maxRetries) {
        attempt()
      } else {
        // line is in view — now scroll the actual mark into view
        requestAnimationFrame(scrollCurrentMark)
      }
    })
  }

  attempt()
}
