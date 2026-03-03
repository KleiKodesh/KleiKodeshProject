/**
 * Toolbar Position Composable
 * Handles toolbar positioning and dragging
 */

import { ref, computed, type Ref } from 'vue'
import { useDraggable, onClickOutside } from '@vueuse/core'
import { useTabStore } from '@/data/stores/tabStore'

export function useToolbarPosition(
    toolbarRef: Ref<HTMLElement | undefined>,
    dragHandleRef: Ref<HTMLElement | undefined>
) {
    const tabStore = useTabStore()
    const showPositionDropdown = ref(false)
    const positionSelectorRef = ref<HTMLElement>()

    const toolbarPosition = computed(() => {
        return tabStore.activeTab?.bookState?.toolbarPosition || 'top'
    })

    const isFloating = computed(() =>
        toolbarPosition.value === 'float-vertical' || toolbarPosition.value === 'float-horizontal'
    )

    // Drag functionality
    const { isDragging } = useDraggable(toolbarRef, {
        handle: dragHandleRef,
        preventDefault: true,
        stopPropagation: true,
        disabled: computed(() => !isFloating.value),
        initialValue: computed(() => {
            const tab = tabStore.activeTab
            if (isFloating.value && tab?.bookState?.toolbarFloatX !== undefined && tab?.bookState?.toolbarFloatY !== undefined) {
                return { x: tab.bookState.toolbarFloatX, y: tab.bookState.toolbarFloatY }
            }
            return { x: 100, y: 100 }
        }),
        onMove: (position) => {
            if (!isFloating.value) return
            const tab = tabStore.activeTab
            if (tab?.bookState) {
                tab.bookState.toolbarFloatX = position.x
                tab.bookState.toolbarFloatY = position.y
            }
        },
        onEnd: (position) => {
            if (!isFloating.value) return
            const tab = tabStore.activeTab
            if (tab?.bookState) {
                tab.bookState.toolbarFloatX = position.x
                tab.bookState.toolbarFloatY = position.y
            }
        }
    })

    const adjustedDragStyle = computed(() => {
        if (!isFloating.value) return undefined

        const tab = tabStore.activeTab
        const x = tab?.bookState?.toolbarFloatX ?? 100
        const y = tab?.bookState?.toolbarFloatY ?? 100

        return {
            position: 'fixed' as const,
            left: `${x}px`,
            top: `${y}px`,
            margin: '0',
            zIndex: '1000',
        }
    })

    onClickOutside(positionSelectorRef, () => {
        showPositionDropdown.value = false
    })

    const togglePositionDropdown = () => {
        showPositionDropdown.value = !showPositionDropdown.value
    }

    const setToolbarPosition = (position: 'top' | 'bottom' | 'left' | 'right' | 'float-vertical' | 'float-horizontal') => {
        const tab = tabStore.activeTab
        if (tab?.bookState) {
            tab.bookState.toolbarPosition = position
        }
        showPositionDropdown.value = false
    }

    return {
        toolbarPosition,
        isFloating,
        isDragging,
        adjustedDragStyle,
        showPositionDropdown,
        positionSelectorRef,
        togglePositionDropdown,
        setToolbarPosition
    }
}
