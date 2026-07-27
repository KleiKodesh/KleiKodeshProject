import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'

/**
 * True when the payload is a SINGLE inline run — one <span> wrapping everything,
 * which is what the copyJoinLines and quotation branches emit. Those are the cases
 * that must paste without a paragraph break; anything else (per-line <div> blocks,
 * an <h2> source heading, appended endnotes) is genuinely block content.
 *
 * Deliberately conservative: it requires the string to both open and close with the
 * same single span, so a payload that merely *starts* with a span (e.g. a run plus
 * an endnotes block) is correctly treated as block content.
 */
function isSingleInlineRun(html: string): boolean {
  const trimmed = html.trim()
  if (!trimmed.startsWith('<span') || !trimmed.endsWith('</span>')) return false
  // Reject multiple top-level spans: strip the outer pair and ensure no span closes
  // at the top level in between (a nested <b>/<a> is fine, a sibling </span> is not).
  const inner = trimmed.slice(trimmed.indexOf('>') + 1, -'</span>'.length)
  let depth = 0
  for (const m of inner.matchAll(/<(\/?)span\b/gi)) {
    depth += m[1] ? -1 : 1
    if (depth < 0) return false
  }
  return depth === 0
}

/**
 * Wraps clipboard HTML for Word.
 *
 * Word decides "inline run" vs "whole-document import" from the CF_HTML fragment
 * markers, NOT from the payload's tags. Chromium generates the CF_HTML header and
 * places <!--StartFragment-->/<!--EndFragment--> around whatever string we pass to
 * setData('text/html', …) — so handing it a full <!DOCTYPE html> document gets the
 * document fragment-marked as a document, and Word terminates the final paragraph.
 * That trailing paragraph break is what "העתק כרצף" was still producing.
 *
 * Verified against real Word (COM, PasteAndFormat(20)), pasting between sentinels:
 *   <span> in a document wrapper → 2 paragraphs (BEFORE|text¶|AFTER)
 *   <div>  in a document wrapper → 2 paragraphs
 *   <div>  fragment-marked       → 2 paragraphs
 *   <span> fragment-marked       → 1 paragraph  (BEFORE|text|AFTER)
 * Both conditions are required: an inline outer element AND no document wrapper.
 *
 * So a single inline run is emitted bare, carrying its RTL direction on the element's
 * own dir attribute. The dropped <style>body{direction:rtl}</style> was not pulling
 * weight: it is a `body` selector with no body to match once unwrapped, and the
 * pasteIntoWord path uses Merge Formatting, which discards imported formatting
 * anyway (see WordExporter.PasteAtCursorCore).
 *
 * Block payloads keep the document wrapper — they SHOULD paste as paragraphs (one
 * per line with copyJoinLines off). No whitespace around the payload there either:
 * a leading/trailing whitespace text node becomes its own empty paragraph.
 */
export function wrapRtlHtml(innerHtml: string): string {
  const trimmed = innerHtml.trim()
  if (isSingleInlineRun(trimmed)) {
    return trimmed.startsWith('<span dir=') ? trimmed : trimmed.replace(/^<span/i, '<span dir="rtl"')
  }
  return `<!DOCTYPE html><html dir="rtl"><head><meta charset="utf-8">` +
    `<style>body{direction:rtl}</style></head><body>${trimmed}</body></html>`
}

/** Trimmed so the text/plain flavor doesn't carry a trailing newline either. */
export function htmlToPlainText(html: string): string {
  const tempDiv = document.createElement('div')
  tempDiv.innerHTML = html
  return (tempDiv.textContent ?? '').trim()
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
 * Callback registered by the current copy action handler. When set, useScopedCopy
 * calls this after writing to the clipboard — used by onPasteIntoWord to fire the
 * bridge action only after the clipboard write is confirmed complete.
 */
let _afterCopyCallback: (() => void) | null = null

/**
 * Triggers document.execCommand('copy') so that useScopedCopy intercepts the copy
 * event and writes the formatted HTML to the clipboard. If afterCopy is provided,
 * it is called once inside the copy event handler after the clipboard write, before
 * the event handler returns — guaranteeing the clipboard is set before the callback runs.
 */
export function triggerCopy(afterCopy?: () => void): void {
  _afterCopyCallback = afterCopy ?? null
  document.execCommand('copy')
}

/**
 * Intercepts the native browser copy event on a scroller element and applies the
 * active copy format settings via buildFormattedHtml.
 *
 * Flow for all copy paths (menu, Ctrl+C):
 *   1. Copy action fires document.execCommand('copy') via triggerCopy()
 *   2. Browser dispatches copy event on the focused element
 *   3. This handler intercepts it, calls buildFormattedHtml to apply all active flags
 *      (copyJoinLines, copySourcePosition, copyWithNotes, copyCleanText)
 *   4. Writes the result to event.clipboardData and calls event.preventDefault()
 *   5. If an afterCopy callback was registered via triggerCopy(), calls it now —
 *      the clipboard is guaranteed to be set at this point.
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

    const callback = _afterCopyCallback
    _afterCopyCallback = null
    callback?.()
  })

  useEventListener(scrollerEl, 'dragstart', (event: DragEvent) => {
    if (!buildFormattedHtml) return
    const formatted = buildFormattedHtml()
    if (formatted === null) return
    event.dataTransfer?.setData('text/html', wrapRtlHtml(formatted))
    event.dataTransfer?.setData('text/plain', htmlToPlainText(formatted))
  })
}
