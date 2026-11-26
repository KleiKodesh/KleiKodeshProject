import { nextTick, ref, watch, onScopeDispose, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostResizeObserver } from './useHostResizeObserver'

/**
 * VueUse's useInfiniteScroll for a scroll box that can live in an app window.
 *
 * VueUse gates every load on useElementVisibility, whose IntersectionObserver is always the
 * page window's; for an element in an app window (a popup of the page) that observer stops
 * reporting while the page's own window is hidden, so loading stalled. Here the visibility
 * observer comes from the element's own window; the edge check runs on the box's scroll,
 * its resizes and visibility changes (as VueUse's does on scroll arrival and visibility).
 *
 * Same behaviour otherwise: when the box is visible, `canLoadMore()` is true and the box is
 * within `distance` px of the `direction` edge (or not scrollable at all), `onLoadMore` runs;
 * at most one load at a time, at least `interval` ms apart, re-checked after each.
 */
export function useHostInfiniteScroll(
  element: Ref<HTMLElement | null>,
  onLoadMore: () => unknown,
  options: { distance?: number; direction?: 'top' | 'bottom'; interval?: number; canLoadMore?: () => boolean },
) {
  const { distance = 0, direction = 'bottom', interval = 100, canLoadMore = () => true } = options
  const isVisible = ref(false)
  const isLoading = ref(false)

  let visibility: IntersectionObserver | undefined
  watch(
    element,
    (el) => {
      visibility?.disconnect()
      visibility = undefined
      isVisible.value = false
      const view = el?.ownerDocument.defaultView as (Window & typeof globalThis) | null | undefined
      if (!el || !view) return
      visibility = new view.IntersectionObserver((entries) => {
        const last = entries[entries.length - 1]
        if (last) isVisible.value = last.isIntersecting
      })
      visibility.observe(el)
    },
    { immediate: true, flush: 'post' },
  )
  onScopeDispose(() => visibility?.disconnect())

  function atEdge(el: HTMLElement): boolean {
    if (el.scrollHeight <= el.clientHeight) return true
    return direction === 'top'
      ? el.scrollTop <= distance
      : el.scrollHeight - el.clientHeight - el.scrollTop <= distance
  }

  function check() {
    const el = element.value
    if (isLoading.value || !el || !isVisible.value || !canLoadMore() || !atEdge(el)) return
    isLoading.value = true
    Promise.all([onLoadMore(), new Promise((resolve) => setTimeout(resolve, interval))]).finally(() => {
      isLoading.value = false
      void nextTick(check)
    })
  }

  useEventListener(element, 'scroll', check, { passive: true })
  useHostResizeObserver(element, () => check())
  watch(isVisible, check, { flush: 'post' })

  return { isLoading }
}
