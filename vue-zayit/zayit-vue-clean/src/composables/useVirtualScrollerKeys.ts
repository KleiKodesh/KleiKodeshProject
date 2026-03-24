import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'
import type { Virtualizer } from '@tanstack/vue-virtual'

/**
 * Wires Ctrl+Home / Ctrl+End keyboard shortcuts to a TanStack virtual scroller.
 *
 * Ctrl+Home → scrollToIndex(0) then scrollTop = 0 (ensures we're truly at the top)
 * Ctrl+End  → scrollToIndex(lastIndex) then scrollTop = scrollHeight (ensures we're truly at the bottom)
 *
 * @param scrollerEl - ref to the scroll container element
 * @param getVirtualizer - getter returning the current Virtualizer instance
 * @param getCount - getter returning the total item count
 */
export function useVirtualScrollerKeys(
  scrollerEl: Ref<HTMLElement | null>,
  getVirtualizer: () => Virtualizer<Element, Element>,
  getCount: () => number,
) {
  useEventListener(scrollerEl, 'keydown', (event: KeyboardEvent) => {
    if (!event.ctrlKey && !event.metaKey) return
    const el = scrollerEl.value
    if (!el) return

    if (event.code === 'Home') {
      event.preventDefault()
      const v = getVirtualizer()
      v.scrollToIndex(0, { align: 'start' })
      requestAnimationFrame(() => { el.scrollTop = 0 })
    } else if (event.code === 'End') {
      event.preventDefault()
      const count = getCount()
      if (!count) return
      const v = getVirtualizer()
      v.scrollToIndex(count - 1, { align: 'end' })
      requestAnimationFrame(() => { el.scrollTop = el.scrollHeight })
    }
  })
}
