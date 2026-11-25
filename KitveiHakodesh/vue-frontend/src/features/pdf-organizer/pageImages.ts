import type { HostWindow } from '@/composables/detachedWindow/hostWindow'
import type { OrganizerPage, OrganizerSource } from './pdfPageAssembly'

export type ImageFormat = 'png' | 'jpeg'

/** One organizer page as an image file at `dpi` (PDF units are 1/72 inch), its turn included. */
export async function pageImage(
  hostWindow: HostWindow,
  page: OrganizerPage,
  sources: Map<number, OrganizerSource>,
  dpi: number,
  format: ImageFormat,
): Promise<Blob> {
  const scale = dpi / 72
  const canvas = hostWindow.document.createElement('canvas')
  const source = page.pageIndex >= 0 ? sources.get(page.source) : undefined
  if (source) {
    const pdfPage = await source.doc.getPage(page.pageIndex + 1)
    const viewport = pdfPage.getViewport({ scale, rotation: (pdfPage.rotate + page.rotation) % 360 })
    canvas.width = Math.ceil(viewport.width)
    canvas.height = Math.ceil(viewport.height)
    const ctx = canvas.getContext('2d', { alpha: false })!
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    await pdfPage.render({ canvas, canvasContext: ctx, viewport }).promise
  } else {
    const size = page.blankSize ?? { width: 612, height: 792 }
    const turned = page.rotation % 180 !== 0
    canvas.width = Math.ceil((turned ? size.height : size.width) * scale)
    canvas.height = Math.ceil((turned ? size.width : size.height) * scale)
    const ctx = canvas.getContext('2d', { alpha: false })!
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
  }
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, `image/${format}`, 0.92))
  canvas.width = canvas.height = 0
  if (!blob) throw new Error('image failed')
  return blob
}
