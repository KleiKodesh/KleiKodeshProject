import type { PDFDocumentProxy, PDFPageProxy, RenderTask } from 'pdfjs-dist'
import type { EventBus, PDFViewer } from 'pdfjs-dist/web/pdf_viewer.mjs'

/**
 * Page thumbnails for one open document, shared by everything that shows them (the viewer's
 * thumbnails panel, the page organizer): the same document gets the same engine, so a page
 * drawn for one is there for the other.
 *
 * Modelled on PDF.js's own thumbnail viewer (pdf_thumbnail_view.js + pdf_rendering_queue.js),
 * but without its two memory traps (a live view object per page, every page kept forever):
 * - Only what is asked for renders: a row that mounts asks (`request`), a row that unmounts
 *   lets go (`release`). A request let go before its turn never renders; a render running for
 *   no one is cancelled. Rows showing now go first, then, one at a time, rows mounted just
 *   outside the view (as PDF.js: the visible ones, then ahead).
 * - The main view's pages come first: a thumbnail render pauses (PDF.js `onContinue`) while
 *   the viewer is drawing a page, and resumes when it has drawn it.
 * - A page the viewer has already drawn at least as big is not rendered again: its canvas is
 *   scaled down (in halving steps, as PDF.js's #reduceImage) into the thumbnail.
 * - A thumbnail is kept as a small JPEG blob URL (not a canvas, not a data: string in the JS
 *   heap); the cache is LRU-capped and revokes what it drops, never a URL still on screen.
 * - Page proxies are fetched at render time only, and the page's parsed resources are freed
 *   (`page.cleanup()`) after a thumbnail render when the viewer is not showing that page.
 */

/** Device-pixel widths thumbnails are drawn at; a request takes the smallest that is wide enough. */
const WIDTH_STEPS = [128, 160, 240, 360, 520]
/** Renders at once (PDF.js renders thumbnails one at a time behind the pages; two keeps a fast scroll fed). */
const CONCURRENT_RENDERS = 2
/**
 * Thumbnails kept per document that no row shows: none. An image lives only while a mounted row
 * (thumbnails panel / organizer) holds it; when the row unmounts - scrolled away, panel closed or
 * mode switched - its image is released (the user asked for no thumbnail kept in memory).
 */
const MAX_KEPT = 0
/** RenderingStates.RUNNING in pdf_viewer.mjs (not imported: importing the viewer module here would load it in the organizer). */
const PAGE_VIEW_RUNNING = 1
const PAGE_VIEW_FINISHED = 3

export interface ThumbnailImage {
  url: string
  /** Width / height of the image (the page at its rotation). */
  aspect: number
}

interface Entry {
  pageIndex: number
  rotation: number
  width: number
  image: ThumbnailImage
  /** Rows showing it now: never dropped while above 0. */
  holders: number
}

interface Job {
  pageIndex: number
  rotation: number
  width: number
  wanters: number
  /** Rows wanting it that are showing now (not just mounted near the view). */
  showing: number
  running: boolean
  task: RenderTask | null
  promise: Promise<ThumbnailImage>
  resolve: (image: ThumbnailImage) => void
  reject: (reason: unknown) => void
  /** Resumes a render paused for the main view. */
  resume: (() => void) | null
  /** Taken from the viewer's drawn page: a cancelled render of it is not a failure. */
  taken: boolean
  done: boolean
}

interface PageViewLike {
  id: number
  renderingState: number
  scale: number
  rotation: number
  thumbnailCanvas: HTMLCanvasElement | null
  pdfPage?: PDFPageProxy | null
}

/** Rotation in [0, 360). */
const norm = (r: number) => ((r % 360) + 360) % 360

/** One row's claim on a page's thumbnail (PdfThumbnailEngine.request). */
export interface ThumbnailHold {
  /** The image at once, when it was kept. */
  image: ThumbnailImage | null
  promise: Promise<ThumbnailImage>
  /** The row is showing (or no longer): showing rows render first. */
  setShowing(showing: boolean): void
  release(): void
}

export class ThumbnailCancelled extends Error {
  constructor() { super('thumbnail no longer wanted') }
}

export function thumbnailWidthFor(cssWidth: number, devicePixelRatio: number): number {
  const want = Math.ceil(cssWidth * (devicePixelRatio || 1))
  return WIDTH_STEPS.find((w) => w >= want) ?? WIDTH_STEPS[WIDTH_STEPS.length - 1]!
}

