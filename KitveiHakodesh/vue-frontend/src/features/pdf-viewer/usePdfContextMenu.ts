import { ref, type Ref } from 'vue'
import type ContextMenu from '@/components/ContextMenu.vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import { attachScopedCopy, triggerCopy } from '@/composables/useLineCopy'
import { pasteIntoWord, copyImageToClipboard } from '@/webview-host/bridge'

// The page is rendered at the highest scale that still fits within
// MAX_IMAGE_DIMENSION (longest side, px) — PDF pages are vector, so more pixels
// means a sharper copy. 4000px on a letter page is ~360 DPI. The cap also bounds
// canvas memory and the base64 payload sent over the bridge. MAX_IMAGE_SCALE
// guards against pointlessly upscaling a tiny page into a huge raster.
const MAX_IMAGE_DIMENSION = 4000
const MAX_IMAGE_SCALE = 8

// Minimal shape of the bits of PDF.js's global app object we touch. The viewer
// exposes the full object on the iframe's window as `PDFViewerApplication`.
interface PdfViewerApplicationLike {
  pdfDocument: {
    getPage: (pageNumber: number) => Promise<{
      rotate: number
      getViewport: (opts: { scale: number; rotation?: number }) => {
        width: number
        height: number
      }
      render: (opts: { canvasContext: CanvasRenderingContext2D; viewport: unknown }) => {
        promise: Promise<void>
      }
    }>
  } | null
  page: number
  pdfViewer?: { pagesRotation?: number }
}

function getApp(win: Window | null): PdfViewerApplicationLike | null {
  const app = (win as unknown as { PDFViewerApplication?: PdfViewerApplicationLike })
    ?.PDFViewerApplication
  return app && app.pdfDocument ? app : null
}

/**
 * Builds RTL Word-friendly HTML from a plain-text PDF selection. The PDF.js text
 * layer is a soup of absolutely-positioned spans, so we copy the visible text
 * (not its markup) and wrap each line in an RTL div — this is what pastes cleanly
 * into Word, matching the book view's copy behaviour.
 */
function selectionToHtml(text: string): string {
  const lines = text
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l.length > 0)
  if (lines.length === 0) return ''
  return lines.map((l) => `<div dir="rtl">${escapeHtml(l)}</div>`).join('')
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

/**
 * Extracts the selected text from the PDF.js text layer with word gaps intact.
 *
 * sel.toString() is NOT usable here: in OCR'd PDFs the text layer is one span per WORD
 * with no space characters anywhere — the gaps are purely visual (absolute positioning).
 * toString() therefore concatenates whole rows into a single merged word (verified live:
 * a row of 8 word-spans came back with zero spaces). Acrobat copies the same document
 * fine because it reconstructs spacing from glyph geometry; we do the DOM equivalent —
 * walk the selected spans, clip the first/last by the range offsets, and join with a
 * space at span boundaries (and a newline when the row's top offset changes).
 *
 * PDFs whose text layer DOES embed real spaces are unaffected: joining adds a space only
 * when the boundary has none.
 */
function selectionToText(win: Window): string {
  const sel = win.getSelection()
  if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return ''
  const range = sel.getRangeAt(0)

  // Word spans only — NOT every descendant span. The find bar's highlighter nests a
  // <span class="highlight"> INSIDE the word span it matched; that child also
  // intersects the range, so counting it too would emit the matched word twice.
  const spans = (Array.from(win.document.querySelectorAll('.textLayer span')) as HTMLElement[])
    .filter((el) => !el.parentElement?.closest('.textLayer span'))
    .filter((el) => range.intersectsNode(el))
  if (spans.length === 0) return sel.toString()

  /**
   * The part of a span's text actually inside the range (first/last spans may be cut).
   * Clipped via a sub-range clamped to the selection rather than by slicing textContent
   * with the raw range offsets: an offset is only a CHARACTER offset when its container
   * is a text node (for an element container it's a child index), and a find-highlighted
   * span holds several text nodes, making a whole-textContent slice wrong either way.
   * Range.toString() handles both cases and reads nested markup's text exactly once.
   */
  function clippedText(el: HTMLElement): string {
    const sub = win.document.createRange()
    sub.selectNodeContents(el)
    if (sub.compareBoundaryPoints(Range.START_TO_START, range) < 0) {
      sub.setStart(range.startContainer, range.startOffset)
    }
    if (sub.compareBoundaryPoints(Range.END_TO_END, range) > 0) {
      sub.setEnd(range.endContainer, range.endOffset)
    }
    return sub.toString()
  }

  /**
   * Same visual row ⇔ rendered tops within half a line height. Measured from
   * getBoundingClientRect, NOT style.top: PDF.js writes style.top as a PERCENTAGE of the
   * page (rows ~1.5% apart in a live probe) and often sets no inline font-size at all,
   * so no fixed threshold on the style values is unit-safe. Real pixels also make the
   * tolerance self-scaling: OCR word boxes wobble vertically by a pixel or two (exact
   * equality would fabricate a newline between every word), while a genuine line step is
   * at least the line's own height. Spans on different PAGES get distinct viewport tops
   * too, so a cross-page selection breaks lines correctly as a side effect.
   * Known limitation: 90°/270°-rotated pages lay words of one visual line at different
   * tops, so rotated selections degrade to one word per line rather than merging words.
   */
  function sameRow(rect: DOMRect, prevRect: DOMRect): boolean {
    const tolerance = Math.max(2, Math.min(rect.height, prevRect.height) / 2)
    return Math.abs(rect.top - prevRect.top) <= tolerance
  }

  let out = ''
  let prevRect: DOMRect | null = null
  for (const el of spans) {
    const piece = clippedText(el)
    if (!piece) continue
    const rect = el.getBoundingClientRect()
    if (out.length > 0 && prevRect !== null) {
      if (!sameRow(rect, prevRect)) {
        out = out.trimEnd() + '\n'
      } else if (!/\s$/.test(out) && !/^\s/.test(piece)) {
        out += ' '
      }
    }
    out += piece
    prevRect = rect
  }
  return out
}

