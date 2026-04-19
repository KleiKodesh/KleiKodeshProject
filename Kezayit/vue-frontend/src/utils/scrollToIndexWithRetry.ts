import type { Virtualizer } from '@tanstack/vue-virtual'

/**
 * Scrolls a virtualizer to a target index using 'auto' alignment (only scrolls
 * if the item is not already visible). Retries if the item hasn't rendered yet.
 */
export function scrollToIndexWithRetry(
  virtualizer: Virtualizer<Element, Element>,
  scrollerEl: HTMLElement,
  index: number,
  _offset = 0,
  maxRetries = 3,
): void {
  let attempts = 0

  function attempt() {
    virtualizer.scrollToIndex(index, { align: 'auto' })

    requestAnimationFrame(() => {
      const m = virtualizer.measurementsCache.find((c) => c.index === index)
      if (!m) {
        if (++attempts < maxRetries) attempt()
        return
      }

      const viewTop = scrollerEl.scrollTop
      const viewBottom = viewTop + scrollerEl.clientHeight
      const isVisible = m.start >= viewTop && m.end <= viewBottom

      if (!isVisible && ++attempts < maxRetries) {
        attempt()
      } else {
        // line is in view — scroll the actual mark into view in case the line is long
        requestAnimationFrame(() => {
          const mark = scrollerEl.querySelector('mark.search-match.current') as HTMLElement | null
          if (!mark) return
          const markRect = mark.getBoundingClientRect()
          const scrollerRect = scrollerEl.getBoundingClientRect()
          const scrollPaddingTop = parseInt(scrollerEl.style.scrollPaddingTop ?? '0', 10) || 0
          const obscured = markRect.top < scrollerRect.top + scrollPaddingTop
          if (obscured) {
            scrollerEl.scrollTop -= scrollerRect.top + scrollPaddingTop - markRect.top + 8
          }
        })
      }
    })
  }

  attempt()
}
