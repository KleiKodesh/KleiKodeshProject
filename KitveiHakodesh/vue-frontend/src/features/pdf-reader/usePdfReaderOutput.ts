import { t } from '@/i18n'
import { showToast } from '@/composables/useToast'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { copyImageToClipboard } from '@/webview-host/bridge'
import type { PdfScroller } from './usePdfScroller'

// Copy-as-image: the largest scale whose longest side stays within 4000px (~360 DPI on letter), as the old viewer.
const MAX_IMAGE_DIMENSION = 4000
const MAX_IMAGE_SCALE = 8

/**
 * What leaves the viewer as an image: copy the current page as an image.
 * Saving: usePdfSave. Printing: the file itself through the host (PdfReaderView printDocument).
 */
export function usePdfReaderOutput(pdf: PdfScroller, fileName: () => string | null) {
  const hostWindow = useHostWindow()

  /** Render one page (with the reader's rotation) onto a white canvas in the host document. */
  async function renderPage(pageNumber: number, scaleFor: (longestAt1: number) => number): Promise<HTMLCanvasElement> {
    const doc = pdf.pdfDocument.value
    if (!doc) throw new Error('no document')
    const page = await doc.getPage(pageNumber)
    const rotation = (page.rotate + pdf.rotation.value) % 360
    const base = page.getViewport({ scale: 1, rotation })
    const viewport = page.getViewport({ scale: scaleFor(Math.max(base.width, base.height) || 1), rotation })
    const canvas = hostWindow.document.createElement('canvas')
    canvas.width = Math.ceil(viewport.width)
    canvas.height = Math.ceil(viewport.height)
    const ctx = canvas.getContext('2d', { alpha: false })
    if (!ctx) throw new Error('no 2d context')
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    await page.render({ canvas, canvasContext: ctx, viewport }).promise
    page.cleanup()
    return canvas
  }

  function download(blob: Blob, name: string) {
    const url = URL.createObjectURL(blob)
    const a = hostWindow.document.createElement('a')
    a.href = url
    a.download = name
    hostWindow.document.body.append(a)
    a.click()
    a.remove()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  }

  async function copyPageAsImage() {
    if (!pdf.pdfDocument.value) return showToast(t('pdfReader.menu.noPdf'))
    const pageNumber = pdf.pageNumber.value || 1
    let canvas: HTMLCanvasElement
    try {
      canvas = await renderPage(pageNumber, (longest) => Math.min(MAX_IMAGE_SCALE, MAX_IMAGE_DIMENSION / longest))
    } catch (err) {
      console.error('[PdfReader] page render failed', err)
      return showToast(t('pdfReader.menu.renderFailed'))
    }
    const copied = () => showToast(t('pdfReader.menu.pageCopiedAsImage', { page: pageNumber }))
    // Hosted: the app's origin is not a secure context, so the browser clipboard is missing - C# copies it.
    if (typeof window.__webviewAction === 'function') {
      try {
        if ((await copyImageToClipboard(canvas.toDataURL('image/png')))?.ok) return copied()
      } catch { /* fall back to the browser clipboard */ }
    }
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/png'))
    if (!blob) return showToast(t('pdfReader.menu.imageFailed'))
    const nav = hostWindow.navigator
    if (hostWindow.ClipboardItem && nav.clipboard?.write) {
      try {
        await nav.clipboard.write([new hostWindow.ClipboardItem({ 'image/png': blob })])
        return copied()
      } catch (err) {
        console.warn('[PdfReader] clipboard image write failed, downloading', err)
      }
    }
    download(blob, `page-${pageNumber}.png`)
    showToast(t('pdfReader.menu.clipboardUnavailableSaved', { page: pageNumber }))
  }

  return { copyPageAsImage }
}
