import type { Ref, ShallowRef } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { RenderingStates, type EventBus, type PDFViewer } from 'pdfjs-dist/web/pdf_viewer.mjs'

/**
 * The old viewer's memory tuning (legacy-pdf/public-pdfjs/CUSTOMIZATIONS.md), done from outside
 * stock pdfjs-dist. Measured on a 1827-page scanned book: each drawn page holds its canvas plus
 * its decoded scan (~50 MB together), and PDF.js keeps up to 10 drawn pages.
 *
 * - Drawn pages kept: 3 (current + one each side), not PDF.js's 10. Same rule as PDF.js's
 *   PDFPageViewBuffer: a page joins when it starts drawing ("pagerender"), the visible pages are
 *   kept, the least recent beyond max(3, 2 x visible + 1) are destroyed (canvas and page data).
 * - Idle cleanup after 5 s, not never: when nothing is left to draw, PDF.js's rendering queue
 *   calls `onIdle` after its own CLEANUP_TIMEOUT (30 s); its app then frees the drawn pages'
 *   leftovers and the worker's caches (`_cleanup`). We do the same `_cleanup`, after 5 s, as the
 *   old viewer. The 5 s is that PDF.js timer, made shorter - there is no event for it.
 * - A scroll jump bigger than one screen (scrollbar drag, Home / End, page box, thumbnails)
 *   cancels the draws still running for the pages left behind, so the new place draws at once
 *   (the old viewer's fork commit 87223b8bd), and puts those pages back to "not drawn" so they
 *   draw again when shown (CUSTOMIZATIONS.md 0c; a cancelled page otherwise stays "running").
 */
const KEEP_DRAWN_PAGES = 3
const IDLE_CLEANUP_MS = 5000

interface PageViewLike {
  id: number
  renderingState: number
  detailView?: PageViewLike | null
  cancelRendering(): void
  destroy(): void
}
interface ViewerInternals {
  _pages?: PageViewLike[]
  _getVisiblePages(): { views: unknown[]; ids: Set<number> }
}
interface RenderingQueueInternals {
  renderHighestPriority(visible?: unknown): void
}

export function usePdfPageMemory(
  container: Ref<HTMLElement | null>,
  eventBus: EventBus,
  viewer: ShallowRef<PDFViewer | null>,
  pdfDocument: ShallowRef<PDFDocumentProxy | null>,
) {
  const internals = () => viewer.value as unknown as ViewerInternals | null

  // --- drawn pages kept (PDFPageViewBuffer with 3) ---
  const drawn = new Set<PageViewLike>()
  let keep = KEEP_DRAWN_PAGES
  function trim() {
    while (drawn.size > keep) {
      const oldest = drawn.values().next().value as PageViewLike
      drawn.delete(oldest)
      oldest.destroy()
    }
  }
  eventBus.on('pagerender', (e: { pageNumber: number }) => {
    const view = internals()?._pages?.[e.pageNumber - 1]
    if (!view) return
    drawn.delete(view)
    drawn.add(view)
    trim()
  })
  eventBus.on('updateviewarea', () => {
    const v = internals()
    if (!v?._pages) return
    const visible = v._getVisiblePages()
    keep = Math.max(KEEP_DRAWN_PAGES, 2 * visible.views.length + 1)
    for (const view of [...drawn]) {
      if (!visible.ids.has(view.id)) continue
      drawn.delete(view)
      drawn.add(view)
    }
    trim()
  })
  // A new document: the old page views are gone.
  eventBus.on('pagesinit', () => drawn.clear())

  // --- idle cleanup (PDF.js app's _cleanup, after 5 s) ---
  let idleTimer: ReturnType<typeof setTimeout> | null = null
  function cleanup() {
    idleTimer = null
    const v = viewer.value
    const doc = pdfDocument.value
    if (!v || !doc) return
    v.cleanup()
    // Refused while a page (e.g. a thumbnail) is still drawing; the next idle tries again.
    void doc.cleanup().catch(() => {})
  }
  let patchedQueue: object | null = null
  function patchRenderingQueue() {
    const queue = viewer.value?.renderingQueue as (RenderingQueueInternals & object) | undefined
    if (!queue || queue === patchedQueue) return
    patchedQueue = queue
    // Any new draw request ends the idle wait, as in PDFRenderingQueue.
    const renderHighestPriority = queue.renderHighestPriority.bind(queue)
    queue.renderHighestPriority = (visible?: unknown) => {
      if (idleTimer) clearTimeout(idleTimer)
      idleTimer = null
      renderHighestPriority(visible)
    }
    // PDFRenderingQueue reads `onIdle` only when nothing is left to draw: start our shorter wait
    // there and give it no callback (so its own 30 s timer is not set).
    Object.defineProperty(queue, 'onIdle', {
      configurable: true,
      get() {
        if (idleTimer) clearTimeout(idleTimer)
        idleTimer = setTimeout(cleanup, IDLE_CLEANUP_MS)
        return null
      },
      set() {},
    })
  }
  eventBus.on('pagesinit', patchRenderingQueue)

  // --- a jump cancels the draws left behind ---
  let lastTop = 0
  let lastLeft = 0
  useEventListener(container, 'scroll', () => {
    const el = container.value
    if (!el) return
    const jumped = Math.abs(el.scrollTop - lastTop) > el.clientHeight || Math.abs(el.scrollLeft - lastLeft) > el.clientWidth
    lastTop = el.scrollTop
    lastLeft = el.scrollLeft
    if (!jumped) return
    // Runs before PDF.js's own scroll update (it waits for the next frame), as the fork's patch.
    for (const page of internals()?._pages ?? []) {
      for (const view of [page, page.detailView]) {
        if (view && (view.renderingState === RenderingStates.RUNNING || view.renderingState === RenderingStates.PAUSED)) {
          view.cancelRendering()
          view.renderingState = RenderingStates.INITIAL
        }
      }
    }
  }, { passive: true })

  return { dispose: () => idleTimer && clearTimeout(idleTimer) }
}
