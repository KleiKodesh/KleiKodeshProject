import { ref, type Ref } from 'vue'

/**
 * Composable for providing visual touch feedback
 * Adds a brief visual feedback when an element is touched/clicked
 */
export function useTouchFeedback(elementRef: Ref<HTMLElement | undefined>) {
    const isActive = ref(false)

    const triggerFeedback = (duration = 150) => {
        if (!elementRef.value) return

        isActive.value = true

        // Add active class for visual feedback
        elementRef.value.classList.add('touch-active')

        setTimeout(() => {
            isActive.value = false
            elementRef.value?.classList.remove('touch-active')
        }, duration)
    }

    const addTouchListeners = () => {
        if (!elementRef.value) return

        const element = elementRef.value

        // Handle both mouse and touch events
        const handleStart = () => triggerFeedback()

        element.addEventListener('mousedown', handleStart, { passive: true })
        element.addEventListener('touchstart', handleStart, { passive: true })

        // Return cleanup function
        return () => {
            element.removeEventListener('mousedown', handleStart)
            element.removeEventListener('touchstart', handleStart)
        }
    }

    return {
        isActive,
        triggerFeedback,
        addTouchListeners
    }
}