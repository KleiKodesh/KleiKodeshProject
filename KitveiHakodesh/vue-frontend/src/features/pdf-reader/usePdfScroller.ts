import { onBeforeUnmount, ref, shallowRef, type Ref } from 'vue'
import { GlobalWorkerOptions, PasswordResponses, getDocument, type PDFDocumentLoadingTask, type PDFDocumentProxy } from 'pdfjs-dist'
import { EventBus, FindState, LinkTarget, PDFFindController, PDFLinkService, PDFScriptingManager, PDFViewer, ScrollMode, SpreadMode } from 'pdfjs-dist/web/pdf_viewer.mjs'
import { classifyPdfLoadError, reportPdfLoadFailure, type PdfLoadFailure } from './pdfLoadErrors'
import { createPdfJumpHistory } from './pdfJumpHistory'
import 'pdfjs-dist/web/pdf_viewer.css'
import { AnnotationEditorType } from 'pdfjs-dist'
import { HIGHLIGHT_COLORS_OPTION } from './usePdfEditing'
import { createPdfEditManagers } from './pdfEditManagers'
import { createPdfScriptingServices } from './pdfScripting'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'

GlobalWorkerOptions.workerSrc = workerUrl

// PDF.js's decoder files (scanned books, Hebrew fonts, colour profiles), from the same pdfjs-dist
// as the engine (vite.config.ts pdfjsResourcesPlugin). Without them those pages render blank.
const RESOURCES = `${import.meta.env.BASE_URL}pdfjs-dist-res/`

/** PDF.js's named zoom presets. */
export const SCALE_PRESETS = ['auto', 'page-actual', 'page-fit', 'page-width'] as const
export type ScalePreset = (typeof SCALE_PRESETS)[number]

export const MIN_SCALE = 0.25
export const MAX_SCALE = 5
const SCALE_STEP = 1.1

export { FindState, ScrollMode, SpreadMode }

/** A reader's place in a document (usePdfScroller's viewState / open(source, keep)). */
export interface ViewState {
  pageNumber: number
  top?: number
  left?: number
  /** A preset name or a numeric scale as text (PDFViewer.currentScaleValue). */
  scaleValue: string
  rotation: number
}

/**
 * The PDF.js headless scroller (PDFViewer + EventBus), mounted in our own element.
 * No PDF.js UI: every toolbar, find bar and panel is ours and talks to it only through this.
 */
