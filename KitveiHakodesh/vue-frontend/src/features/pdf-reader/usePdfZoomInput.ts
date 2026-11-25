import { type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import type { PdfScroller } from './usePdfScroller'

/**
 * Zoom input for the PDF scroller: Ctrl+wheel and trackpad pinch (both arrive as ctrl+wheel),
 * two-finger touch pinch, and Ctrl+= / Ctrl+- / Ctrl+0 - the book view's zoom keys.
 * Wheel and pinch keep the point under the fingers / cursor still.
 */
export function usePdfZoomInput(root: Ref<HTMLElement | null>, scroller: Ref<HTMLElement | null>, pdf: PdfScroller) {
  const hostWindow = useHostWindow()

  // Ctrl+wheel / trackpad pinch: a smooth factor from the wheel delta (pinch sends small deltas).
  useEventListener(scroller, 'wheel', (e: WheelEvent) => {
    if (!e.ctrlKey) return
    e.preventDefault()
    const delta = e.deltaMode === 1 ? e.deltaY * 16 : e.deltaY
    pdf.zoomBy(Math.exp(-delta * 0.002), [e.clientX, e.clientY])
  }, { passive: false })

  // Two-finger touch pinch.
  const touches = new Map<number, { x: number; y: number }>()
  let lastDistance = 0
  const distance = () => {
    const [a, b] = [...touches.values()] as [{ x: number; y: number }, { x: number; y: number }]
    return Math.hypot(a.x - b.x, a.y - b.y)
  }
  const midpoint = (): [number, number] => {
    const [a, b] = [...touches.values()] as [{ x: number; y: number }, { x: number; y: number }]
    return [(a.x + b.x) / 2, (a.y + b.y) / 2]
  }
  useEventListener(scroller, 'touchstart', (e: TouchEvent) => {
    for (const t of e.changedTouches) touches.set(t.identifier, { x: t.clientX, y: t.clientY })
    if (touches.size === 2) lastDistance = distance()
  }, { passive: true })
  useEventListener(scroller, 'touchmove', (e: TouchEvent) => {
    for (const t of e.changedTouches) if (touches.has(t.identifier)) touches.set(t.identifier, { x: t.clientX, y: t.clientY })
    if (touches.size !== 2 || !lastDistance) return
    e.preventDefault()
    const d = distance()
    pdf.zoomBy(d / lastDistance, midpoint())
    lastDistance = d
  }, { passive: false })
  const endTouch = (e: TouchEvent) => {
    for (const t of e.changedTouches) touches.delete(t.identifier)
    if (touches.size < 2) lastDistance = 0
  }
  useEventListener(scroller, 'touchend', endTouch, { passive: true })
  useEventListener(scroller, 'touchcancel', endTouch, { passive: true })

  // Keyboard, while focus is inside this viewer.
  useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
    if (!(e.ctrlKey || e.metaKey) || e.altKey) return
    const active = hostWindow.document.activeElement
    if (!root.value || !(active === hostWindow.document.body || root.value.contains(active))) return
    if (e.code === 'Equal' || e.code === 'NumpadAdd') pdf.zoomIn()
    else if (e.code === 'Minus' || e.code === 'NumpadSubtract') pdf.zoomOut()
    else if (e.code === 'Digit0' || e.code === 'Numpad0') pdf.fitWidth()
    else return
    e.preventDefault()
    e.stopPropagation()
  })
}
