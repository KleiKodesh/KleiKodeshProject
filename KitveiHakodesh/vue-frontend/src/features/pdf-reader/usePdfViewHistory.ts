import type { Ref } from 'vue'
import { onBeforeUnmount } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import type { EventBus, PDFLinkService, PDFViewer } from 'pdfjs-dist/web/pdf_viewer.mjs'
import { PDFSearchIndexStore } from 'pdfjs-dist/web/pdf_viewer.mjs'
import { ViewHistory } from './pdfjs/view_history.js'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * Every PDF reopens where it was left (the old viewer's behaviour): PDF.js's own ViewHistory
 * (localStorage "pdfjs.history", the last 20 files, keyed by the document fingerprint), with the
 * restore / save glue of PDF.js's web/app.js (load: getMultiple -> setInitialView; save:
 * onUpdateViewarea, onViewerModesChanged, onSidebarViewChanged).
 *
 * Stored per file: page, zoom (percent or preset name), scrollLeft, scrollTop, rotation,
 * scrollMode, spreadMode, sidebarView (PDF.js numbers: 0 closed, 1 thumbnails, 2 outline) and
 * our own sidePanelMode (the panel's mode, also bookmarks / comments).
 */

/** PDF.js's SidebarView numbers (web/ui_utils.js). */
export const SidebarView = { UNKNOWN: -1, NONE: 0, THUMBS: 1, OUTLINE: 2 } as const

export interface StoredView {
  page: number | null
  zoom: string | number
  scrollLeft: string | number
  scrollTop: string | number
  rotation: string | number | null
  sidebarView: number
  sidePanelMode: string | null
  scrollMode: number
  spreadMode: number
}

/**
 * The one place a document's ViewHistory is made. When the fork's sidecar index lands, its
 * ViewHistory constructor also prunes the stored sidecars (pdf_sidecar_store retainOnly), so the
 * sidecar code must share this instance / lifecycle: hook it here.
 */
export function createViewHistory(fingerprint: string) {
  const history = new ViewHistory(fingerprint)
  // A file's search index (pdfSearchIndexLink.ts) lives as long as its view history entry.
  void history._initializedPromise.then(() => {
    const files = (history.database as { files?: { fingerprint: string }[] }).files ?? []
    return PDFSearchIndexStore.retainOnly(files.map((f) => f.fingerprint))
  }).catch(() => {})
  return history
}

const isValidRotation = (a: unknown) => Number.isInteger(a) && (a as number) % 90 === 0
const isValidScrollMode = (m: number) => Number.isInteger(m) && m >= 0 && m <= 3
const isValidSpreadMode = (m: number) => Number.isInteger(m) && m >= 0 && m <= 2

export function usePdfViewHistory(container: Ref<HTMLElement | null>, eventBus: EventBus, linkService: PDFLinkService) {
  let store: ViewHistory | null = null
  /** As app.js isInitialViewSet: nothing is saved until the stored view was applied. */
  let initialViewSet = false
  /** True while the reading state is not the reader's own (presentation mode). */
  let suspended: () => boolean = () => false
  /** The side panel's restore (PdfReaderView). */
  let applySidePanel: ((open: boolean, mode: string | null) => void) | null = null

  const canSave = () => !!store && initialViewSet && !suspended()
  const write = (properties: Record<string, unknown>) => void store?.setMultiple(properties).catch(() => {})

  // The view area moves with every scroll frame: written once the scroll ends (scrollend), not per
  // frame. A change without a scroll (zoom, rotation keeping the top) is written at once.
  let scrolling = false
  let pending: Record<string, unknown> | null = null
  function flush() {
    if (pending && canSave()) write(pending)
    pending = null
  }
  eventBus.on('updateviewarea', ({ location }: { location?: { pageNumber: number; scale: string | number; left: number; top: number; rotation: number } }) => {
    if (!location || !canSave()) return
    pending = { page: location.pageNumber, zoom: location.scale, scrollLeft: location.left, scrollTop: location.top, rotation: location.rotation }
    if (!scrolling) flush()
  })
  useEventListener(container, 'scroll', () => (scrolling = true), { passive: true })
  useEventListener(container, 'scrollend', () => {
    scrolling = false
    flush()
  })
  const hostWindow = useHostWindow()
  useEventListener(hostWindow, 'pagehide', flush)
  useEventListener(() => hostWindow.document, 'visibilitychange', () => {
    if (hostWindow.document.visibilityState === 'hidden') flush()
  })
  onBeforeUnmount(flush)

  // As app.js onViewerModesChanged (not in presentation mode).
  eventBus.on('scrollmodechanged', (e: { mode: number }) => canSave() && write({ scrollMode: e.mode }))
  eventBus.on('spreadmodechanged', (e: { mode: number }) => canSave() && write({ spreadMode: e.mode }))

  /** As app.js onSidebarViewChanged: the panel opened / closed or changed mode. */
  function saveSidePanel(open: boolean, mode: string) {
    if (!canSave()) return
    const sidebarView = !open ? SidebarView.NONE : mode === 'thumbnails' ? SidebarView.THUMBS : mode === 'outline' ? SidebarView.OUTLINE : SidebarView.UNKNOWN
    write({ sidebarView, sidePanelMode: mode })
  }

  /** A new document: its stored view (app.js getMultiple), read while its pages are set up. */
  function begin(doc: PDFDocumentProxy): Promise<StoredView | null> {
    flush()
    initialViewSet = false
    scrolling = false
    store = createViewHistory(doc.fingerprints[0] as string)
    return store.getMultiple({
      page: null,
      zoom: 'auto',
      scrollLeft: '0',
      scrollTop: '0',
      rotation: null,
      sidebarView: SidebarView.UNKNOWN,
      sidePanelMode: null,
      scrollMode: -1,
      spreadMode: -1,
    }).then((v) => v as unknown as StoredView, () => null)
  }

  /**
   * app.js setInitialView: modes, rotation, then the place and zoom through the link service's
   * hash (`page=..&zoom=..,left,top`). That path is not a document destination, so the saved
   * zoom applies even with ignoreDestinationZoom. `placeElsewhere` (an outline target): the
   * zoom is restored but the place is left to that jump.
   */
  function applyInitialView(viewer: PDFViewer, stored: StoredView | null, defaultScale: string, placeElsewhere = false) {
    if (stored?.page) {
      const scroll = stored.scrollMode | 0
      const spread = stored.spreadMode | 0
      if (isValidScrollMode(scroll)) viewer.scrollMode = scroll
      if (isValidSpreadMode(spread)) viewer.spreadMode = spread
      const rotation = parseInt(String(stored.rotation), 10)
      if (isValidRotation(rotation)) viewer.pagesRotation = rotation
      if (placeElsewhere) {
        const zoom = parseFloat(String(stored.zoom))
        viewer.currentScaleValue = zoom ? String(zoom / 100) : String(stored.zoom)
      } else {
        linkService.setHash(`page=${stored.page}&zoom=${stored.zoom},${stored.scrollLeft},${stored.scrollTop}`)
      }
      const mode = stored.sidePanelMode ?? (stored.sidebarView === SidebarView.THUMBS ? 'thumbnails' : stored.sidebarView === SidebarView.OUTLINE ? 'outline' : null)
      if (stored.sidebarView !== SidebarView.UNKNOWN || mode) applySidePanel?.(stored.sidebarView !== SidebarView.NONE, mode)
    }
    // Scale was not set (nothing stored, or a bad entry): the default.
    if (!viewer.currentScaleValue) viewer.currentScaleValue = defaultScale
    initialViewSet = true
  }

  /** A reopen that keeps its place (after a save): no restore, but saving resumes for its entry. */
  function markInitialViewSet() {
    initialViewSet = true
  }

  return {
    begin,
    applyInitialView,
    markInitialViewSet,
    saveSidePanel,
    /** Presentation mode: its page-fit, page-by-page view is not saved as the reading state. */
    setSuspended: (fn: () => boolean) => void (suspended = fn),
    onRestoreSidePanel: (fn: (open: boolean, mode: string | null) => void) => void (applySidePanel = fn),
  }
}

export type PdfViewHistory = ReturnType<typeof usePdfViewHistory>
