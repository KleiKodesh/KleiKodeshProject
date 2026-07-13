import { ref, type Ref } from 'vue'
import type ContextMenu from '@/components/ContextMenu.vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import { execCopyHtmlToClipboard } from '@/composables/useLineCopy'
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

  function captureSelectionText(win: Window): string {
    const sel = win.getSelection()
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return ''
    return sel.toString()
  }

  function onCopy(): void {
    const html = selectionToHtml(capturedText)
    if (!html) return
    execCopyHtmlToClipboard(html)
  }

  function onCopyIntoWord(): void {
    const html = selectionToHtml(capturedText)
    if (!html) return
    // Set the clipboard synchronously, then ask C# to paste from it — same
    // ordering the book view relies on (clipboard first, bridge second).
    execCopyHtmlToClipboard(html)
    pasteIntoWord().catch(() => {})
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

    // Preferred path: let the C# host set the clipboard. The browser's
    // navigator.clipboard.write() for images is blocked inside WebView2, which is
    // why the old path fell through to a download. copyImageToClipboard rejects
    // when there's no bridge (dev/browser), so we fall back to the browser API.
    try {
      const res = await copyImageToClipboard(canvas.toDataURL('image/png'))
      if (res?.ok) {
        options.notify?.(`דף ${pageNumber} הועתק כתמונה.`)
        return
      }
    } catch {
      /* no host bridge — try the browser clipboard below */
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

    event.preventDefault()
    capturedText = captureSelectionText(win)
    items.value = buildItems(capturedText.trim().length > 0)

    // The event's coordinates are relative to the iframe viewport; the menu is
    // teleported to the parent body (position: fixed), so offset by the iframe's
    // position within the parent document.
    const rect = iframe.getBoundingClientRect()
    menuRef.value?.showAtPosition(rect.left + event.clientX, rect.top + event.clientY)
  }

  function attach(win: Window): void {
    // Capture phase so we intercept the right-click before PDF.js's own handlers
    // and reliably suppress the browser's native context menu.
    win.document.addEventListener('contextmenu', onContextMenu, true)
  }

  function detach(win: Window | null): void {
    win?.document.removeEventListener('contextmenu', onContextMenu, true)
    menuRef.value?.hide()
  }

  return { items, attach, detach }
}
