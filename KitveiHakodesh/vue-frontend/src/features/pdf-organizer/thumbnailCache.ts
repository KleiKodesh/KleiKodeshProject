import type { PDFDocumentProxy } from 'pdfjs-dist'
import type { HostWindow } from '@/composables/detachedWindow/hostWindow'

/** Thumbnails are drawn at a few fixed widths, so moving the size slider reuses them. */
const WIDTH_STEPS = [160, 240, 360, 520]
const CONCURRENT_RENDERS = 2

/**
 * Page thumbnails for the organizer: rendered on demand (only pages that scroll into view
 * ask), two at a time, kept as image URLs until the organizer closes.
 */
export function createThumbnailCache(hostWindow: HostWindow) {
  const docIds = new WeakMap<PDFDocumentProxy, number>()
  let nextDocId = 1
  const urls = new Map<string, Promise<string>>()
  let running = 0
  const waiting: (() => void)[] = []

  async function slot<T>(work: () => Promise<T>): Promise<T> {
    if (running >= CONCURRENT_RENDERS) await new Promise<void>((resolve) => waiting.push(resolve))
    running++
    try {
      return await work()
    } finally {
      running--
      waiting.shift()?.()
    }
  }

  async function render(doc: PDFDocumentProxy, pageIndex: number, rotation: number, width: number): Promise<string> {
    const page = await doc.getPage(pageIndex + 1)
    const rotate = (page.rotate + rotation) % 360
    const base = page.getViewport({ scale: 1, rotation: rotate })
    const viewport = page.getViewport({ scale: width / base.width, rotation: rotate })
    const canvas = hostWindow.document.createElement('canvas')
    canvas.width = Math.ceil(viewport.width)
    canvas.height = Math.ceil(viewport.height)
    const ctx = canvas.getContext('2d', { alpha: false })!
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    await page.render({ canvas, canvasContext: ctx, viewport }).promise
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.85))
    canvas.width = canvas.height = 0
    if (!blob) throw new Error('thumbnail failed')
    return URL.createObjectURL(blob)
  }

  /** An image URL of the page at least `cssWidth` wide (device pixels counted). */
  function get(doc: PDFDocumentProxy, pageIndex: number, rotation: number, cssWidth: number): Promise<string> {
    const want = cssWidth * (hostWindow.devicePixelRatio || 1)
    const width = WIDTH_STEPS.find((w) => w >= want) ?? WIDTH_STEPS[WIDTH_STEPS.length - 1]!
    let id = docIds.get(doc)
    if (!id) docIds.set(doc, (id = nextDocId++))
    const key = `${id}:${pageIndex}:${rotation}:${width}`
    let url = urls.get(key)
    if (!url) {
      url = slot(() => render(doc, pageIndex, rotation, width))
      url.catch(() => urls.delete(key))
      urls.set(key, url)
    }
    return url
  }

  function dispose() {
    for (const url of urls.values()) url.then((u) => URL.revokeObjectURL(u), () => {})
    urls.clear()
  }

  return { get, dispose }
}

export type ThumbnailCache = ReturnType<typeof createThumbnailCache>
