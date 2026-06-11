import type { Virtualizer } from '@tanstack/vue-virtual'

/**
 * Scrolls a virtualizer to a target index so the item appears just below any
 * fixed overlay (topReserved px from the top of the viewport).
 *
 * Strategy:
 *   - If the item is already in the virtualizer's measurements cache, set
 *     scrollTop directly — do NOT call scrollToIndex, which would fight us.
 *   - If the item is not yet measured (far outside the rendered range), call
 *     scrollToIndex to bring it into range, then retry once it's measured.
 *
 * onScrolled: optional callback invoked after the scroll is applied (or confirmed
 * already correct), so callers can do follow-up work (e.g. scrolling a <mark>
 * into view within a tall line).
 */
export function scrollToIndexWithRetry(
  virtualizer: Virtualizer<Element, Element>,
  scrollerEl: HTMLElement,
  index: number,
  topReserved = 0,
  maxRetries = 5,
  onScrolled?: () => void,
  isCancelled?: () => boolean,
): void {
  let attempts = 0
  const gap = topReserved + 8

  function attempt() {
    requestAnimationFrame(() => {
      if (isCancelled?.()) {
        console.log(`[scrollToIndexWithRetry] cancelled at attempt=${attempts} index=${index}`)
        return
      }
      const m = virtualizer.measurementsCache.find((c) => c.index === index)
      console.log(`[scrollToIndexWithRetry] attempt=${attempts} index=${index} measured=${!!m} cacheSize=${virtualizer.measurementsCache.length}`, m ? `start=${m.start}` : '')

      if (!m) {
        virtualizer.scrollToIndex(index, { align: 'start' })
        if (++attempts < maxRetries) attempt()
        else console.warn(`[scrollToIndexWithRetry] gave up after ${maxRetries} attempts for index=${index}`)
        return
      }

      const targetScrollTop = m.start - gap
      const currentScrollTop = scrollerEl.scrollTop
      const alreadyCorrect = Math.abs(currentScrollTop - targetScrollTop) <= 2
      console.log(`[scrollToIndexWithRetry] SETTLING index=${index} targetScrollTop=${targetScrollTop} currentScrollTop=${currentScrollTop} alreadyCorrect=${alreadyCorrect}`)

      if (!alreadyCorrect) {
        // Set scrollTop directly — do NOT use virtualizer.scrollToOffset here,
        // which queues an async scroll and creates races with other in-flight scrolls.
        scrollerEl.scrollTop = targetScrollTop
      }

      // Verify the scroll stuck in the next frame.
      requestAnimationFrame(() => {
        if (isCancelled?.()) {
          console.log(`[scrollToIndexWithRetry] cancelled in drift-check rAF index=${index}`)
          return
        }
        const actual = scrollerEl.scrollTop
        if (Math.abs(actual - targetScrollTop) > 2) {
          console.log(`[scrollToIndexWithRetry] scroll drifted: actual=${actual}, re-applying targetScrollTop=${targetScrollTop}`)
          scrollerEl.scrollTop = targetScrollTop
        } else {
          console.log(`[scrollToIndexWithRetry] scroll confirmed at ${actual}`)
        }
        onScrolled?.()
      })
    })
  }

  attempt()
}
