import { useEventListener } from '@vueuse/core'
import { type Ref, computed, toValue, type MaybeRefOrGetter } from 'vue'

/**
 * Composable for arrow key navigation in tree/list structures
 * Handles ArrowUp/ArrowDown/Tab/Escape navigation through focusable items
 * 
 * Usage:
 * 1. Add tabindex="0" to all focusable items
 * 2. Call useListKeyboardNavigation(containerRef, options)
 * 3. Attach to container: @keydown="handleKeyDown"
 */
export function useListKeyboardNavigation(
    containerRef: Ref<HTMLElement | undefined>,
    options?: {
        onEscape?: () => void
        onTab?: () => void
        enabled?: MaybeRefOrGetter<boolean>
    }
) {
    const isEnabled = computed(() => options?.enabled !== undefined ? toValue(options.enabled) : true)

    // Use useEventListener for keyboard handling
    useEventListener(containerRef, 'keydown', (event: KeyboardEvent) => {
        if (!isEnabled.value) return

        // Arrow navigation
        if (event.key === 'ArrowDown') {
            event.preventDefault()
            event.stopPropagation()
            navigateDown()
            return
        }

        if (event.key === 'ArrowUp') {
            event.preventDefault()
            event.stopPropagation()
            navigateUp()
            return
        }

        // Tab key - call onTab callback or onEscape as fallback
        if (event.key === 'Tab') {
            event.preventDefault()
            event.stopPropagation()
            if (options?.onTab) {
                options.onTab()
            } else if (options?.onEscape) {
                options.onEscape()
            }
            return
        }

        // Escape key
        if (event.key === 'Escape') {
            event.preventDefault()
            event.stopPropagation()
            options?.onEscape?.()
            return
        }
    })

    function getFocusableItems(): HTMLElement[] {
        if (!containerRef.value) return []
        const items = containerRef.value.querySelectorAll('[tabindex="0"]')
        return Array.from(items) as HTMLElement[]
    }

    function scrollIntoView(element: HTMLElement) {
        element.scrollIntoView({
            behavior: 'auto',
            block: 'nearest',
            inline: 'nearest'
        })
    }

    function navigateDown() {
        const focusableItems = getFocusableItems()
        if (focusableItems.length === 0) return

        const currentIndex = focusableItems.findIndex(item => item === document.activeElement)
        const nextIndex = currentIndex < focusableItems.length - 1 ? currentIndex + 1 : currentIndex

        const nextItem = focusableItems[nextIndex]
        if (nextItem) {
            nextItem.focus({ preventScroll: true })
            scrollIntoView(nextItem)
        }
    }

    function navigateUp() {
        const focusableItems = getFocusableItems()
        if (focusableItems.length === 0) return

        const currentIndex = focusableItems.findIndex(item => item === document.activeElement)
        const nextIndex = currentIndex > 0 ? currentIndex - 1 : currentIndex === -1 ? 0 : currentIndex

        const nextItem = focusableItems[nextIndex]
        if (nextItem) {
            nextItem.focus({ preventScroll: true })
            scrollIntoView(nextItem)
        }
    }

    // For backward compatibility with template @keydown handlers
    function handleKeyDown(e: KeyboardEvent) {
        // This is now handled by useEventListener above
        // Keep this function for backward compatibility but it does nothing
    }

    return {
        handleKeyDown,
        navigateDown,
        navigateUp
    }
}
