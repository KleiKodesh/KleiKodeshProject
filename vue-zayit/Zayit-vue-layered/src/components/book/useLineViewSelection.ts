/**
 * Line View Selection Management Composable
 * Handles text selection, Ctrl+A, and copy operations
 */

import { ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'

export function useLineViewSelection(scrollerElRef: Ref<HTMLElement | undefined>) {
    const selectAllWasPressed = ref(false)

    // Reset selectAll flag when selection changes or user interacts
    useEventListener(scrollerElRef, 'mousedown', () => {
        selectAllWasPressed.value = false
    })

    useEventListener(document, 'selectionchange', () => {
        const selection = window.getSelection()
        if (!selection || selection.toString().length === 0) {
            selectAllWasPressed.value = false
        }
    })

    // Handle Ctrl+A to select all text in the container
    useEventListener('keydown', (event: KeyboardEvent) => {
        const hasCtrlOrMeta = event.ctrlKey || event.metaKey

        // Ctrl+A: Select all in container
        if (hasCtrlOrMeta && event.code === 'KeyA') {
            const scrollerEl = scrollerElRef.value
            if (scrollerEl && document.activeElement === scrollerEl) {
                event.preventDefault()
                selectAllInContainer(scrollerEl)
                selectAllWasPressed.value = true
            }
        }
    })

    function selectAllInContainer(scrollerEl: HTMLElement) {
        const selection = window.getSelection()
        if (!selection) return

        const range = document.createRange()
        range.selectNodeContents(scrollerEl)
        selection.removeAllRanges()
        selection.addRange(range)
    }

    return {
        selectAllWasPressed,
        selectAllInContainer
    }
}