export function usePdfScroller(container: Ref<HTMLDivElement | null>) {
  const eventBus = new EventBus()
  // External links get target=_blank (never this page; usePdfExternalLinks sends them to the
  // system browser); link / outline jumps keep the reader's zoom.
  const linkService = new PDFLinkService({ eventBus, externalLinkTarget: LinkTarget.BLANK, ignoreDestinationZoom: true })
  const findController = new PDFFindController({ eventBus, linkService })
  /** Signature and comment managers for PDF.js's editors (dialogs: PdfEditDialogs.vue). */
  const editManagers = createPdfEditManagers(eventBus)
  /** Form-script alerts waiting to be shown (the view shows the first in the app's modal). */
  const scriptAlerts = ref<string[]>([])
  // Form scripts, sandboxed per viewer (pdfScripting.ts): updates and dialogs stay in this viewer's window.
  const scriptingManager = new PDFScriptingManager({
    eventBus,
    externalServices: createPdfScriptingServices({
      eventBus,
      hostWindow: useHostWindow(),
      resources: RESOURCES,
      dialogs: { alert: (message) => void (scriptAlerts.value = [...scriptAlerts.value, message]) },
    }),
  })
  const viewer = shallowRef<PDFViewer | null>(null)
  const pdfDocument = shallowRef<PDFDocumentProxy | null>(null)
  let loadingTask: PDFDocumentLoadingTask | null = null

  const pageNumber = ref(1)
  const pageCount = ref(0)
  const scale = ref(1)
  const rotation = ref(0)
  /** The preset in use, or null after a free zoom (wheel, pinch, typed %, +/-). */
  const preset = ref<ScalePreset | null>('page-width')
  const scrollMode = ref<number>(ScrollMode.VERTICAL)
  const spreadMode = ref<number>(SpreadMode.NONE)
  const matchCount = ref(0)
  const currentMatch = ref(0)
  const loadError = ref<string | null>(null)
  /** The document's page labels (e.g. roman front matter), or null when they are just the page numbers. */
  const pageLabels = shallowRef<string[] | null>(null)
  /** Why the last open failed (pdfLoadErrors), for the pane's message and Retry. */
  const loadFailure = shallowRef<PdfLoadFailure | null>(null)
  /** True from an open until its first page is drawn (the progress bar). */
  const loading = ref(false)
  /** Percent loaded, or NaN while the total is unknown. */
  const loadProgress = ref(Number.NaN)
  /** A protected document waiting for its password (PdfPasswordDialog). */
  const passwordRequest = shallowRef<{ incorrect: boolean; submit: (password: string) => void; cancel: () => void } | null>(null)
  /** PDF.js's find state (FindState) of the last search step, null before any. */
  const findState = ref<number | null>(null)
  /** The last step went past the end (or start, `findPrevious`) and wrapped. */
  const findWrappedPrevious = ref(false)
  /** Back / forward between the places jumps left (pdfJumpHistory). */
  const jumpHistory = createPdfJumpHistory(eventBus, viewer)
  linkService.setHistory(jumpHistory.linkHistory as never)
  eventBus.on('pagerendered', () => (loading.value = false))

  eventBus.on('pagechanging', (e: { pageNumber: number }) => (pageNumber.value = e.pageNumber))
  eventBus.on('scalechanging', (e: { scale: number; presetValue?: string }) => {
    scale.value = e.scale
    preset.value = (SCALE_PRESETS as readonly string[]).includes(e.presetValue ?? '') ? (e.presetValue as ScalePreset) : null
  })
  eventBus.on('rotationchanging', (e: { pagesRotation: number }) => (rotation.value = e.pagesRotation))
  eventBus.on('scrollmodechanged', (e: { mode: number }) => (scrollMode.value = e.mode))
  eventBus.on('spreadmodechanged', (e: { mode: number }) => (spreadMode.value = e.mode))
  eventBus.on('updatefindmatchescount', (e: { matchesCount: { current: number; total: number } }) => {
    matchCount.value = e.matchesCount.total
    currentMatch.value = e.matchesCount.current
  })
  eventBus.on('updatefindcontrolstate', (e: { state: number; previous?: boolean; matchesCount: { current: number; total: number } }) => {
    findState.value = e.state
    findWrappedPrevious.value = !!e.previous
    matchCount.value = e.matchesCount.total
    currentMatch.value = e.matchesCount.current
  })

  function ensureViewer(): PDFViewer {
    if (viewer.value) return viewer.value
    const el = container.value
    if (!el) throw new Error('PDF scroller container is not mounted')
    const v = new PDFViewer({
      container: el,
      eventBus,
      linkService,
      findController,
      scriptingManager,
      // Editing on: the editors are switched from our edit toolbar (usePdfEditing).
      annotationEditorMode: AnnotationEditorType.NONE,
      annotationEditorHighlightColors: HIGHLIGHT_COLORS_OPTION,
      // Not in PDFViewerOptions' types; PDFViewer hands them to its AnnotationEditorUIManager.
      // PDF.js's floating "highlight" button stays off: it reads the global document's selection
      // (never a detached window's); ours is PdfHighlightButton.
      ...({ signatureManager: editManagers.signatureManager, commentManager: editManagers.commentManager } as object),
    })
    linkService.setViewer(v)
    scriptingManager.setViewer(v)
    viewer.value = v
    return v
  }

  /** Where the reader is: page, top of the view, zoom and rotation - what a reload after a save restores. */
  let viewTop: { pageNumber: number; top?: number; left?: number } | null = null
  eventBus.on('updateviewarea', (e: { location?: { pageNumber: number; top?: number; left?: number } }) => {
    if (e.location) viewTop = { pageNumber: e.location.pageNumber, top: e.location.top, left: e.location.left }
  })
  function viewState(): ViewState {
    return {
      pageNumber: viewTop?.pageNumber ?? pageNumber.value,
      top: viewTop?.top,
      left: viewTop?.left,
      scaleValue: preset.value ?? String(scale.value),
      rotation: rotation.value,
    }
  }

  /** Bumped by each open: a late result or failure of an earlier open is ignored. */
  let openGeneration = 0
  /**
   * Opens a PDF by URL, or from bytes (a just-saved file, reloaded without a cached read).
   * `keep`: show the new document at this place and zoom (from viewState()) instead of page 1 at page width.
   */
  async function open(source: string | Uint8Array, keep?: ViewState) {
    viewTop = null
    const v = ensureViewer()
    const generation = ++openGeneration
    loadError.value = null
    loadFailure.value = null
    lastOpen = { source, keep }
    if (!keep) jumpHistory.reset()
    passwordRequest.value?.cancel()
    loading.value = true
    loadProgress.value = Number.NaN
    let passwordCancelled = false
    try {
      void loadingTask?.destroy()
      loadingTask = getDocument({
        ...(typeof source === 'string' ? { url: source } : { data: source.slice() }),
        cMapUrl: `${RESOURCES}cmaps/`,
        cMapPacked: true,
        standardFontDataUrl: `${RESOURCES}standard_fonts/`,
        wasmUrl: `${RESOURCES}wasm/`,
        iccUrl: `${RESOURCES}iccs/`,
        // XFA forms: PDFViewer draws them in its XFA layer; their values save like other form fields.
        enableXfa: true,
      })
      loadingTask.onProgress = ({ loaded, total }: { loaded: number; total: number }) => {
        if (generation === openGeneration) loadProgress.value = total > 0 ? Math.min(100, (loaded / total) * 100) : Number.NaN
      }
      // Protected document: ask (PdfPasswordDialog); Cancel hands PDF.js an error, as its password_prompt.js.
      loadingTask.onPassword = (updateCallback: (password: string | Error) => void, reason: number) => {
        if (generation !== openGeneration) return
        let answered = false
        const answer = (value: string | Error) => {
          if (answered) return
          answered = true
          passwordRequest.value = null
          updateCallback(value)
        }
        passwordRequest.value = {
          incorrect: reason === PasswordResponses.INCORRECT_PASSWORD,
          submit: (password) => answer(password),
          cancel: () => {
            passwordCancelled = true
            answer(new Error('Password prompt cancelled.'))
          },
        }
      }
      const doc = await loadingTask.promise
      if (generation !== openGeneration) return
      pdfDocument.value = doc
      pageCount.value = doc.numPages
      v.setDocument(doc)
      linkService.setDocument(doc)
      void scriptingManager.setDocument(doc)
      pageLabels.value = null
      void doc.getPageLabels().then((labels) => {
        if (generation !== openGeneration || pdfDocument.value !== doc) return
        // As PDF.js's app: labels that only repeat the page numbers are ignored.
        if (!labels || labels.length !== doc.numPages || labels.every((l, i) => l === String(i + 1))) return v.setPageLabels(null)
        pageLabels.value = labels
        v.setPageLabels(labels)
      }).catch(() => {})
      eventBus.on('pagesinit', () => {
        if (!keep) return void (v.currentScaleValue = 'page-width')
        v.pagesRotation = keep.rotation
        v.currentScaleValue = keep.scaleValue
        const page = Math.min(Math.max(1, keep.pageNumber), doc.numPages)
        if (keep.top == null) v.currentPageNumber = page
        else v.scrollPageIntoView({ pageNumber: page, destArray: [null, { name: 'XYZ' }, keep.left ?? null, keep.top, null], allowNegativeOffset: true })
      }, { once: true })
    } catch (err) {
      if (generation !== openGeneration) return
      loading.value = false
      passwordRequest.value = null
      loadError.value = err instanceof Error ? err.message : String(err)
      loadFailure.value = classifyPdfLoadError(err, passwordCancelled)
      reportPdfLoadFailure(typeof source === 'string' ? source : null, err, loadFailure.value)
    }
  }
  /** The last open's arguments, for Retry. */
  let lastOpen: { source: string | Uint8Array; keep?: ViewState } | null = null
  const retryOpen = () => lastOpen && open(lastOpen.source, lastOpen.keep)

  const goToPage = (n: number) => {
    if (viewer.value && n >= 1 && n <= pageCount.value) viewer.value.currentPageNumber = n
  }
  /** A page jump (page box, first / last, Home / End, thumbnails): remembered for Back (pdfJumpHistory). */
  const jumpToPage = (n: number) => {
    if (!viewer.value || n < 1 || n > pageCount.value || n === pageNumber.value) return
    jumpHistory.pushCurrentPosition()
    goToPage(n)
  }
  /** The label of page n (1-based): its page label when the document has them, else the number. */
  const pageLabel = (n: number) => pageLabels.value?.[n - 1] || String(n)
  /** Go to a typed page: a page label first (as PDF.js's page box), else a page number. False when neither fits. */
  function goToPageLabel(text: string): boolean {
    const typed = text.trim()
    if (!typed) return false
    const index = pageLabels.value?.indexOf(typed) ?? -1
    if (index >= 0) return jumpToPage(index + 1), true
    const n = Number(typed)
    if (!Number.isInteger(n) || n < 1 || n > pageCount.value) return false
    jumpToPage(n)
    return true
  }
  /** Go to an outline / link destination (named or explicit), as PDF.js's own outline does. */
  const goToDestination = (dest: string | unknown[]) => linkService.goToDestination(dest)
  const setScale = (s: number) => {
    if (viewer.value) viewer.value.currentScale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, s))
  }
  const zoomIn = () => setScale(scale.value * SCALE_STEP)
  const zoomOut = () => setScale(scale.value / SCALE_STEP)
  /** Zoom by a factor keeping the point under `origin` (client x, y) still - wheel and pinch zoom. */
  const zoomBy = (factor: number, origin?: [number, number]) => {
    const target = Math.min(MAX_SCALE, Math.max(MIN_SCALE, scale.value * factor))
    viewer.value?.updateScale({ scaleFactor: target / scale.value, origin, drawingDelay: 400 })
  }
  const setPreset = (p: ScalePreset) => viewer.value && (viewer.value.currentScaleValue = p)
  const fitWidth = () => setPreset('page-width')
  const rotate = (delta: 90 | -90) => viewer.value && (viewer.value.pagesRotation = (rotation.value + delta + 360) % 360)

  const setScrollMode = (m: number) => viewer.value && (viewer.value.scrollMode = m)
  const setSpreadMode = (m: number) => viewer.value && (viewer.value.spreadMode = m)

  /** Find with PDF.js's own highlighting; `again` steps to the next / previous match of the same query. */
  /** PDF.js's find options (the search bar's options menu). */
  const findOptions = ref({ highlightAll: true, caseSensitive: false, entireWord: false, matchDiacritics: false })
  /** The first search step since the bar opened remembers the place for Back (pdfJumpHistory). */
  let findPushedHistory = false
  function find(query: string, again = false, previous = false) {
    if (!findPushedHistory) {
      findPushedHistory = true
      jumpHistory.pushCurrentPosition()
    }
    if (!again) findState.value = null
    eventBus.dispatch('find', {
      source: null,
      type: again ? 'again' : '',
      query,
      caseSensitive: findOptions.value.caseSensitive,
      entireWord: findOptions.value.entireWord,
      highlightAll: findOptions.value.highlightAll,
      findPrevious: previous,
      matchDiacritics: findOptions.value.matchDiacritics,
    })
  }
  function clearFind() {
    eventBus.dispatch('findbarclose', { source: null })
    findPushedHistory = false
    findState.value = null
    matchCount.value = 0
    currentMatch.value = 0
  }

  onBeforeUnmount(() => {
    void scriptingManager.setDocument(null)
    viewer.value?.cleanup()
    void loadingTask?.destroy()
  })

  return {
    eventBus, viewer, pdfDocument, open, viewState, loadError, editManagers,
    loadFailure, retryOpen, loading, loadProgress, passwordRequest, jumpHistory, findState, findWrappedPrevious,
    pageNumber, pageCount, goToPage, jumpToPage, goToDestination, pageLabels, pageLabel, goToPageLabel, scriptAlerts,
    scale, zoomIn, zoomOut, zoomBy, setScale, fitWidth, preset, setPreset,
    rotation, rotate,
    scrollMode, setScrollMode, spreadMode, setSpreadMode,
    find, findOptions, clearFind, matchCount, currentMatch,
  }
}

export type PdfScroller = ReturnType<typeof usePdfScroller>
