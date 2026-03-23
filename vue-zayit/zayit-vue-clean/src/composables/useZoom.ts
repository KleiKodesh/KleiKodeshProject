import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'

export const ZOOM_CONFIG = {
  MIN: 50,
  MAX: 200,
  DEFAULT: 100,
  STEP: 10,
  WHEEL_SENSITIVITY: 1,
  PINCH_SENSITIVITY: 2,
} as const

export function calculateZoom(current: number, delta: number): number {
  return Math.max(ZOOM_CONFIG.MIN, Math.min(ZOOM_CONFIG.MAX, current + delta))
}

export function zoomIn(currentZoom: number): number {
  return calculateZoom(currentZoom, ZOOM_CONFIG.STEP)
}

export function zoomOut(currentZoom: number): number {
  return calculateZoom(currentZoom, -ZOOM_CONFIG.STEP)
}

export function resetZoom(): number {
  return ZOOM_CONFIG.DEFAULT
}

export interface ZoomHandlerOptions {
  zoom: Ref<number>
  target?: Ref<HTMLElement | undefined> | HTMLElement | Window
  enabled?: Ref<boolean> | boolean
}

export function useZoomHandler(options: ZoomHandlerOptions) {
  const { zoom, target = window, enabled = true } = options

  let initialDistance = 0
  let initialZoom = 0

  function isEnabled() {
    return typeof enabled === 'boolean' ? enabled : enabled.value
  }

  function getTouchDistance(t1: Touch, t2: Touch): number {
    const dx = t1.clientX - t2.clientX
    const dy = t1.clientY - t2.clientY
    return Math.sqrt(dx * dx + dy * dy)
  }

  useEventListener(target, 'keydown', (event: KeyboardEvent) => {
    if (!isEnabled()) return
    const ctrl = event.ctrlKey || event.metaKey
    if (ctrl && (event.code === 'Equal' || event.code === 'NumpadAdd')) {
      event.preventDefault()
      zoom.value = zoomIn(zoom.value)
    } else if (ctrl && (event.code === 'Minus' || event.code === 'NumpadSubtract')) {
      event.preventDefault()
      zoom.value = zoomOut(zoom.value)
    } else if (ctrl && (event.code === 'Digit0' || event.code === 'Numpad0')) {
      event.preventDefault()
      zoom.value = resetZoom()
    }
  })

  useEventListener(target, 'wheel', (event: WheelEvent) => {
    if (!isEnabled() || !(event.ctrlKey || event.metaKey)) return
    event.preventDefault()
    zoom.value = calculateZoom(zoom.value, -event.deltaY * ZOOM_CONFIG.WHEEL_SENSITIVITY)
  }, { passive: false })

  useEventListener(target, 'touchstart', (event: TouchEvent) => {
    if (!isEnabled() || event.touches.length !== 2) return
    event.preventDefault()
    const [t1, t2] = [event.touches[0]!, event.touches[1]!]
    initialDistance = getTouchDistance(t1, t2)
    initialZoom = zoom.value
  }, { passive: false })

  useEventListener(target, 'touchmove', (event: TouchEvent) => {
    if (!isEnabled() || event.touches.length !== 2 || initialDistance === 0) return
    event.preventDefault()
    const [t1, t2] = [event.touches[0]!, event.touches[1]!]
    const delta = (getTouchDistance(t1, t2) - initialDistance) * ZOOM_CONFIG.PINCH_SENSITIVITY
    zoom.value = calculateZoom(initialZoom, delta)
  }, { passive: false })

  useEventListener(target, 'touchend', (event: TouchEvent) => {
    if (event.touches.length < 2) { initialDistance = 0; initialZoom = 0 }
  })
}
