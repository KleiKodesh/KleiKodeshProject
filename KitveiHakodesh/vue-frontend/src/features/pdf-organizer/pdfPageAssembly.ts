import { getDocument, type PDFDocumentProxy, type PDFWorker } from 'pdfjs-dist'
import { RANGE_CHUNK_BYTES, rangeTransportFor } from '@/features/pdf-reader/pdfRangeTransport'
import { sharedPdfWorkerInUse } from '@/features/pdf-reader/usePdfScroller'
import { setPageRotations } from './pdfRotationUpdate'

/**
 * Builds a new PDF from an ordered list of pages, with PDF.js's own page editor
 * (PDFDocumentProxy.extractPages - the engine behind PDF.js's "organize pages"):
 * pages of the open document, pages of other PDFs and blank pages, in any order,
 * duplicated or left out. Rotation is not part of PDF.js's page editor, so turned
 * pages get their /Rotate in a small incremental update afterwards (pdfRotationUpdate).
 */

/** The open document. */
export const PRIMARY_SOURCE = 0

export interface OrganizerSource {
  id: number
  /**
   * For rendering thumbnails, counting pages and as the page editor's source. Another file's
   * document is open in the same PDF.js worker as the open one (openPdfSource), so the page
   * editor reads only the pages it copies from it (KleiKodesh/pdf.js: extractPages with a
   * document source).
   */
  doc: PDFDocumentProxy
  name: string
}

export interface OrganizerPage {
  /** Stable key for the grid and undo. */
  key: number
  /** Source id; ignored for a blank page. */
  source: number
  /** 0-based page in the source; -1 = blank page. */
  pageIndex: number
  /** Extra clockwise turn in degrees (0, 90, 180, 270) on top of the page's own. */
  rotation: number
  /** Blank page size in PDF points (the size of the page it was inserted next to). */
  blankSize?: { width: number; height: number }
}

type PageInfo = Parameters<PDFDocumentProxy['extractPages']>[0][number]

/** A white image the size of a page, for PDF.js's synthetic image page (one per blank page). */
async function blankImage(size: { width: number; height: number }): Promise<ImageBitmap> {
  // Small: the page editor stretches the image over the page; the page size is what matters.
  const scale = 16 / Math.max(size.width, size.height)
  const w = Math.max(1, Math.round(size.width * scale))
  const h = Math.max(1, Math.round(size.height * scale))
  const canvas = new OffscreenCanvas(w, h)
  const ctx = canvas.getContext('2d')!
  ctx.fillStyle = '#ffffff'
  ctx.fillRect(0, 0, w, h)
  return createImageBitmap(canvas)
}

/**
 * The page editor takes each source page at most once per entry, in ascending page order,
 * placed at the given output positions. Duplicates of a page go to further entries.
 */
function pageInfosFor(pages: OrganizerPage[], sources: Map<number, OrganizerSource>): { infos: PageInfo[]; blanks: { pos: number; page: OrganizerPage }[] } {
  const infos: PageInfo[] = []
  const blanks: { pos: number; page: OrganizerPage }[] = []
  // source -> layers; each layer maps a source page to its output position
  const layers = new Map<number, Map<number, number>[]>()
  pages.forEach((page, pos) => {
    if (page.pageIndex < 0) { blanks.push({ pos, page }); return }
    let list = layers.get(page.source)
    if (!list) layers.set(page.source, (list = []))
    let layer = list.find((l) => !l.has(page.pageIndex))
    if (!layer) list.push((layer = new Map()))
    layer.set(page.pageIndex, pos)
  })
  for (const [sourceId, list] of layers) {
    const source = sources.get(sourceId)
    if (!source) throw new Error(`unknown source ${sourceId}`)
    for (const layer of list) {
      const indices = [...layer.keys()].sort((a, b) => a - b)
      infos.push({
        // null = the open document itself (its unsaved marks come along).
        document: sourceId === PRIMARY_SOURCE ? null : (source.doc as unknown as Uint8Array),
        includePages: indices,
        pageIndices: indices.map((i) => layer.get(i)!),
      })
    }
  }
  return { infos, blanks }
}

/**
 * The bytes of a new PDF holding `pages`, in order. The open document is `primary`; its unsaved
 * marks come along (PDF.js's annotationStorage), `options.outline` replaces its outline.
 */
export async function assemblePdf(
  primary: PDFDocumentProxy,
  pages: OrganizerPage[],
  sources: Map<number, OrganizerSource>,
  options: { outline?: unknown } = {},
): Promise<Uint8Array> {
  if (!pages.length) throw new Error('no pages')
  const { infos, blanks } = pageInfosFor(pages, sources)
  for (const { pos, page } of blanks) {
    infos.push({ image: await blankImage(page.blankSize ?? { width: 612, height: 792 }), pageIndices: [pos] })
  }
  const extract = primary.extractPages as unknown as (infos: PageInfo[], copyLevels?: null, o?: { outline?: unknown }) => Promise<Uint8Array | null>
  const bytes = await extract.call(primary, infos, null, options.outline ? { outline: options.outline } : {})
  if (!bytes) throw new Error('page editor returned nothing')
  const turns = new Map<number, number>()
  pages.forEach((p, i) => { if (p.rotation % 360) turns.set(i, p.rotation) })
  return turns.size ? setPageRotations(bytes, turns) : bytes
}

/**
 * Opens another PDF as a page source, in the same PDF.js worker as `sameWorkerAs` (the page
 * editor copies pages only between documents of one worker). A URL is read by random access
 * (pdfRangeTransport) where the host answers byte ranges: only what is shown or copied is read.
 * Bytes (a document built here, e.g. cut pages) are handed to the worker, not copied.
 */
export async function openPdfSource(source: { url: string } | { bytes: Uint8Array }, sameWorkerAs: PDFDocumentProxy): Promise<PDFDocumentProxy> {
  // The worker `sameWorkerAs` runs in (our fork records it as _usedWorker; older engines only
  // the one PDF.js created itself, else the viewers' shared one - usePdfScroller).
  const task = sameWorkerAs.loadingTask as unknown as { _usedWorker?: PDFWorker | null; _worker: PDFWorker | null }
  const worker = task._usedWorker ?? task._worker ?? sharedPdfWorkerInUse() ?? undefined
  // Same decoder files as the viewer (usePdfScroller): scanned pages and Hebrew fonts render blank without them.
  const resources = `${import.meta.env.BASE_URL}pdfjs-dist-res/`
  const range = 'url' in source ? await rangeTransportFor(source.url) : null
  return getDocument({
    worker,
    ...('url' in source
      ? (range ? { range, rangeChunkSize: RANGE_CHUNK_BYTES, disableAutoFetch: true, disableStream: true } : { url: source.url })
      : { data: source.bytes }),
    cMapUrl: `${resources}cmaps/`,
    cMapPacked: true,
    standardFontDataUrl: `${resources}standard_fonts/`,
    wasmUrl: `${resources}wasm/`,
    iccUrl: `${resources}iccs/`,
  }).promise
}