export class PdfThumbnailEngine {
  /** By `${pageIndex}:${rotation}`, each page's thumbnails by width (Map order = LRU order across `lru`). */
  private entries = new Map<string, Entry>()
  private jobs = new Map<string, Job>()
  /** Jobs waiting for a render slot, in request order (newest last). */
  private queue: Job[] = []
  private running = 0
  private viewer: PDFViewer | null = null
  private eventBus: EventBus | null = null
  private disposed = false

  constructor(private readonly doc: PDFDocumentProxy) {}

  /**
   * The viewer showing this document: its pages are rendered first, and its drawn canvases
   * become thumbnails. Returns the detach function.
   */
  attachViewer(viewer: PDFViewer, eventBus: EventBus): () => void {
    this.viewer = viewer
    this.eventBus = eventBus
    eventBus.on('pagerendered', this.onPageRendered)
    return () => {
      eventBus.off('pagerendered', this.onPageRendered)
      if (this.viewer === viewer) {
        this.viewer = null
        this.eventBus = null
      }
      this.resumePaused()
    }
  }

  /** A drawn thumbnail at once, when one wide enough is kept. Marks it used. */
  peek(pageIndex: number, rotation: number, width: number): ThumbnailImage | null {
    const entry = this.findEntry(pageIndex, norm(rotation), width)
    if (!entry) return null
    this.touch(entry)
    return entry.image
  }

  /**
   * The page's thumbnail at least `width` device pixels wide: at once (`image`) when one is
   * kept, else rendered (`promise`). A row holds what it asked for until it calls `release`
   * (it unmounted): a kept image is never dropped while held, and a render no one holds any
   * more is dropped from the line or cancelled. `promise` rejects with ThumbnailCancelled then.
   */
  request(pageIndex: number, rotation: number, width: number): ThumbnailHold {
    rotation = norm(rotation)
    const entry = this.findEntry(pageIndex, rotation, width)
    if (entry) {
      this.touch(entry)
      entry.holders++
      let released = false
      return {
        image: entry.image,
        promise: Promise.resolve(entry.image),
        setShowing: () => {},
        release: () => {
          if (released) return
          released = true
          entry.holders--
          this.trim()
        },
      }
    }
    const key = `${pageIndex}:${rotation}:${width}`
    let job = this.jobs.get(key)
    if (!job) {
      let resolve!: (image: ThumbnailImage) => void
      let reject!: (reason: unknown) => void
      const promise = new Promise<ThumbnailImage>((res, rej) => { resolve = res; reject = rej })
      promise.catch(() => {})
      job = { pageIndex, rotation, width, wanters: 0, showing: 0, running: false, task: null, promise, resolve, reject, resume: null, taken: false, done: false }
      this.jobs.set(key, job)
      this.queue.push(job)
    }
    const mine = job
    mine.wanters++
    let released = false
    let showing = false
    let held: Entry | null = null
    const promise = mine.promise.then((image) => {
      if (released) throw new ThumbnailCancelled()
      held = this.findExact(pageIndex, rotation, image)
      if (held) held.holders++
      return image
    })
    promise.catch(() => {})
    this.pump()
    return {
      image: null,
      promise,
      setShowing: (now: boolean) => {
        if (released || now === showing || mine.done) return
        showing = now
        mine.showing += now ? 1 : -1
        if (now) this.pump()
      },
      release: () => {
        if (released) return
        released = true
        if (showing && !mine.done) mine.showing--
        if (held) {
          held.holders--
          this.trim()
          return
        }
        if (mine.done || --mine.wanters > 0) return
        // No one wants it any more: drop it from the line, or stop its render.
        const at = this.queue.indexOf(mine)
        if (at >= 0) this.queue.splice(at, 1)
        if (mine.running) mine.task?.cancel()
        this.finish(mine, null, new ThumbnailCancelled())
      },
    }
  }

  /** Revokes every kept image and stops every render (the document closed). */
  dispose() {
    this.disposed = true
    for (const job of [...this.jobs.values()]) {
      job.task?.cancel()
      this.finish(job, null, new ThumbnailCancelled())
    }
    this.queue = []
    for (const entry of this.entries.values()) URL.revokeObjectURL(entry.image.url)
    this.entries.clear()
    this.eventBus?.off('pagerendered', this.onPageRendered)
    this.viewer = null
    this.eventBus = null
  }

  // ── Cache ──────────────────────────────────────────────────────────────────

  private entryKey(pageIndex: number, rotation: number, width: number) {
    return `${pageIndex}:${rotation}:${width}`
  }

