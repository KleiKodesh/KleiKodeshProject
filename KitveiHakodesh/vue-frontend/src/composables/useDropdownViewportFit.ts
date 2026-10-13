import { watch, onScopeDispose } from 'vue'
import { unrefElement, useEventListener, type MaybeElementRef } from '@vueuse/core'

type MaybeElement = HTMLElement | null | undefined

/** Gap kept between the dropdown and the window edge. */
const VIEWPORT_MARGIN = 8
/** Below this much room under its top edge, the dropdown is shifted up instead of shrunk. */
const MINIMUM_USEFUL_HEIGHT = 160

/**
 * Keeps a dropdown fully inside the window: shifts it horizontally and vertically when it
 * would spill past an edge, and caps its height to the room left (scrolling the rest).
 *
 * Works on the element's MEASURED rectangle, so it is direction-agnostic (LTR and RTL,
 * toolbar on any side) and leaves a dropdown that already fits completely untouched.
 * Re-fits when the dropdown appears, when its size changes and when the window resizes.
 *
 * Only inline `translate`, `max-height`, `max-width` and `overflow-y` are written, so the
 * dropdown's own CSS still decides where it opens and how it looks.
 */
export function useDropdownViewportFit(target: MaybeElementRef<MaybeElement>) {
  let resizeObserver: ResizeObserver | undefined
  let fittedElement: HTMLElement | undefined
  let isFitting = false

  function clearFit(element: HTMLElement) {
    element.style.translate = ''
    element.style.maxHeight = ''
    element.style.maxWidth = ''
    element.style.overflowY = ''
  }

  function fitInsideViewport(element: HTMLElement) {
    if (isFitting) return
    isFitting = true
    clearFit(element)
    const rectangle = element.getBoundingClientRect()
    const viewportWidth = window.innerWidth
    const viewportHeight = window.innerHeight
    const usableWidth = viewportWidth - VIEWPORT_MARGIN * 2
    const usableHeight = viewportHeight - VIEWPORT_MARGIN * 2

    // ── Horizontal ──
    let shiftX = 0
    if (rectangle.width > usableWidth) {
      element.style.maxWidth = `${usableWidth}px`
      shiftX = VIEWPORT_MARGIN - rectangle.left
    } else if (rectangle.right > viewportWidth - VIEWPORT_MARGIN) {
      shiftX = viewportWidth - VIEWPORT_MARGIN - rectangle.right
    } else if (rectangle.left < VIEWPORT_MARGIN) {
      shiftX = VIEWPORT_MARGIN - rectangle.left
    }

    // ── Vertical ──
    let shiftY = 0
    let top = rectangle.top
    if (top < VIEWPORT_MARGIN) {
      shiftY = VIEWPORT_MARGIN - top
      top = VIEWPORT_MARGIN
    }
    const roomBelowTop = viewportHeight - VIEWPORT_MARGIN - top
    if (rectangle.height > roomBelowTop) {
      if (roomBelowTop < Math.min(rectangle.height, MINIMUM_USEFUL_HEIGHT)) {
        // Too little room under it: move it up as far as its height needs.
        const newTop = Math.max(VIEWPORT_MARGIN, viewportHeight - VIEWPORT_MARGIN - rectangle.height)
        shiftY += newTop - top
        top = newTop
      }
      const availableHeight = viewportHeight - VIEWPORT_MARGIN - top
      if (rectangle.height > availableHeight) {
        element.style.maxHeight = `${Math.min(availableHeight, usableHeight)}px`
        element.style.overflowY = 'auto'
      }
    }

    if (shiftX !== 0 || shiftY !== 0) element.style.translate = `${shiftX}px ${shiftY}px`
    // The observer reports our own max-height change; ignore that echo.
    requestAnimationFrame(() => (isFitting = false))
  }

  function stopObserving() {
    resizeObserver?.disconnect()
    resizeObserver = undefined
    fittedElement = undefined
  }

  watch(
    () => unrefElement(target) as MaybeElement,
    (element) => {
      stopObserving()
      if (!element) return
      fittedElement = element
      requestAnimationFrame(() => {
        if (fittedElement === element) fitInsideViewport(element)
      })
      resizeObserver = new ResizeObserver(() => fitInsideViewport(element))
      resizeObserver.observe(element)
    },
    { immediate: true, flush: 'post' },
  )

  useEventListener(window, 'resize', () => {
    if (fittedElement) fitInsideViewport(fittedElement)
  })

  onScopeDispose(stopObserving)
}