/**
 * Custom right-click menu for the PDF viewer iframe, mirroring the book view's
 * menu: העתק (copy) and העתק לתוך וורד (copy into Word), plus העתק דף כתמונה
 * (copy the current page to the clipboard as an image).
 *
 * The PDF renders in a same-origin iframe, so we read its selection and its
 * `PDFViewerApplication` directly and render/copy from the parent document —
 * the same access pattern the OCR and theme code already use.
 */
export function usePdfContextMenu(
  getIframe: () => HTMLIFrameElement | null,
  menuRef: Ref<InstanceType<typeof ContextMenu> | null>,
  options: { isBlocked?: () => boolean; notify?: (message: string) => void } = {},
) {
  const items = ref<ContextMenuItem[]>([])

  // Selection captured at the moment the menu opened (right-click never clears it).
  let capturedText = ''
  // The live Range behind capturedText, cloned at menu-open time. ContextMenu.vue does
  // exactly this for in-page menus, but its save/restore reads window.getSelection() —
  // the PARENT window — so it saves nothing for a selection living in the iframe. We
  // keep the iframe-side range ourselves and restore it before copying.
  let capturedRange: Range | null = null
  let capturedWin: Window | null = null

  function captureSelection(win: Window): void {
    capturedWin = win
    const sel = win.getSelection()
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) {
      capturedText = ''
      capturedRange = null
      return
    }
    capturedText = selectionToText(win)
    capturedRange = sel.getRangeAt(0).cloneRange()
  }

  /**
   * Re-selects the captured range inside the iframe and focuses that window, then fires
   * the copy there. Both steps are required: execCommand('copy') acts on the FOCUSED
   * document's selection, and clicking our menu (which lives in the parent document)
   * moves focus out of the iframe. Without the focus() the command is a no-op — the
   * original bug, where "העתק" did nothing and "העתק לתוך וורד" pasted whatever the
   * user had copied earlier.
   *
   * Returns whether the clipboard was actually written.
   */
  function copyCapturedSelection(afterCopy?: () => void): boolean {
    const win = capturedWin
    if (!win || !capturedRange || !selectionToHtml(capturedText)) return false

    const sel = win.getSelection()
    if (!sel) return false
    sel.removeAllRanges()
    sel.addRange(capturedRange)
    win.focus()

    // The copy listener attached to the iframe document rewrites the payload into the
    // RTL Word-friendly shape; execCommand alone would copy PDF.js's span soup.
    return triggerCopy(afterCopy, win.document)
  }

  function onCopy(): void {
    copyCapturedSelection()
  }

  function onCopyIntoWord(): void {
    // pasteIntoWord runs INSIDE the copy event, after clipboardData is written — the
    // same guarantee book view relies on. If the copy never happens the callback never
    // fires, so Word is never told to paste a stale clipboard.
    const copied = copyCapturedSelection(() => {
      pasteIntoWord().catch(() => {})
    })
    if (!copied) options.notify?.('לא ניתן היה להעתיק את הטקסט שנבחר.')
  }

  async function copyPageAsImage(): Promise<void> {
    const win = getIframe()?.contentWindow ?? null
    const app = getApp(win)
    if (!app || !app.pdfDocument) {
      options.notify?.('לא נטען קובץ PDF.')
      return
    }
    const pageNumber = app.page || 1
    let canvas: HTMLCanvasElement
    try {
      const page = await app.pdfDocument.getPage(pageNumber)
      const userRotation = app.pdfViewer?.pagesRotation || 0
      const rotation = (page.rotate + userRotation) % 360

      // Pick the largest scale whose longest side stays within the cap.
      const base = page.getViewport({ scale: 1, rotation })
      const longestAt1 = Math.max(base.width, base.height) || 1
      const scale = Math.min(MAX_IMAGE_SCALE, MAX_IMAGE_DIMENSION / longestAt1)
      const viewport = page.getViewport({ scale, rotation })

      canvas = document.createElement('canvas')
      canvas.width = Math.ceil(viewport.width)
      canvas.height = Math.ceil(viewport.height)
      const ctx = canvas.getContext('2d', { alpha: false })
      if (!ctx) throw new Error('no 2d context')
      ctx.fillStyle = '#ffffff'
      ctx.fillRect(0, 0, canvas.width, canvas.height)

      await page.render({ canvasContext: ctx, viewport }).promise
    } catch (err) {
      console.error('[PdfContextMenu] render failed', err)
      options.notify?.('לא ניתן היה לצייר את הדף.')
      return
    }

    // Hosted MUST go through C#: the host serves the app from
    // http://KitveiHakodesh-vue-app/ — plain http on a non-localhost hostname, which Chromium
    // treats as an INSECURE context, so navigator.clipboard isn't exposed there at all.
    // Dev is served from http://localhost, which IS a secure context, so the browser API below
    // works and there is nothing for C# to do — skip the call rather than issue one that can
    // only reject (the service has no clipboard-image op).
    if (typeof window.__webviewAction === 'function') {
      try {
        const res = await copyImageToClipboard(canvas.toDataURL('image/png'))
        if (res?.ok) {
          options.notify?.(`דף ${pageNumber} הועתק כתמונה.`)
          return
        }
      } catch {
        /* host bridge failed — try the browser clipboard below */
      }
    }

    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/png'))
    if (!blob) {
      options.notify?.('לא ניתן היה ליצור את התמונה.')
      return
    }

    if (window.ClipboardItem && navigator.clipboard?.write) {
      try {
        await navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })])
        options.notify?.(`דף ${pageNumber} הועתק כתמונה.`)
        return
      } catch (err) {
        console.warn('[PdfContextMenu] clipboard image write failed, downloading', err)
      }
    }
    // Last resort: download the PNG if neither clipboard path is available.
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `page-${pageNumber}.png`
    document.body.append(a)
    a.click()
    a.remove()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
    options.notify?.(`הלוח אינו זמין — דף ${pageNumber} נשמר כתמונה.`)
  }

  function buildItems(hasSelection: boolean): ContextMenuItem[] {
    const list: ContextMenuItem[] = []
    if (hasSelection) {
      list.push({ label: 'העתק', action: onCopy, shortcut: 'Ctrl+C' })
      list.push({ label: 'העתק לתוך וורד', action: onCopyIntoWord })
      list.push({ type: 'separator' })
    }
    list.push({ label: 'העתק דף כתמונה', action: () => void copyPageAsImage() })
    return list
  }

  function onContextMenu(event: MouseEvent): void {
    if (options.isBlocked?.()) return
    const iframe = getIframe()
    const win = iframe?.contentWindow
    if (!iframe || !win) return

    // Only inside the page-viewing area. This listener is on the iframe
    // DOCUMENT (capture phase, so it beats PDF.js's own handlers), which means
    // it also sees right-clicks on the side panel, the toolbars and the find
    // bar — where "העתק דף כתמונה" is meaningless and, worse, it suppressed
    // the outline panel's own row menu. #viewerContainer is the scroll area
    // holding the rendered pages.
    const target = event.target as Element | null
    if (!target?.closest?.('#viewerContainer')) return

    event.preventDefault()
    captureSelection(win)
    items.value = buildItems(capturedText.trim().length > 0)

    // The event's coordinates are relative to the iframe viewport; the menu is
    // teleported to the parent body (position: fixed), so offset by the iframe's
    // position within the parent document.
    const rect = iframe.getBoundingClientRect()
    menuRef.value?.showAtPosition(rect.left + event.clientX, rect.top + event.clientY)
  }

  let detachCopy: (() => void) | null = null

  function attach(win: Window): void {
    // Capture phase so we intercept the right-click before PDF.js's own handlers
    // and reliably suppress the browser's native context menu.
    win.document.addEventListener('contextmenu', onContextMenu, true)

    // Intercept copies inside the iframe and rewrite the payload into the RTL
    // Word-friendly shape. This serves BOTH our menu's העתק and the user's own Ctrl+C
    // on the PDF text layer — matching book view, where every copy path funnels through
    // the same event handler.
    detachCopy = attachScopedCopy(win.document, () => {
      // selectionToText, not sel.toString(): OCR'd text layers carry no space chars, so
      // toString() merges every word in a row (see selectionToText).
      const live = selectionToText(win)
      return selectionToHtml(live || capturedText) || null
    })
  }

  function detach(win: Window | null): void {
    win?.document.removeEventListener('contextmenu', onContextMenu, true)
    detachCopy?.()
    detachCopy = null
    capturedRange = null
    capturedWin = null
    menuRef.value?.hide()
  }

  return { items, attach, detach }
}
