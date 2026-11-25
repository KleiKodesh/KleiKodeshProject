import { ref, watch, onUnmounted, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import type { Worker } from 'tesseract.js'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import type { OcrScript, OcrSelectionResult } from './pdfOcrTypes'

const LANG_FILES: Record<OcrScript, string> = {
  hebrew: 'heb',
  rashi: 'heb_rashi',
  mixed: 'heb+heb_rashi',
  english: 'eng',
}

/** Output resolution for the re-rendered area: 300 DPI (PDF units are 1/72 inch). */
const OCR_RENDER_SCALE = 300 / 72
/** Cap on the re-rendered area's pixels (keeps a whole-page box from allocating a huge canvas). */
const MAX_RENDER_PIXELS = 16_000_000

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

/**
 * OCR for the new PDF view: while on, dragging a box over the pages reads its text - from the
 * text layer when it is readable (unless the store's force option is on), otherwise by
 * re-rendering that area at 300 DPI and recognising it with Tesseract. Script and force come
 * from the shared pdfOcrStore (the old viewer's toolbar choices); on/off is this view's own.
 */
export function usePdfReaderOcr(
  scroller: Ref<HTMLElement | null>,
  pdf: { pdfDocument: Ref<PDFDocumentProxy | null>; rotation: Ref<number> },
) {
  const hostWindow = useHostWindow()
  const store = usePdfOcrStore()
  const isActive = ref(false)
  const isProcessing = ref(false)
  const processingProgress = ref(0)
  const result = ref<OcrSelectionResult | null>(null)

  // ── Tesseract workers (same caching as the old viewer's usePdfOcrSelection) ──
  const workers: Partial<Record<OcrScript, Worker>> = {}
  const workerSpawns: Partial<Record<OcrScript, Promise<void>>> = {}
  let disposed = false

  function initWorker(target: OcrScript): Promise<void> {
    if (workers[target]) return Promise.resolve()
    const existing = workerSpawns[target]
    if (existing) return existing
    const spawn = (async () => {
      const { createWorker } = await import('tesseract.js')
      const worker = await createWorker(LANG_FILES[target], 1, {
        langPath: '/tesseract/',
        gzip: false,
        workerPath: '/tesseract/worker.min.js',
        corePath: '/tesseract/tesseract-core.wasm.js',
      })
      if (disposed) {
        void worker.terminate()
        return
      }
      workers[target] = worker
    })()
    workerSpawns[target] = spawn
    spawn.catch(() => {}).finally(() => { delete workerSpawns[target] })
    return spawn
  }

  // Warm the chosen script's worker while OCR mode is on, as the old viewer did on script change.
  watch([isActive, () => store.script], ([active, s]) => {
    if (active) initWorker(s).catch(() => {})
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
    if (r.width > 5 && r.height > 5) void processRect({ left: r.left, top: r.top, right: r.right, bottom: r.bottom })
  }, { capture: true })

  useEventListener(scroller, 'pointercancel', removeSelection, { capture: true })

  // ── Text layer short-cut ──
  function extractText(el: HTMLElement, rect: ClientRect): string | null {
    const words: string[] = []
    for (const span of el.querySelectorAll<HTMLElement>('.textLayer span')) {
      const text = span.textContent
      if (!text || !text.trim()) continue
      const sr = span.getBoundingClientRect()
      const cx = sr.left + sr.width / 2
      const cy = sr.top + sr.height / 2
      if (cx >= rect.left && cx <= rect.right && cy >= rect.top && cy <= rect.bottom) words.push(text)
    }
    const text = words.join(' ').replace(/\s+/g, ' ').trim()
    return text || null
  }

  // ── Image of the area: each overlapped page re-rendered at 300 DPI, composited as on screen ──
  async function captureArea(el: HTMLElement, rect: ClientRect): Promise<HTMLCanvasElement | null> {
    const doc = pdf.pdfDocument.value
    if (!doc) return null
    const parts: { pageNumber: number; x0: number; y0: number; x1: number; y1: number; px: number; py: number; pw: number }[] = []
    for (const pageEl of el.querySelectorAll<HTMLElement>('.page[data-page-number]')) {
      const pr = pageEl.getBoundingClientRect()
      // The page box inside its border.
      const px = pr.left + pageEl.clientLeft
      const py = pr.top + pageEl.clientTop
      const pw = pageEl.clientWidth
      const ph = pageEl.clientHeight
      if (!pw || !ph) continue
      const x0 = Math.max(rect.left, px)
      const y0 = Math.max(rect.top, py)
      const x1 = Math.min(rect.right, px + pw)
      const y1 = Math.min(rect.bottom, py + ph)
      if (x1 - x0 < 1 || y1 - y0 < 1) continue
      parts.push({ pageNumber: Number(pageEl.dataset.pageNumber), x0, y0, x1, y1, px, py, pw })
    }
    if (parts.length === 0) return null

    const ux = Math.min(...parts.map((p) => p.x0))
    const uy = Math.min(...parts.map((p) => p.y0))
    const ux1 = Math.max(...parts.map((p) => p.x1))
    const uy1 = Math.max(...parts.map((p) => p.y1))

    // Output pixels per CSS pixel: 300 DPI of the overlapped pages, capped by area.
    const pages = await Promise.all(parts.map(async (p) => {
      const page = await doc.getPage(p.pageNumber)
      const rotation = (page.rotate + pdf.rotation.value) % 360
      return { p, page, rotation, base: page.getViewport({ scale: 1, rotation }) }
    }))
    let outPerCss = Infinity
    for (const { p, base } of pages) outPerCss = Math.min(outPerCss, (base.width * OCR_RENDER_SCALE) / p.pw)
    const area = (ux1 - ux) * (uy1 - uy)
    outPerCss = Math.max(1, Math.min(outPerCss, Math.sqrt(MAX_RENDER_PIXELS / area)))

    const out = el.ownerDocument.createElement('canvas')
    out.width = Math.max(1, Math.round((ux1 - ux) * outPerCss))
    out.height = Math.max(1, Math.round((uy1 - uy) * outPerCss))
    const ctx = out.getContext('2d')!
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, out.width, out.height)

    for (const { p, page, rotation, base } of pages) {
      const scale = (p.pw * outPerCss) / base.width
      const w = Math.max(1, Math.round((p.x1 - p.x0) * outPerCss))
      const h = Math.max(1, Math.round((p.y1 - p.y0) * outPerCss))
      const viewport = page.getViewport({
        scale,
        rotation,
        offsetX: -(p.x0 - p.px) * outPerCss,
        offsetY: -(p.y0 - p.py) * outPerCss,
      })
      const partCanvas = el.ownerDocument.createElement('canvas')
      partCanvas.width = w
      partCanvas.height = h
      const partCtx = partCanvas.getContext('2d')!
      partCtx.fillStyle = '#ffffff'
      partCtx.fillRect(0, 0, w, h)
      await page.render({ canvas: partCanvas, canvasContext: partCtx, viewport, background: '#ffffff' } as any).promise
      ctx.drawImage(partCanvas, Math.round((p.x0 - ux) * outPerCss), Math.round((p.y0 - uy) * outPerCss))
    }
    return out
  }

  async function processRect(rect: ClientRect) {
    const el = scroller.value
    if (!el) return
    if (!store.skipExistingText) {
      const text = extractText(el, rect)
      if (text && !isGibberish(text)) {
        result.value = { text, isOcr: false }
        return
      }
    }

    result.value = { text: '', isOcr: true }
    isProcessing.value = true
    processingProgress.value = 0
    let progressInterval: ReturnType<typeof setInterval> | null = null
    try {
      const target = store.script
      const [canvas] = await Promise.all([captureArea(el, rect), initWorker(target)])
      if (disposed || !workers[target] || !canvas) return
      progressInterval = setInterval(() => {
        if (processingProgress.value < 0.9) processingProgress.value += Math.random() * 0.3
      }, 200)
      const { data } = await workers[target]!.recognize(canvas)
      processingProgress.value = 1
      if (result.value) result.value = { text: cleanText(data.text), isOcr: true }
    } catch (error) {
      console.error('[PdfReaderOcr] OCR failed:', error)
      if (result.value) result.value = { text: '', isOcr: true }
    } finally {
      if (progressInterval != null) clearInterval(progressInterval)
      isProcessing.value = false
      processingProgress.value = 0
    }
  }

  // ── Esc: closes the result first (unless still recognising), then OCR mode ──
  useEventListener(hostWindow, 'keydown', (event: KeyboardEvent) => {
    if (event.key !== 'Escape') return
    if (result.value) {
      if (isProcessing.value) return
      result.value = null
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
    result.value = null
  }

  onUnmounted(() => {
    disposed = true
    removeSelection()
    for (const worker of Object.values(workers)) void worker?.terminate()
  })

  return { isActive, isDrawing, isProcessing, processingProgress, result, toggle, activate, deactivate, dismissResult }
}
