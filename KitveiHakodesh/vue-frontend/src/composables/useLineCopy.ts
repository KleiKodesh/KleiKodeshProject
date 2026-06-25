import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import { useSettingsStore } from '@/stores/settingsStore'

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

function linesToHtml(lines: string[]): string {
  return lines.map((l) => `<div>${l}</div>`).join('\n')
}

function selectedHtml(): string {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0) return ''
  const range = selection.getRangeAt(0)
  const fragment = range.cloneContents()
  const container = document.createElement('div')
  container.appendChild(fragment)
  return container.innerHTML
}

function stripNoteMarkers(html: string): string {
  return html.replace(/<sup[^>]*class="user-note-marker"[^>]*>.*?<\/sup>/gs, '')
}

/**
 * Set to true while execCopyHtml is executing document.execCommand('copy') so that
 * the useScopedCopy event listener can ignore the programmatic copy event and avoid
 * double-processing (which would append source decorations twice).
 */
let _isProgrammaticCopy = false

/**
 * Copy HTML to the clipboard by placing it in a hidden off-screen RTL container,
 * selecting it, and calling execCommand('copy').
 *
 * Sets _isProgrammaticCopy = true before the execCommand call so that any
 * useScopedCopy listener on the same element skips re-processing.
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
    // Reset asynchronously — in Chromium/WebView2 the copy event from execCommand
    // may fire after the synchronous finally block, so delay the flag reset.
    setTimeout(() => { _isProgrammaticCopy = false }, 0)
    selection?.removeAllRanges()
    document.body.removeChild(container)
  }
}

/**
 * Intercepts the native browser copy event on a scroller element and applies the
 * active copy format settings (copyCleanText, copyAsBlock, copySourcePosition).
 *
 * When buildFormattedHtml is provided, it is called instead of the default raw-HTML
 * path so that all copy paths — native Ctrl+C, menu copy, and paste-to-Word — use
 * the same formatting logic.
 *
 * When buildFormattedHtml returns null (no selection), the event is not intercepted
 * and the browser handles the copy natively.
 */
export function useScopedCopy(
  scrollerEl: Ref<HTMLElement | null>,
  getLines: () => string[],
  isSelectAll: Ref<boolean>,
  buildFormattedHtml?: () => string | null,
) {
  const settingsStore = useSettingsStore()

  useEventListener(scrollerEl, 'copy', (event: ClipboardEvent) => {
    // Skip programmatic copy events triggered by execCopyHtmlToClipboard.
    // In Chromium/WebView2, execCommand('copy') fires with isTrusted=true so
    // we use the explicit _isProgrammaticCopy flag instead.
    if (_isProgrammaticCopy) return

    let innerHtml: string

    if (buildFormattedHtml) {
      const formatted = buildFormattedHtml()
      if (formatted === null) return // no selection — let browser handle it
      innerHtml = formatted
    } else {
      const raw = isSelectAll.value ? linesToHtml(getLines()) : selectedHtml()
      if (!raw.trim()) return
      innerHtml = stripNoteMarkers(raw)
      if (settingsStore.copyCleanText) innerHtml = cleanHebrewText(innerHtml)
    }

    const htmlContent = wrapRtlHtml(innerHtml)
    const plainText = htmlToPlainText(innerHtml)

    event.clipboardData?.setData('text/html', htmlContent)
    event.clipboardData?.setData('text/plain', plainText)
    event.preventDefault()
  })

  useEventListener(scrollerEl, 'dragstart', (event: DragEvent) => {
    let innerHtml: string

    if (buildFormattedHtml) {
      const formatted = buildFormattedHtml()
      if (formatted === null) return
      innerHtml = formatted
    } else {
      const raw = isSelectAll.value ? linesToHtml(getLines()) : selectedHtml()
      if (!raw.trim()) return
      innerHtml = stripNoteMarkers(raw)
      if (settingsStore.copyCleanText) innerHtml = cleanHebrewText(innerHtml)
    }

    const htmlContent = wrapRtlHtml(innerHtml)
    const plainText = htmlToPlainText(innerHtml)

    event.dataTransfer?.setData('text/html', htmlContent)
    event.dataTransfer?.setData('text/plain', plainText)
  })
}
