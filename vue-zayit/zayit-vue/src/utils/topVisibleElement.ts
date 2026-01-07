/**
 * Top Visible Element
 * 
 * Utilities for finding the top visible element within scrollable containers
 * used for saving lineindex
 */

/**
 * Find the top visible element in a scrollable container
 * 
 * @param containerElement - The scrollable container element
 * @param childElements - Array of child elements to check (must be in DOM order)
 * @returns The index of the top visible element, or undefined if none found
 */
export function getTopVisibleElementIndex(
    containerElement: HTMLElement,
    childElements: (HTMLElement | null | undefined)[]
): number | undefined {
    const containerRect = containerElement.getBoundingClientRect()
    const containerTop = containerRect.top

    let closestIndex: number | undefined = undefined
    let closestDistance = Infinity

    for (let i = 0; i < childElements.length; i++) {
        const element = childElements[i]
        if (!element) continue

        const elementRect = element.getBoundingClientRect()

        // Only consider elements that are at least partially visible
        if (elementRect.bottom > containerTop && elementRect.top < containerRect.bottom) {
            const distance = Math.abs(elementRect.top - containerTop)
            if (distance < closestDistance) {
                closestDistance = distance
                closestIndex = i
            }
        }
    }

    return closestIndex
}

/**
 * Find the top visible element using a selector
 * 
 * @param containerElement - The scrollable container element
 * @param selector - CSS selector to find child elements
 * @returns The index of the top visible element, or undefined if none found
 */
export function getTopVisibleElementBySelector(
    containerElement: HTMLElement,
    selector: string
): number | undefined {
    const elements = Array.from(containerElement.querySelectorAll(selector)) as HTMLElement[]
    return getTopVisibleElementIndex(containerElement, elements)
}
