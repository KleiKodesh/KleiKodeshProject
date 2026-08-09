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
 * Callback registered by the current copy action handler. When set, useScopedCopy
 * calls this after writing to the clipboard — used by onPasteIntoWord to fire the
 * bridge action only after the clipboard write is confirmed complete.
 */
let _afterCopyCallback: (() => void) | null = null

/**
 * Triggers execCommand('copy') so that useScopedCopy intercepts the copy
 * event and writes the formatted HTML to the clipboard. If afterCopy is provided,
 * it is called once inside the copy event handler after the clipboard write, before
 * the event handler returns — guaranteeing the clipboard is set before the callback runs.
 *
 * `targetDoc` selects WHICH document runs the command. execCommand('copy') acts on the
 * focused document and its selection, so a caller whose selection lives in a same-origin
 * iframe (the PDF viewer) must pass that iframe's document — running it on the parent
 * would copy nothing and, worse, leave a stale clipboard for an afterCopy callback to
 * paste. Defaults to the parent document for every in-page caller.
 *
 * Returns execCommand's own success flag so callers can gate follow-up work (e.g. only
 * asking C# to paste into Word once the clipboard write actually happened).
 */
export function triggerCopy(afterCopy?: () => void, targetDoc: Document = document): boolean {
  _afterCopyCallback = afterCopy ?? null
  try {
    return targetDoc.execCommand('copy')
  } finally {
    // A copy that never fired the event leaves the callback armed; clear it so it can
    // never run against a later, unrelated copy.
    _afterCopyCallback = null
  }
}

/**
 * The body shared by useScopedCopy and attachScopedCopy: format the current selection,
 * write both flavors onto the event, and run any armed afterCopy callback. Kept in one
 * place so the in-page and iframe copy paths can never drift apart.
 *
 * Flow for all copy paths (menu, Ctrl+C):
 *   1. Copy action fires execCommand('copy') via triggerCopy()
 *   2. Browser dispatches copy event on the focused element
 *   3. This handler intercepts it, calls buildFormattedHtml to apply all active flags
 *      (copyJoinLines, copySourcePosition, copyWithNotes, copyCleanText)
 *   4. Writes the result to event.clipboardData and calls event.preventDefault()
 *   5. If an afterCopy callback was registered via triggerCopy(), calls it now —
 *      the clipboard is guaranteed to be set at this point.
 *
 * When buildFormattedHtml returns null (no selection), the event is not intercepted.
 */
function handleScopedCopyEvent(
  event: ClipboardEvent,
  buildFormattedHtml?: () => string | null,
): void {
  if (!buildFormattedHtml) return

  const formatted = buildFormattedHtml()
  if (formatted === null) return

  event.clipboardData?.setData('text/html', wrapRtlHtml(formatted))
  event.clipboardData?.setData('text/plain', htmlToPlainText(formatted))
  event.preventDefault()

  const callback = _afterCopyCallback
  _afterCopyCallback = null
  callback?.()
}

/**
 * useScopedCopy for a document that Vue does not own — specifically the PDF viewer's
 * same-origin iframe, whose lifecycle is driven by iframe load/unload rather than by a
 * component ref, so useEventListener has nothing to bind to.
 *
 * Binding the listener to the iframe DOCUMENT (not an element inside it) matters: PDF.js
 * rebuilds its text layer as pages virtualize in and out, so any element we captured at
 * attach time would be replaced out from under us. The document survives.
 *
 * CAPTURE PHASE is required, for the same reason the PDF context-menu handler uses it:
 * PDF.js installs its own copy handler on the text layer and calls stopPropagation(), so
 * the event never reaches a bubble-phase document listener. Verified live in Chromium —
 * a bubble listener on the iframe document never fired, and the clipboard ended up with
 * PDF.js's default plain-text-only payload (no text/html flavor for Word to import).
 *
 * Returns the detach function.
 */
export function attachScopedCopy(
  targetDoc: Document,
  buildFormattedHtml: () => string | null,
): () => void {
  const onCopy = (event: Event) => handleScopedCopyEvent(event as ClipboardEvent, buildFormattedHtml)
  targetDoc.addEventListener('copy', onCopy, true)
  return () => targetDoc.removeEventListener('copy', onCopy, true)
}

export function useScopedCopy(
  scrollerEl: Ref<HTMLElement | null>,
  getLines: () => string[],
  isSelectAll: Ref<boolean>,
  buildFormattedHtml?: () => string | null,
) {
  useEventListener(scrollerEl, 'copy', (event: ClipboardEvent) => {
    handleScopedCopyEvent(event, buildFormattedHtml)
  })

  useEventListener(scrollerEl, 'dragstart', (event: DragEvent) => {
    if (!buildFormattedHtml) return
    const formatted = buildFormattedHtml()
    if (formatted === null) return
    event.dataTransfer?.setData('text/html', wrapRtlHtml(formatted))
    event.dataTransfer?.setData('text/plain', htmlToPlainText(formatted))
  })
}
