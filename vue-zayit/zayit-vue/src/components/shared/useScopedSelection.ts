/**
 * Scoped Selection Composable
 * Provides scoped Ctrl+A selection within a container
 * Reusable across any component that needs container-scoped selection
 */

import { type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'

export function useScopedSelection(containerRef: Ref<HTMLElement | undefined | null>) {
    /**
     * Select all content within the container
     */
    function selectAllInContainer() {
        const container = containerRef.value
        if (!container) return

        const selection = window.getSelection()
        if (!selection) return

        const range = document.createRange()
        range.selectNodeContents(container)
        selection.removeAllRanges()
        selection.addRange(range)
    }

    /**
     * Handle Ctrl+A to select all text in the container only (scoped)
     * Only triggers when the container has focus
     */
    useEventListener('keydown', (event: KeyboardEvent) => {
        const hasCtrlOrMeta = event.ctrlKey || event.metaKey

        // Ctrl+A: Select all in container (scoped to this container only)
        if (hasCtrlOrMeta && event.code === 'KeyA') {
            const container = containerRef.value
            if (container && document.activeElement === container) {
                event.preventDefault()
                selectAllInContainer()
            }
        }
    })

    return {
        selectAllInContainer
    }
}
