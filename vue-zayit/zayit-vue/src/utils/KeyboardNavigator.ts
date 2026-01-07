/**
 * KeyboardNavigator - Handles arrow key navigation for tree/list structures
 * 
 * Usage:
 * 1. Add tabindex="0" to all focusable items
 * 2. Create navigator: const nav = new KeyboardNavigator(containerElement)
 * 3. Attach to container: @keydown="nav.handleKeyDown"
 * 4. Cleanup on unmount: nav.destroy()
 */
export class KeyboardNavigator {
    private container: HTMLElement

    constructor(container: HTMLElement) {
        this.container = container
    }

    /**
     * Handle keydown events - call this from @keydown on your container
     */
    handleKeyDown = (e: KeyboardEvent) => {
        if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') {
            return
        }

        // Stop the event from bubbling and scrolling the page
        e.preventDefault()
        e.stopPropagation()

        const focusableItems = this.getFocusableItems()
        if (focusableItems.length === 0) return

        const currentIndex = focusableItems.findIndex(item => item === document.activeElement)

        let nextIndex: number
        if (e.key === 'ArrowDown') {
            nextIndex = currentIndex < focusableItems.length - 1 ? currentIndex + 1 : currentIndex
        } else {
            nextIndex = currentIndex > 0 ? currentIndex - 1 : currentIndex === -1 ? 0 : currentIndex
        }

        const nextItem = focusableItems[nextIndex]
        if (nextItem) {
            nextItem.focus({ preventScroll: true })
            this.scrollIntoView(nextItem)
        }
    }

    /**
     * Get all focusable items within the container
     */
    private getFocusableItems(): HTMLElement[] {
        const items = this.container.querySelectorAll('[tabindex="0"]')
        return Array.from(items) as HTMLElement[]
    }

    /**
     * Scroll the item into view within the container
     * Uses 'nearest' to only scroll if item is out of view, keeping it at the edge
     */
    private scrollIntoView(element: HTMLElement) {
        element.scrollIntoView({
            behavior: 'auto',
            block: 'nearest',
            inline: 'nearest'
        })
    }

    /**
     * Cleanup - call this when component unmounts
     */
    destroy() {
        // Nothing to cleanup currently, but keeping for future use
    }
}
