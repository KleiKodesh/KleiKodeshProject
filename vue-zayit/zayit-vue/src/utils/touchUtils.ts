/**
 * Touch device detection and utilities
 */

/**
 * Detect if the current device supports touch
 */
export function isTouchDevice(): boolean {
    return (
        'ontouchstart' in window ||
        navigator.maxTouchPoints > 0 ||
        // @ts-ignore - for older browsers
        navigator.msMaxTouchPoints > 0
    )
}

/**
 * Detect if the device is primarily touch-based (mobile/tablet)
 */
export function isPrimaryTouchDevice(): boolean {
    return window.matchMedia('(hover: none) and (pointer: coarse)').matches
}

/**
 * Get the appropriate event names for the current device
 */
export function getEventNames() {
    const isTouchSupported = isTouchDevice()

    return {
        start: isTouchSupported ? 'touchstart' : 'mousedown',
        move: isTouchSupported ? 'touchmove' : 'mousemove',
        end: isTouchSupported ? 'touchend' : 'mouseup',
        // For click outside, we need both
        clickOutside: ['click', 'touchstart']
    }
}

/**
 * Get coordinates from mouse or touch event
 */
export function getEventCoordinates(event: MouseEvent | TouchEvent): { x: number; y: number } {
    if (event instanceof MouseEvent) {
        return { x: event.clientX, y: event.clientY }
    } else {
        const touch = event.touches[0] || event.changedTouches[0]
        return { x: touch?.clientX ?? 0, y: touch?.clientY ?? 0 }
    }
}

/**
 * Add touch-friendly event listeners
 */
export function addTouchFriendlyListener(
    element: HTMLElement,
    eventType: 'start' | 'move' | 'end',
    handler: (event: MouseEvent | TouchEvent) => void,
    options?: AddEventListenerOptions
) {
    const events = getEventNames()

    switch (eventType) {
        case 'start':
            element.addEventListener('mousedown', handler as EventListener, options)
            element.addEventListener('touchstart', handler as EventListener, { ...options, passive: true })
            break
        case 'move':
            element.addEventListener('mousemove', handler as EventListener, options)
            element.addEventListener('touchmove', handler as EventListener, options)
            break
        case 'end':
            element.addEventListener('mouseup', handler as EventListener, options)
            element.addEventListener('touchend', handler as EventListener, options)
            break
    }
}

/**
 * Remove touch-friendly event listeners
 */
export function removeTouchFriendlyListener(
    element: HTMLElement,
    eventType: 'start' | 'move' | 'end',
    handler: (event: MouseEvent | TouchEvent) => void,
    options?: AddEventListenerOptions
) {
    switch (eventType) {
        case 'start':
            element.removeEventListener('mousedown', handler as EventListener, options)
            element.removeEventListener('touchstart', handler as EventListener)
            break
        case 'move':
            element.removeEventListener('mousemove', handler as EventListener, options)
            element.removeEventListener('touchmove', handler as EventListener, options)
            break
        case 'end':
            element.removeEventListener('mouseup', handler as EventListener, options)
            element.removeEventListener('touchend', handler as EventListener, options)
            break
    }
}