  /** The narrowest kept thumbnail of the page at least `width` wide. */
  private findEntry(pageIndex: number, rotation: number, width: number): Entry | null {
    let best: Entry | null = null
    for (const w of WIDTH_STEPS) {
      if (w < width) continue
      const e = this.entries.get(this.entryKey(pageIndex, rotation, w))
      if (e && (!best || e.width < best.width)) best = e
    }
    return best
  }

  private findExact(pageIndex: number, rotation: number, image: ThumbnailImage): Entry | null {
    for (const w of WIDTH_STEPS) {
      const e = this.entries.get(this.entryKey(pageIndex, rotation, w))
      if (e?.image === image) return e
    }
    return null
  }

  private touch(entry: Entry) {
    const key = this.entryKey(entry.pageIndex, entry.rotation, entry.width)
    this.entries.delete(key)
    this.entries.set(key, entry)
  }

  private store(pageIndex: number, rotation: number, width: number, image: ThumbnailImage): ThumbnailImage {
    const key = this.entryKey(pageIndex, rotation, width)
    const old = this.entries.get(key)
    if (old) {
      // Already kept (drawn by the view's canvas meanwhile): keep the one rows may hold.
      URL.revokeObjectURL(image.url)
      this.touch(old)
      return old.image
    }
    // No trim here: the row that asked takes its hold right after; release() trims.
    this.entries.set(key, { pageIndex, rotation, width, image, holders: 0 })
    return image
  }

  /** Drops images no row shows (beyond MAX_KEPT, which is 0). */
  private trim() {
    if (this.entries.size <= MAX_KEPT) return
    for (const [key, entry] of this.entries) {
      if (this.entries.size <= MAX_KEPT) break
      if (entry.holders > 0) continue
      URL.revokeObjectURL(entry.image.url)
      this.entries.delete(key)
    }
  }

  // ── Rendering ──────────────────────────────────────────────────────────────

  /** The viewer is drawing a page now: thumbnails wait (pages before thumbnails, as PDF.js). */
  private mainBusy(): boolean {
    const views = this.viewer?.getCachedPageViews?.() as Iterable<PageViewLike> | undefined
    if (!views) return false
    for (const v of views) if (v.renderingState === PAGE_VIEW_RUNNING) return true
    return false
  }

  private pageViewOf(pageIndex: number): PageViewLike | null {
    return (this.viewer?.getPageView(pageIndex) as PageViewLike | undefined) ?? null
  }

  /**
   * The next job: the first one showing (rows ask in the order they mount, top to bottom);
   * else, with no other render running, the first one mounted near the view - as PDF.js
   * renders the visible thumbnails in order, then one ahead.
   */
  private nextJob(): Job | null {
    for (let i = 0; i < this.queue.length; i++) {
      if (this.queue[i]!.showing > 0) return this.queue.splice(i, 1)[0]!
    }
    if (this.running > 0 || !this.queue.length) return null
    return this.queue.shift()!
  }

  private pump() {
    while (!this.disposed && this.running < CONCURRENT_RENDERS) {
      const job = this.nextJob()
      if (!job) break
      // A page the viewer has drawn big enough: from its canvas, no render.
      if (this.fromViewCanvas(job)) continue
      job.running = true
      this.running++
      void this.render(job).finally(() => {
        this.running--
        this.pump()
      })
    }
  }

  private async render(job: Job) {
    let page: PDFPageProxy | null = null
    try {
      page = await this.doc.getPage(job.pageIndex + 1)
      if (job.done) return
      const rotate = norm(page.rotate + job.rotation)
      const base = page.getViewport({ scale: 1, rotation: rotate })
      // Like PDF.js, drawn at twice the size and halved: the same look as one taken from a page's canvas.
      const viewport = page.getViewport({ scale: (2 * job.width) / base.width, rotation: rotate })
      // Offscreen: the engine serves every window, and no window's document has to outlive it.
      const canvas = new OffscreenCanvas(Math.ceil(viewport.width), Math.ceil(viewport.height))
      const ctx = canvas.getContext('2d', { alpha: false })!
      ctx.fillStyle = '#ffffff'
      ctx.fillRect(0, 0, canvas.width, canvas.height)
      const task = page.render({ canvas: canvas as unknown as HTMLCanvasElement, canvasContext: ctx as unknown as CanvasRenderingContext2D, viewport, intent: 'display' })
      job.task = task
      task.onContinue = (cont: () => void) => {
        if (this.mainBusy()) job.resume = cont
        else cont()
      }
      try {
        await task.promise
      } finally {
        job.task = null
      }
      if (job.done || job.taken) return
      const image = await this.toImage(canvas, job.width)
      canvas.width = canvas.height = 0
      if (job.done || job.taken) return URL.revokeObjectURL(image.url)
      this.finish(job, this.store(job.pageIndex, job.rotation, job.width, image))
    } catch (err) {
      if (!job.taken) this.finish(job, null, job.done ? null : err)
    } finally {
      // Free the page's parsed content unless the viewer is showing it (it would parse it again).
      const view = this.pageViewOf(job.pageIndex)
      if (page && !(view && (view.renderingState === PAGE_VIEW_FINISHED || view.renderingState === PAGE_VIEW_RUNNING))) page.cleanup()
    }
  }

