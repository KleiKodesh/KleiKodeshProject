import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'

function wrapRtlHtml(innerHtml: string): string {
  return `<!DOCTYPE html><html><head><meta charset="utf-8">
<style>body { direction: rtl; }</style></head><body>
${innerHtml}
</body></html>`
}

function htmlToPlainText(html: string): string {
  const tempDiv = document.createElement('div')
  tempDiv.innerHTML = html
  return tempDiv.textContent ?? ''
}

/**
 * Set to true while execCopyHtmlToClipboard is running document.execCommand('copy').
 * In Chromium/WebView2, execCommand fires the copy event with isTrusted=true, so we
 * cannot use event.isTrusted to distinguish programmatic from user-initiated copies.
 * This flag lets useScopedCopy skip the programmatic event and avoid double-processing
 * (which would apply source decorations twice).
 * Reset is deferred via setTimeout because in Chromium/WebView2 the copy event fires
 * asynchronously — after the synchronous finally block — so the flag must stay true
 * until the next event loop turn.
 */
let _isProgrammaticCopy = false

/**
 * Copies formatted HTML to the clipboard by placing it in a hidden off-screen RTL
 * container, selecting it, and calling execCommand('copy').
 * Sets _isProgrammaticCopy so useScopedCopy skips the resulting copy event.
 */
export function execCopyHtmlToClipboard(html: string): void {
  const container = document.createElement('div')
  container.setAttribute('dir', 'rtl')
  container.style.position = 'fixed'
  container.style.left = '-9999px'
  container.style.top = '-9999px'
  container.innerHTML = html
  document.body.appendChild(container)

  const selection = window.getSelection()
  const range = document.createRange()
  range.selectNodeContents(container)
  selection?.removeAllRanges()
  selection?.addRange(range)

  try {
    _isProgrammaticCopy = true
    document.execCommand('copy')
  } finally {
    setTimeout(() => { _isProgrammaticCopy = false }, 0)
    selection?.removeAllRanges()
    document.body.removeChild(container)
  }
}

/**
 * Intercepts the native browser copy event on a scroller element and applies the
 * active copy format settings via buildFormattedHtml.
 *
 * Flow for all copy paths (menu, Ctrl+C):
 *   1. Copy action fires document.execCommand('copy')
 *   2. Browser dispatches copy event on the focused element
 *   3. This handler intercepts it, calls buildFormattedHtml to apply all active flags
 *      (copyAsBlob, copySourcePosition, copyWithNotes, copyCleanText)
 *   4. Writes the result to event.clipboardData and calls event.preventDefault()
 *
 * Programmatic copies from execCopyHtmlToClipboard are skipped via _isProgrammaticCopy.
 * When buildFormattedHtml returns null (no selection), the event is not intercepted.
 */
export function useScopedCopy(
  scrollerEl: Ref<HTMLElement | null>,
  getLines: () => string[],
  isSelectAll: Ref<boolean>,
  buildFormattedHtml?: () => string | null,
) {
  useEventListener(scrollerEl, 'copy', (event: ClipboardEvent) => {
    if (_isProgrammaticCopy) return
    if (!buildFormattedHtml) return

    const formatted = buildFormattedHtml()
    if (formatted === null) return

    event.clipboardData?.setData('text/html', wrapRtlHtml(formatted))
    event.clipboardData?.setData('text/plain', htmlToPlainText(formatted))
    event.preventDefault()
  })

  useEventListener(scrollerEl, 'dragstart', (event: DragEvent) => {
    if (!buildFormattedHtml) return
    const formatted = buildFormattedHtml()
    if (formatted === null) return
    event.dataTransfer?.setData('text/html', wrapRtlHtml(formatted))
    event.dataTransfer?.setData('text/plain', htmlToPlainText(formatted))
  })
}
