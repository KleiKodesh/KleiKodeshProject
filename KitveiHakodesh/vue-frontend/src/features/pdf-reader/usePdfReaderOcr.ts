import { ref, watch, onUnmounted, type Ref, type ShallowRef } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { PDFDocumentProxy, PDFPageProxy } from 'pdfjs-dist'
import type { PDFViewer } from 'pdfjs-dist/web/pdf_viewer.mjs'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import { joinOcrWorkerPool, ocrWorker, ocrWorkerUsed } from './ocrWorkerPool'
import type { OcrScript, OcrSelectionResult } from './pdfOcrTypes'

/** Output resolution for the re-rendered area: 300 DPI (PDF units are 1/72 inch). */
const OCR_RENDER_SCALE = 300 / 72
/** Cap on the re-rendered area's pixels (keeps a whole-page box from allocating a huge canvas). */
const MAX_RENDER_PIXELS = 16_000_000
/** RenderingStates in pdf_viewer.mjs. */
const PAGE_VIEW_RUNNING = 1
const PAGE_VIEW_FINISHED = 3

const cleanText = (text: string) =>
  text.split('\n').map((l) => l.trim()).filter((l) => l.length > 0).join('\n').replace(/\s+/g, ' ').trim()

/**
 * A text layer that is there but unreadable (boxes, replacement/control/private-use glyphs,
 * Latin-1 mojibake of old Hebrew fonts): such a selection goes to OCR, as if forced.
 */
function isGibberish(text: string) {
  const chars = Array.from(text.replace(/\s+/g, ''))
  if (chars.length === 0) return true
  let bad = 0
  for (const ch of chars) {
    if (/[�■-◿\u0000-\u001F\u007F-\u009F]|\p{Co}|\p{Cn}/u.test(ch)) bad++
    else if (/[À-ÿ]/.test(ch)) bad++
  }
  return bad / chars.length >= 0.3
}

interface ClientRect { left: number; top: number; right: number; bottom: number }

/** The viewer's page view (PDFPageView), the parts this file reads. */
interface PageViewLike {
  id: number
  div: HTMLElement
  pdfPage?: PDFPageProxy | null
  renderingState: number
  viewport: { scale: number; rotation: number; convertToPdfPoint(x: number, y: number): [number, number] }
}

/**
 * One page's part of a captured area, in page terms (PDF units): it stays valid after scrolling
 * or zooming, so the same area can be recognised again. No page objects are kept.
 */
interface AreaPart {
  pageNumber: number
  /** [x0, y0, x1, y1] in PDF user space. */
  pdfRect: [number, number, number, number]
  rotation: number
  /** CSS pixels per PDF unit when captured, and the part's place in the area (CSS pixels). */
  cssPerPdf: number
  dx: number
  dy: number
  w: number
  h: number
}
interface CapturedArea { parts: AreaPart[]; width: number; height: number }

/** Frees a canvas's pixel buffer now instead of at the next GC. */
function releaseCanvas(c: HTMLCanvasElement) {
  c.width = 0
  c.height = 0
}

/**
 * OCR for the PDF view: while on, dragging a box over the pages reads its text - from the page's
 * own text content when it is readable (unless force is on), otherwise by rendering that area of
 * the viewer's own pages at 300 DPI and recognising it with Tesseract (shared workers,
 * ocrWorkerPool). The box maps to the pages through each PDFPageView's viewport, so zoom, scroll,
 * rotation and spreads all hold. The result box can re-run the same area with force on/off.
 */
