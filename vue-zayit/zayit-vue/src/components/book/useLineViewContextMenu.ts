/**
 * Line View Context Menu Composable
 * Handles context menu for copy operations
 */

import { computed, type Ref } from 'vue'
import type { ContextMenuItem } from '@/components/shared/useContextMenu'
import { useScopedSelection } from '@/components/shared/useScopedSelection'

export function useLineViewContextMenu(scrollerElRef: Ref<HTMLElement | undefined>) {
    const { selectAllInContainer } = useScopedSelection(scrollerElRef)

    const contextMenuItems = computed<ContextMenuItem[]>(() => [
        {
            label: 'העתק',
            action: handleCopyFromContextMenu
        },
        {
            label: 'העתק כבלוק',
            action: handleCopyAsBlockFromContextMenu
        },
        {
            label: 'בחר הכל',
            action: selectAllInContainer
        }
    ])

    function handleCopyFromContextMenu() {
        document.execCommand('copy')
    }

    function handleCopyAsBlockFromContextMenu() {
        const selection = window.getSelection()
        if (!selection) return

        // Get selected text
        const selectedText = selection.toString()
        if (!selectedText.trim()) return

        // Replace line breaks with spaces to create a continuous block
        const blockText = selectedText.replace(/\n+/g, ' ').replace(/\s+/g, ' ').trim()

        // Copy to clipboard
        navigator.clipboard.writeText(blockText).catch(err => {
            console.error('Failed to copy text as block:', err)
        })
    }

    return {
        contextMenuItems,
        handleCopyFromContextMenu,
        handleCopyAsBlockFromContextMenu
    }
}
