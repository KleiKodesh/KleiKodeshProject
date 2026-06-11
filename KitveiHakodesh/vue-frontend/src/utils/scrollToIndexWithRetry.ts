import type { Virtualizer } from '@tanstack/vue-virtual'

/**
 * Scrolls a virtualizer to a target index so the item appears at the top of the
 * scroller (offset by topReserved px).
 *
 * index: either a static number or a resolver function called fresh on each
 *   attempt. Use a resolver when flatItems can change between the call and the
 *   rAF (e.g. a new groups load completes mid-flight).
 *
 * After setting scrollTop, waits one rAF then re-resolves the index and
 * re-reads the measurement. If the measurement changed (item was estimated,
 * now DOM-measured with a different size), re-applies with the fresh target
 * and retries up to maxRetries total attempts.
 *
 * onScrolled: optional callback invoked once the scroll is confirmed stable.
 * isCancelled: optional function checked before each rAF.
 */
export function scrollToIndexWithRetry(
  virtualizer: Virtualizer<Element, Element>,
  scrollerEl: HTMLElement,
  index: number | (() => number),
  topReserved = 0,
  maxRetries = 5,
  onScrolled?: () => void,
  isCancelled?: () => boolean,
): void {
  let attempts = 0
  const gap = topReserved + 8

  function resolveIndex(): number {
    return typeof index === 'function' ? index() : index
  }

  function attempt() {
    requestAnimationFrame(() => {
      if (isCancelled?.()) return
      const idx = resolveIndex()
      const m = idx >= 0 ? virtualizer.measurementsCache.find((c) => c.index === idx) : undefined

      if (idx < 0 || !m) {
        if (idx >= 0) virtualizer.scrollToIndex(idx, { align: 'start' })
        if (++attempts < maxRetries) attempt()
        return
      }

      const targetScrollTop = Math.max(0, m.start - gap)
      scrollerEl.scrollTop = targetScrollTop

      requestAnimationFrame(() => {
        if (isCancelled?.()) return

        const freshIdx = resolveIndex()
        const freshM = freshIdx >= 0
          ? virtualizer.measurementsCache.find((c) => c.index === freshIdx)
          : undefined
        const freshTarget = freshM ? Math.max(0, freshM.start - gap) : targetScrollTop
        const actual = scrollerEl.scrollTop

        if (Math.abs(actual - freshTarget) > 2) {
          if (++attempts < maxRetries) attempt()
          else onScrolled?.()
        } else {
          onScrolled?.()
        }
      })
    })
  }

  attempt()
}
