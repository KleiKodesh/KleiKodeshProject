import { ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'

/**
 * Hand tool: while on, dragging the page with the mouse scrolls it (no text selection).
 * Links, form fields and annotation editors keep their own clicks.
 */
export function usePdfHandTool(scroller: Ref<HTMLElement | null>) {
  const enabled = ref(false)
  const dragging = ref(false)
  let startX = 0
  let startY = 0
  let startLeft = 0
  let startTop = 0

  useEventListener(scroller, 'pointerdown', (e: PointerEvent) => {
    const el = scroller.value
    if (!enabled.value || !el || e.button !== 0 || e.pointerType === 'touch') return
    if ((e.target as Element).closest('a, input, textarea, select, button, [contenteditable="true"]')) return
    e.preventDefault()
    dragging.value = true
    startX = e.clientX
    startY = e.clientY
    startLeft = el.scrollLeft
    startTop = el.scrollTop
    el.setPointerCapture(e.pointerId)
  })
  useEventListener(scroller, 'pointermove', (e: PointerEvent) => {
    const el = scroller.value
    if (!dragging.value || !el) return
    el.scrollLeft = startLeft - (e.clientX - startX)
    el.scrollTop = startTop - (e.clientY - startY)
  })
  const stop = () => (dragging.value = false)
  useEventListener(scroller, 'pointerup', stop)
  useEventListener(scroller, 'pointercancel', stop)
  useEventListener(scroller, 'lostpointercapture', stop)

  const toggle = () => (enabled.value = !enabled.value)
  return { enabled, dragging, toggle }
}
