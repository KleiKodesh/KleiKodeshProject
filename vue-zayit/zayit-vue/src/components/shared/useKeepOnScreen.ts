/**
 * Keep On Screen Composable
 * Adjusts element position to ensure it stays within viewport bounds
 * Reusable for context menus, tooltips, dropdowns, etc.
 * 
 * @example
 * // For a tooltip
 * const tooltipPosition = calculateOnScreenPosition(
 *   { x: buttonRect.right, y: buttonRect.top },
 *   tooltipWidth,
 *   tooltipHeight,
 *   { horizontalAlign: 'right', verticalAlign: 'top', padding: 4 }
 * )
 * 
 * @example
 * // For a dropdown menu
 * const dropdownPosition = calculateOnScreenPosition(
 *   { x: buttonRect.left, y: buttonRect.bottom },
 *   dropdownWidth,
 *   dropdownHeight,
 *   { horizontalAlign: 'left', verticalAlign: 'bottom', padding: 8 }
 * )
 */

export interface KeepOnScreenOptions {
    /**
     * Padding from viewport edges in pixels
     * @default 8
     */
    padding?: number

    /**
     * Preferred horizontal alignment
     * @default 'right' - element appears to the right of the point
     */
    horizontalAlign?: 'left' | 'right'

    /**
     * Preferred vertical alignment
     * @default 'bottom' - element appears below the point
     */
    verticalAlign?: 'top' | 'bottom'
}

export interface Position {
    x: number
    y: number
}

/**
 * Calculate adjusted position to keep element within viewport
 * @param clickPosition - Original click/trigger position
 * @param elementWidth - Width of the element to position
 * @param elementHeight - Height of the element to position
 * @param options - Configuration options
 * @returns Adjusted position that keeps element on screen
 */
export function calculateOnScreenPosition(
    clickPosition: Position,
    elementWidth: number,
    elementHeight: number,
    options: KeepOnScreenOptions = {}
): Position {
    const {
        padding = 8,
        horizontalAlign = 'right',
        verticalAlign = 'bottom'
    } = options

    const viewportWidth = window.innerWidth
    const viewportHeight = window.innerHeight

    let x = clickPosition.x
    let y = clickPosition.y

    // Horizontal positioning
    if (horizontalAlign === 'right') {
        // Try to place to the right of click point
        if (x + elementWidth + padding > viewportWidth) {
            // Doesn't fit on right, try left
            x = x - elementWidth
            if (x < padding) {
                // Doesn't fit on left either, align to right edge
                x = viewportWidth - elementWidth - padding
            }
        }
    } else {
        // Try to place to the left of click point
        x = x - elementWidth
        if (x < padding) {
            // Doesn't fit on left, try right
            x = clickPosition.x
            if (x + elementWidth + padding > viewportWidth) {
                // Doesn't fit on right either, align to left edge
                x = padding
            }
        }
    }

    // Vertical positioning
    if (verticalAlign === 'bottom') {
        // Try to place below click point
        if (y + elementHeight + padding > viewportHeight) {
            // Doesn't fit below, try above
            y = y - elementHeight
            if (y < padding) {
                // Doesn't fit above either, align to bottom edge
                y = viewportHeight - elementHeight - padding
            }
        }
    } else {
        // Try to place above click point
        y = y - elementHeight
        if (y < padding) {
            // Doesn't fit above, try below
            y = clickPosition.y
            if (y + elementHeight + padding > viewportHeight) {
                // Doesn't fit below either, align to top edge
                y = padding
            }
        }
    }

    // Final bounds check - ensure element is fully visible
    x = Math.max(padding, Math.min(x, viewportWidth - elementWidth - padding))
    y = Math.max(padding, Math.min(y, viewportHeight - elementHeight - padding))

    return { x, y }
}