  private finish(job: Job, image: ThumbnailImage | null, error?: unknown) {
    if (job.done) return
    job.done = true
    job.running = false
    job.resume = null
    this.jobs.delete(`${job.pageIndex}:${job.rotation}:${job.width}`)
    if (image) job.resolve(image)
    else job.reject(error ?? new ThumbnailCancelled())
  }

  private resumePaused() {
    for (const job of this.jobs.values()) {
      const resume = job.resume
      if (resume && !this.mainBusy()) {
        job.resume = null
        resume()
      }
    }
  }

  private onPageRendered = (e: { source: PageViewLike; pageNumber: number; isDetailView?: boolean }) => {
    // A page is drawn: paused thumbnails go on, then any wanted thumbnail of this page comes from it.
    this.resumePaused()
    if (e.isDetailView) return
    for (const job of [...this.jobs.values()]) {
      if (job.pageIndex !== e.pageNumber - 1 || job.done) continue
      if (this.fromViewCanvas(job)) {
        const at = this.queue.indexOf(job)
        if (at >= 0) this.queue.splice(at, 1)
      }
    }
  }

  /**
   * Fills the job from the viewer's drawn page when it shows the same rotation at least as
   * big (PDF.js setImage). A running render of it is cancelled. True when taken.
   */
  private fromViewCanvas(job: Job): boolean {
    const view = this.pageViewOf(job.pageIndex)
    if (!view || view.renderingState !== PAGE_VIEW_FINISHED || !this.viewer) return false
    if (norm(this.viewer.pagesRotation) !== job.rotation) return false
    const canvas = view.thumbnailCanvas
    if (!canvas || canvas.width < job.width) return false
    if (job.taken) return true
    // Taken now, so a cancelled render of it leaves it alone.
    job.taken = true
    if (job.running) job.task?.cancel()
    this.toImage(canvas, job.width).then(
      (image) => {
        if (job.done) return URL.revokeObjectURL(image.url)
        this.finish(job, this.store(job.pageIndex, job.rotation, job.width, image))
      },
      (err) => this.finish(job, null, err),
    )
    return true
  }

  /** The canvas scaled to `width` (in halving steps for quality), as a JPEG blob URL. */
  private async toImage(source: HTMLCanvasElement | OffscreenCanvas, width: number): Promise<ThumbnailImage> {
    const aspect = source.width / source.height
    const height = Math.max(1, Math.round(width / aspect))
    let current: HTMLCanvasElement | OffscreenCanvas = source
    const temps: OffscreenCanvas[] = []
    while (current.width > 2 * width) {
      const half: OffscreenCanvas = new OffscreenCanvas(Math.max(width, current.width >> 1), Math.max(height, current.height >> 1))
      half.getContext('2d', { alpha: false })!.drawImage(current, 0, 0, half.width, half.height)
      temps.push(half)
      current = half
    }
    const out = new OffscreenCanvas(width, height)
    const ctx = out.getContext('2d', { alpha: false })!
    ctx.imageSmoothingQuality = 'high'
    ctx.drawImage(current, 0, 0, width, height)
    for (const t of temps) t.width = t.height = 0
    const blob = await out.convertToBlob({ type: 'image/jpeg', quality: 0.85 })
    out.width = out.height = 0
    return { url: URL.createObjectURL(blob), aspect }
  }
}

const engines = new WeakMap<PDFDocumentProxy, PdfThumbnailEngine>()

/** The document's thumbnail engine (one per document, shared by the panel and the organizer). */
export function thumbnailEngineFor(doc: PDFDocumentProxy): PdfThumbnailEngine {
  let engine = engines.get(doc)
  if (!engine) engines.set(doc, (engine = new PdfThumbnailEngine(doc)))
  return engine
}

/** The document is closing: its thumbnails are freed. */
export function disposeThumbnailEngine(doc: PDFDocumentProxy | null | undefined) {
  if (!doc) return
  engines.get(doc)?.dispose()
  engines.delete(doc)
}