export function usePdfReaderOcr(
  scroller: Ref<HTMLElement | null>,
  pdf: { pdfDocument: Ref<PDFDocumentProxy | null>; viewer: ShallowRef<PDFViewer | null> },
) {
  const hostWindow = useHostWindow()
  const store = usePdfOcrStore()
  const isActive = ref(false)
  const isProcessing = ref(false)
  const processingProgress = ref(0)
  const result = ref<OcrSelectionResult | null>(null)
  /** The area of the shown result (page terms only), for a re-run with force on/off. */
  let lastArea: CapturedArea | null = null
  let disposed = false
  /** Bumped by each recognition and by closing the result: a late result of an older run is dropped. */
  let runGeneration = 0
  const leavePool = joinOcrWorkerPool()

  // Warm the chosen script's worker while OCR mode is on.
  watch([isActive, () => store.script], ([active, s]) => {
    if (active) ocrWorker(s).catch(() => {})
  })

  // ── Selection rectangle ──
  let selectionDiv: HTMLDivElement | null = null
  let start: { x: number; y: number } | null = null
  const isDrawing = ref(false)

  /** Viewport point -> scroller content coordinates (where an absolute child is placed). */
  function toContent(el: HTMLElement, clientX: number, clientY: number) {
    const r = el.getBoundingClientRect()
    return { x: clientX - r.left - el.clientLeft + el.scrollLeft, y: clientY - r.top - el.clientTop + el.scrollTop }
  }

  function removeSelection() {
    selectionDiv?.remove()
    selectionDiv = null
    start = null
    isDrawing.value = false
  }

  // Capture phase: runs before the hand tool's and the text layer's own pointer handling.
  useEventListener(scroller, 'pointerdown', (e: PointerEvent) => {
    const el = scroller.value
    if (!isActive.value || !el || e.button !== 0) return
    e.preventDefault()
    e.stopImmediatePropagation()
    start = toContent(el, e.clientX, e.clientY)
    const div = el.ownerDocument.createElement('div')
    div.className = 'pdf-reader-ocr-rect'
    Object.assign(div.style, { left: `${start.x}px`, top: `${start.y}px`, width: '0px', height: '0px' })
    el.appendChild(div)
    selectionDiv = div
    isDrawing.value = true
    el.setPointerCapture(e.pointerId)
  }, { capture: true })

  useEventListener(scroller, 'pointermove', (e: PointerEvent) => {
    const el = scroller.value
    if (!selectionDiv || !start || !el) return
    e.stopImmediatePropagation()
    const cur = toContent(el, e.clientX, e.clientY)
    const width = Math.abs(cur.x - start.x)
    const height = Math.abs(cur.y - start.y)
    Object.assign(selectionDiv.style, {
      left: `${Math.min(cur.x, start.x)}px`,
      top: `${Math.min(cur.y, start.y)}px`,
      width: `${width}px`,
      height: `${height}px`,
    })
    selectionDiv.classList.toggle('active', width > 5 && height > 5)
  }, { capture: true })

  useEventListener(scroller, 'pointerup', (e: PointerEvent) => {
    if (!selectionDiv) return
    e.stopImmediatePropagation()
    const r = selectionDiv.getBoundingClientRect()
    removeSelection()
    // One selection per activation, as the old viewer.
    isActive.value = false
    if (r.width > 5 && r.height > 5) {
      const area = captureArea({ left: r.left, top: r.top, right: r.right, bottom: r.bottom })
      if (area) void processArea(area, store.skipExistingText)
    }
  }, { capture: true })

  useEventListener(scroller, 'pointercancel', removeSelection, { capture: true })

  /** The box (client pixels) -> per-page parts in PDF units, through each page view's viewport. */
  function captureArea(rect: ClientRect): CapturedArea | null {
    const viewer = pdf.viewer.value
    if (!viewer) return null
    const found: (Omit<AreaPart, 'dx' | 'dy'> & { x0: number; y0: number })[] = []
    for (let i = 0; i < viewer.pagesCount; i++) {
      const view = viewer.getPageView(i) as unknown as PageViewLike | undefined
      if (!view?.div) continue
      const div = view.div
      const pr = div.getBoundingClientRect()
      if (pr.bottom <= rect.top || pr.top >= rect.bottom || pr.right <= rect.left || pr.left >= rect.right) continue
      // The page box inside its border.
      const px = pr.left + div.clientLeft
      const py = pr.top + div.clientTop
      const pw = div.clientWidth
      const ph = div.clientHeight
      if (!pw || !ph) continue
      const x0 = Math.max(rect.left, px)
      const y0 = Math.max(rect.top, py)
      const x1 = Math.min(rect.right, px + pw)
      const y1 = Math.min(rect.bottom, py + ph)
      if (x1 - x0 < 1 || y1 - y0 < 1) continue
      // CSS size of the page box vs. its viewport (they match unless the page is mid-zoom).
      const vp = view.viewport
      const [ax, ay] = vp.convertToPdfPoint(x0 - px, y0 - py)
      const [bx, by] = vp.convertToPdfPoint(x1 - px, y1 - py)
      found.push({
        pageNumber: view.id,
        pdfRect: [Math.min(ax, bx), Math.min(ay, by), Math.max(ax, bx), Math.max(ay, by)],
        rotation: vp.rotation,
        cssPerPdf: vp.scale,
        w: x1 - x0,
        h: y1 - y0,
        x0,
        y0,
      })
    }
    if (found.length === 0) return null
    const ux = Math.min(...found.map((p) => p.x0))
    const uy = Math.min(...found.map((p) => p.y0))
    const width = Math.max(...found.map((p) => p.x0 + p.w)) - ux
    const height = Math.max(...found.map((p) => p.y0 + p.h)) - uy
    const parts = found.map(({ x0, y0, ...p }) => ({ ...p, dx: x0 - ux, dy: y0 - uy }))
    return { parts, width, height }
  }

  /** The viewer's own page proxy for a page (no separate getPage when the view already has it). */
  async function pageFor(pageNumber: number): Promise<{ page: PDFPageProxy; view: PageViewLike | null } | null> {
    const view = (pdf.viewer.value?.getPageView(pageNumber - 1) as unknown as PageViewLike | undefined) ?? null
    if (view?.pdfPage) return { page: view.pdfPage, view }
    const doc = pdf.pdfDocument.value
    if (!doc) return null
    return { page: await doc.getPage(pageNumber), view }
  }

  /** Frees a page's render resources after our render, unless the viewer is showing that page. */
  function releasePage(page: PDFPageProxy, view: PageViewLike | null) {
    if (!(view && (view.renderingState === PAGE_VIEW_FINISHED || view.renderingState === PAGE_VIEW_RUNNING))) page.cleanup()
  }

  // ── Text short-cut: the page's own text content (what the viewer's text layer draws) ──
  async function extractText(area: CapturedArea): Promise<string | null> {
    const words: string[] = []
    for (const part of area.parts) {
      const got = await pageFor(part.pageNumber)
      if (!got) continue
      const content = await got.page.getTextContent()
      const [x0, y0, x1, y1] = part.pdfRect
      for (const item of content.items) {
        if (!('str' in item) || !item.str.trim()) continue
        const [a, b, , , e, f] = item.transform as [number, number, number, number, number, number]
        const len = Math.hypot(a, b) || 1
        // Centre of the run: half its width along the baseline, half its height above it.
        const cx = e + (a / len) * (item.width / 2) - (b / len) * (item.height / 2)
        const cy = f + (b / len) * (item.width / 2) + (a / len) * (item.height / 2)
        if (cx >= x0 && cx <= x1 && cy >= y0 && cy <= y1) words.push(item.str)
      }
    }
    const text = words.join(' ').replace(/\s+/g, ' ').trim()
    return text || null
  }

  /** True while the viewer draws a page (RenderingStates.RUNNING). */
  function viewerDrawing(): boolean {
    const viewer = pdf.viewer.value
    if (!viewer) return false
    for (let i = 0; i < viewer.pagesCount; i++) {
      if ((viewer.getPageView(i) as unknown as PageViewLike | undefined)?.renderingState === PAGE_VIEW_RUNNING) return true
    }
    return false
  }
  /** Runs `cont` once no page draw is running, checked each frame of the window it is drawn in. */
  function yieldToPageDraws(cont: () => void) {
    if (disposed || !viewerDrawing()) return cont()
    hostWindow.requestAnimationFrame(() => yieldToPageDraws(cont))
  }

  // ── Image of the area: each part rendered from the viewer's page at 300 DPI, composited ──
  async function renderArea(area: CapturedArea, doc: Document): Promise<HTMLCanvasElement | null> {
    // Output pixels per CSS pixel: 300 DPI of the overlapped pages, capped by area.
    let outPerCss = Infinity
    for (const p of area.parts) outPerCss = Math.min(outPerCss, OCR_RENDER_SCALE / p.cssPerPdf)
    outPerCss = Math.max(1, Math.min(outPerCss, Math.sqrt(MAX_RENDER_PIXELS / (area.width * area.height))))

    const out = doc.createElement('canvas')
    out.width = Math.max(1, Math.round(area.width * outPerCss))
    out.height = Math.max(1, Math.round(area.height * outPerCss))
    const ctx = out.getContext('2d')!
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, out.width, out.height)

    for (const part of area.parts) {
      const got = await pageFor(part.pageNumber)
      if (!got) continue
      const { page, view } = got
      const scale = part.cssPerPdf * outPerCss
      const full = page.getViewport({ scale, rotation: part.rotation })
      // Both corners (this PDF.js has no convertToViewportRectangle).
      const [rx0, ry0] = full.convertToViewportPoint(part.pdfRect[0], part.pdfRect[1])
      const [rx1, ry1] = full.convertToViewportPoint(part.pdfRect[2], part.pdfRect[3])
      const w = Math.max(1, Math.round(Math.abs(rx1 - rx0)))
      const h = Math.max(1, Math.round(Math.abs(ry1 - ry0)))
      const viewport = page.getViewport({ scale, rotation: part.rotation, offsetX: -Math.min(rx0, rx1), offsetY: -Math.min(ry0, ry1) })
      const partCanvas = doc.createElement('canvas')
      partCanvas.width = w
      partCanvas.height = h
      try {
        const task = page.render({ canvas: partCanvas, viewport, background: '#ffffff' })
        // Lower priority than the viewer's own page draws, as its rendering queue gives a
        // background view: between operator chunks, wait while any page view is drawing.
        task.onContinue = (cont: () => void) => yieldToPageDraws(cont)
        await task.promise
        ctx.drawImage(partCanvas, Math.round(part.dx * outPerCss), Math.round(part.dy * outPerCss))
      } finally {
        releaseCanvas(partCanvas)
        releasePage(page, view)
      }
    }
    return out
  }

  async function processArea(area: CapturedArea, force: boolean) {
    const el = scroller.value
    if (!el) return
    lastArea = area
    const generation = ++runGeneration
    if (!force) {
      const text = await extractText(area).catch(() => null)
      if (generation !== runGeneration || disposed) return
      if (text && !isGibberish(text)) {
        result.value = { text, isOcr: false, forced: false }
        return
      }
    }

    result.value = { text: '', isOcr: true, forced: force }
    isProcessing.value = true
    processingProgress.value = 0
    let progressInterval: ReturnType<typeof setInterval> | null = null
    let canvas: HTMLCanvasElement | null = null
    const target: OcrScript = store.script
    try {
      const [c, worker] = await Promise.all([renderArea(area, el.ownerDocument), ocrWorker(target)])
      canvas = c
      if (disposed || generation !== runGeneration || !canvas) return
      progressInterval = setInterval(() => {
        if (processingProgress.value < 0.9) processingProgress.value += Math.random() * 0.3
      }, 200)
      const { data } = await worker.recognize(canvas)
      ocrWorkerUsed(target)
      processingProgress.value = 1
      if (generation === runGeneration && result.value) result.value = { text: cleanText(data.text), isOcr: true, forced: force }
    } catch (error) {
      console.error('[PdfReaderOcr] OCR failed:', error)
      if (generation === runGeneration && result.value) result.value = { text: '', isOcr: true, forced: force }
    } finally {
      if (canvas) releaseCanvas(canvas)
      if (progressInterval != null) clearInterval(progressInterval)
      if (generation === runGeneration) {
        isProcessing.value = false
        processingProgress.value = 0
      }
    }
  }

  // ── Esc: closes the result first (unless still recognising), then OCR mode ──
  useEventListener(hostWindow, 'keydown', (event: KeyboardEvent) => {
    if (event.key !== 'Escape') return
    if (result.value) {
      if (isProcessing.value) return
      dismissResult()
    } else if (isActive.value) {
      deactivate()
    } else {
      return
    }
    event.preventDefault()
    event.stopPropagation()
  }, { capture: true })

  function activate() {
    isActive.value = true
  }
  function deactivate() {
    removeSelection()
    isActive.value = false
  }
  function toggle() {
    isActive.value ? deactivate() : activate()
  }
  function dismissResult() {
    runGeneration++
    result.value = null
    lastArea = null
    isProcessing.value = false
    processingProgress.value = 0
  }
  /** Result box "Start new": closes the result and OCR mode is on for the next box at once. */
  function startNew() {
    dismissResult()
    activate()
  }
  /** Result box "Force": flips force and reads the same area again, the result shown in place. */
  function rerunWithForce(force: boolean) {
    if (store.skipExistingText !== force) store.toggleSkipExistingText()
    if (lastArea) void processArea(lastArea, force)
  }

  onUnmounted(() => {
    disposed = true
    runGeneration++
    removeSelection()
    lastArea = null
    result.value = null
    leavePool()
  })

  return { isActive, isDrawing, isProcessing, processingProgress, result, toggle, activate, deactivate, dismissResult, startNew, rerunWithForce }
}
