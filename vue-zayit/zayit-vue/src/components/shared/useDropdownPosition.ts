import { ref, computed, onMounted, onUnmounted, type Ref } from 'vue'

export function useDropdownPosition(
    triggerRef: Ref<HTMLElement | undefined>,
    isOpen: Ref<boolean>,
    options: {
        maxHeight?: number
        minHeight?: number
        offset?: number
    } = {}
) {
    const { maxHeight = 400, minHeight = 100, offset = 4 } = options

    const dropdownPosition = ref<'up' | 'down'>('down')
    const dropdownHeight = ref(maxHeight)

    const dropdownStyles = computed(() => {
        if (dropdownPosition.value === 'up') {
            return {
                bottom: `calc(100% + ${offset}px)`,
                top: 'auto',
                maxHeight: `${dropdownHeight.value}px`,
                boxShadow: '0 -4px 12px rgba(0, 0, 0, 0.15)'
            }
        } else {
            return {
                top: `calc(100% + ${offset}px)`,
                bottom: 'auto',
                maxHeight: `${dropdownHeight.value}px`,
                boxShadow: '0 4px 12px rgba(0, 0, 0, 0.15)'
            }
        }
    })

    const calculatePosition = () => {
        if (!triggerRef.value || !isOpen.value) return

        const trigger = triggerRef.value
        const rect = trigger.getBoundingClientRect()
        const viewportHeight = window.innerHeight

        // Calculate available space above and below
        const spaceAbove = rect.top
        const spaceBelow = viewportHeight - rect.bottom

        // Determine if we should open upwards or downwards
        const shouldOpenUp = spaceBelow < maxHeight && spaceAbove > spaceBelow
        dropdownPosition.value = shouldOpenUp ? 'up' : 'down'

        // Calculate optimal height based on available space
        const availableSpace = shouldOpenUp ? spaceAbove - offset : spaceBelow - offset
        dropdownHeight.value = Math.max(minHeight, Math.min(maxHeight, availableSpace - 20)) // 20px buffer
    }

    const handleResize = () => {
        if (isOpen.value) {
            calculatePosition()
        }
    }

    const handleScroll = () => {
        if (isOpen.value) {
            calculatePosition()
        }
    }

    onMounted(() => {
        window.addEventListener('resize', handleResize)
        window.addEventListener('scroll', handleScroll, true)
    })

    onUnmounted(() => {
        window.removeEventListener('resize', handleResize)
        window.removeEventListener('scroll', handleScroll, true)
    })

    // Watch for dropdown open/close to recalculate position
    const updatePosition = () => {
        if (isOpen.value) {
            // Use nextTick to ensure DOM is updated
            setTimeout(calculatePosition, 0)
        }
    }

    return {
        dropdownStyles,
        dropdownPosition,
        dropdownHeight,
        updatePosition
    }
}