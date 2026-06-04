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
): void {
  let attempts = 0
  const gap = topReserved + 8

  function attempt() {
    requestAnimationFrame(() => {
      const m = virtualizer.measurementsCache.find((c) => c.index === index)

      if (!m) {
        // Item not yet rendered — ask the virtualizer to scroll it into range,
        // then retry. Do NOT set scrollTop here; let the virtualizer do its job.
        virtualizer.scrollToIndex(index, { align: 'start' })
        if (++attempts < maxRetries) attempt()
        return
      }

      // Item is measured — set scrollTop directly. Do NOT call scrollToIndex
      // here; it runs asynchronously and would overwrite our scrollTop value.
      const targetScrollTop = m.start - gap
      const currentScrollTop = scrollerEl.scrollTop
      const alreadyCorrect = Math.abs(currentScrollTop - targetScrollTop) <= 2

      if (!alreadyCorrect) {
        scrollerEl.scrollTop = targetScrollTop
      }

      onScrolled?.()
    })
  }

  attempt()
}
