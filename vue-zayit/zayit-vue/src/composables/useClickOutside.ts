import { ref, onMounted, onUnmounted, type Ref } from 'vue'

/**
 * Composable for handling click/touch outside events
 * Supports both mouse and touch interactions
 */
export function useClickOutside(
    elementRef: Ref<HTMLElement | undefined>,
    callback: () => void,
    options: {
        capture?: boolean
        passive?: boolean
    } = {}
) {
    const isActive = ref(true)

    const handleEvent = (event: Event) => {
        if (!isActive.value || !elementRef.value) return

        const target = event.target as Node
        if (!elementRef.value.contains(target)) {
            callback()
        }
    }

    const activate = () => {
        isActive.value = true
    }

    const deactivate = () => {
        isActive.value = false
    }

    onMounted(() => {
        // Handle both mouse and touch events
        document.addEventListener('click', handleEvent, options)
        document.addEventListener('touchstart', handleEvent, {
            ...options,
            passive: true
        })
    })

    onUnmounted(() => {
        document.removeEventListener('click', handleEvent, options)
        document.removeEventListener('touchstart', handleEvent)
    })

    return {
        isActive,
        activate,
        deactivate
    }
}