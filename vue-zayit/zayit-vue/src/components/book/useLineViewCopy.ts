/**
 * Line View Copy Handler Composable
 * Handles copying full source content from virtual scroller
 */

import { useEventListener } from '@vueuse/core'
import type { Ref, ComputedRef } from 'vue'
import type { BookLineViewerService } from '@/data/services/bookLineViewerService'
import type { Tab } from '@/data/types/Tab'
import { transformText } from '@/components/shared/useTextTransformations'

export function useLineViewCopy(
    scrollerElRef: Ref<HTMLElement | undefined>,
    viewerState: BookLineViewerService,
    myTab: ComputedRef<Tab | undefined>
) {
    function handleCopy(event: ClipboardEvent) {
        const selection = window.getSelection()
        if (!selection || selection.rangeCount === 0) {
            return
        }

        const scrollerEl = scrollerElRef.value
        if (!scrollerEl) {
            return
        }

        const range = selection.getRangeAt(0)
        if (!scrollerEl.contains(range.commonAncestorContainer)) {
            return
        }

        // Check if user has selected all content (Ctrl+A was used)
        const isFullSelection = range.startContainer === scrollerEl ||
            scrollerEl.contains(range.startContainer) &&
            scrollerEl.contains(range.endContainer) &&
            range.toString().length > scrollerEl.textContent!.length * 0.95

        if (!isFullSelection) {
            return // Let browser handle partial selection copy
        }

        // Get all source lines as HTML
        const lines = viewerState.lines.value
        const diacriticsState = myTab.value?.bookState?.diacriticsState

        let htmlContent = ''
        let textContent = ''

        for (let i = 0; i < viewerState.totalLines.value; i++) {
            let line = lines[i] || '\u00A0'

            // Apply unified transformations (diacritics, search will be added later)
            line = transformText(line, {
                diacriticsState
            })

            htmlContent += `<div>${line}</div>\n`

            // For plain text, strip HTML tags
            const tempDiv = document.createElement('div')
            tempDiv.innerHTML = line
            textContent += (tempDiv.textContent || tempDiv.innerText || '') + '\n'
        }

        // Wrap in full HTML document with RTL body
        htmlContent = `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
body { direction: rtl; font-weight: normal; }
</style>
</head>
<body>
${htmlContent}
</body>
</html>`

        // Set clipboard data
        event.clipboardData?.setData('text/html', htmlContent)
        event.clipboardData?.setData('text/plain', textContent)
        event.preventDefault()
    }

    // Set up copy event listener
    useEventListener(scrollerElRef, 'copy', handleCopy)
}
