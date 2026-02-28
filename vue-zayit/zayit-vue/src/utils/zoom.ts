import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'

/**
 * Zoom configuration and constants
 */
export const ZOOM_CONFIG = {
    MIN: 50,
    MAX: 200,
    DEFAULT: 100,
    STEP: 10,
    WHEEL_SENSITIVITY: 1, // How much each wheel tick changes zoom
    PINCH_SENSITIVITY: 2, // How much pinch gesture changes zoom
} as const

/**
 * Calculate new zoom level within bounds
 */
export function calculateZoom(current: number, delta: number): number {
    const newZoom = current + delta
    return Math.max(ZOOM_CONFIG.MIN, Math.min(ZOOM_CONFIG.MAX, newZoom))
}

/**
 * Zoom in by one step
 */
export function zoomIn(currentZoom: number): number {
    return calculateZoom(currentZoom, ZOOM_CONFIG.STEP)
}

/**
 * Zoom out by one step
 */
export function zoomOut(currentZoom: number): number {
    return calculateZoom(currentZoom, -ZOOM_CONFIG.STEP)
}

/**
 * Reset zoom to default
 */
export function resetZoom(): number {
    return ZOOM_CONFIG.DEFAULT
}

/**
 * Options for zoom handler
 */
export interface ZoomHandlerOptions {
    /**
     * Current zoom level (reactive ref)
     */
    zoom: Ref<number>

    /**
     * Target element to attach listeners to (defaults to window)
     */
    target?: Ref<HTMLElement | undefined> | HTMLElement | Window

    /**
     * Whether zoom is enabled (defaults to true)
     */
    enabled?: Ref<boolean> | boolean

    /**
     * Callback when zoom changes
     */
    onZoomChange?: (newZoom: number) => void
}

/**
 * Setup comprehensive zoom handling including keyboard, trackpad, and touch
 * 
 * @example
 * ```ts
 * const zoom = ref(100)
 * const containerRef = ref<HTMLElement>()
 * 
 * useZoomHandler({
 *   zoom,
 *   target: containerRef,
 *   onZoomChange: (newZoom) => {
 *     console.log('Zoom changed to:', newZoom)
 *   }
 * })
 * ```
 */
export function useZoomHandler(options: ZoomHandlerOptions) {
    const { zoom, target = window, enabled = true, onZoomChange } = options

    // Track touch points for pinch-to-zoom
    let initialDistance = 0
    let initialZoom = 0

    /**
     * Calculate distance between two touch points
     */
    function getTouchDistance(touch1: Touch, touch2: Touch): number {
        const dx = touch1.clientX - touch2.clientX
        const dy = touch1.clientY - touch2.clientY
        return Math.sqrt(dx * dx + dy * dy)
    }

    /**
     * Update zoom value
     */
    function updateZoom(newZoom: number) {
        zoom.value = newZoom
        onZoomChange?.(newZoom)
    }

    // Keyboard shortcuts: Ctrl+Plus, Ctrl+Minus, Ctrl+0
    useEventListener(target, 'keydown', (event: KeyboardEvent) => {
        const isEnabled = typeof enabled === 'boolean' ? enabled : enabled.value
        if (!isEnabled) return

        const hasCtrlOrMeta = event.ctrlKey || event.metaKey

        // Ctrl+Plus/Equal: Zoom in
        if (hasCtrlOrMeta && (event.code === 'Equal' || event.code === 'NumpadAdd')) {
            event.preventDefault()
            updateZoom(zoomIn(zoom.value))
        }

        // Ctrl+Minus: Zoom out
        if (hasCtrlOrMeta && (event.code === 'Minus' || event.code === 'NumpadSubtract')) {
            event.preventDefault()
            updateZoom(zoomOut(zoom.value))
        }

        // Ctrl+0: Reset zoom
        if (hasCtrlOrMeta && (event.code === 'Digit0' || event.code === 'Numpad0')) {
            event.preventDefault()
            updateZoom(resetZoom())
        }
    })

    // Trackpad/Mouse wheel zoom: Ctrl+Wheel
    useEventListener(target, 'wheel', (event: WheelEvent) => {
        const isEnabled = typeof enabled === 'boolean' ? enabled : enabled.value
        if (!isEnabled) return

        const hasCtrlOrMeta = event.ctrlKey || event.metaKey
        if (!hasCtrlOrMeta) return

        event.preventDefault()

        // deltaY is negative when scrolling up (zoom in), positive when scrolling down (zoom out)
        const delta = -event.deltaY * ZOOM_CONFIG.WHEEL_SENSITIVITY
        updateZoom(calculateZoom(zoom.value, delta))
    }, { passive: false }) // passive: false allows preventDefault

    // Touch pinch-to-zoom
    useEventListener(target, 'touchstart', (event: TouchEvent) => {
        const isEnabled = typeof enabled === 'boolean' ? enabled : enabled.value
        if (!isEnabled) return

        if (event.touches.length === 2) {
            event.preventDefault()
            const touch0 = event.touches[0]
            const touch1 = event.touches[1]
            if (touch0 && touch1) {
                initialDistance = getTouchDistance(touch0, touch1)
                initialZoom = zoom.value
            }
        }
    }, { passive: false })

    useEventListener(target, 'touchmove', (event: TouchEvent) => {
        const isEnabled = typeof enabled === 'boolean' ? enabled : enabled.value
        if (!isEnabled) return

        if (event.touches.length === 2 && initialDistance > 0) {
            event.preventDefault()

            const touch0 = event.touches[0]
            const touch1 = event.touches[1]
            if (touch0 && touch1) {
                const currentDistance = getTouchDistance(touch0, touch1)
                const distanceChange = currentDistance - initialDistance
                const zoomDelta = distanceChange * ZOOM_CONFIG.PINCH_SENSITIVITY

                updateZoom(calculateZoom(initialZoom, zoomDelta))
            }
        }
    }, { passive: false })

    useEventListener(target, 'touchend', (event: TouchEvent) => {
        if (event.touches.length < 2) {
            initialDistance = 0
            initialZoom = 0
        }
    })
}
