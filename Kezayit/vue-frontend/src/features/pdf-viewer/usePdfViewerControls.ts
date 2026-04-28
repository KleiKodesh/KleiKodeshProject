import { ref, onMounted, onUnmounted } from 'vue'

// Mirror of PDF.js internal enums — kept in sync with viewer.mjs / pdf.mjs
const ScrollMode = { VERTICAL: 0, HORIZONTAL: 1, WRAPPED: 2, PAGE: 3 }
const SpreadMode = { NONE: 0, ODD: 1, EVEN: 2 }
const CursorTool = { SELECT: 0, HAND: 1 }

export const ZOOM_OPTIONS = [
  { value: 'auto', label: 'אוטומטי' },
  { value: 'page-fit', label: 'התאם עמוד' },
  { value: 'page-width', label: 'רוחב עמוד' },
]

export function usePdfViewerControls(getIframe: () => HTMLIFrameElement | null) {
  // ── State ──────────────────────────────────────────────────────────────────

  const currentPage = ref(1)
  const totalPages = ref(0)
  const currentZoom = ref('auto')
  const pageInputValue = ref('1')
  const pageInputFocused = ref(false)
  const cursorTool = ref<'select' | 'hand'>('select')
  const scrollMode = ref<'vertical' | 'horizontal' | 'wrapped' | 'page'>('vertical')
  const spreadMode = ref<'none' | 'odd' | 'even'>('none')

  // ── PDF.js access ──────────────────────────────────────────────────────────

  function getApp(): any {
    return (getIframe()?.contentWindow as any)?.PDFViewerApplication ?? null
  }

  function dispatch(event: string, detail: Record<string, unknown> = {}) {
    getApp()?.eventBus?.dispatch(event, { source: window, ...detail })
  }

  // ── Sync ───────────────────────────────────────────────────────────────────

  let syncInterval: ReturnType<typeof setInterval> | null = null

  function syncState() {
    const app = getApp()
    if (!app?.pdfViewer) return

    const page = app.pdfViewer.currentPageNumber
    const total = app.pdfViewer.pagesCount
    const scale = app.pdfViewer.currentScaleValue
    if (page) {
      currentPage.value = page
      if (!pageInputFocused.value) pageInputValue.value = String(page)
    }
    if (total) totalPages.value = total
    if (scale) currentZoom.value = scale

    const activeTool = app.pdfCursorTools?.activeTool ?? 0
    cursorTool.value = activeTool === CursorTool.HAND ? 'hand' : 'select'

    const scroll = app.pdfViewer.scrollMode
    if (scroll === ScrollMode.PAGE) scrollMode.value = 'page'
    else if (scroll === ScrollMode.HORIZONTAL) scrollMode.value = 'horizontal'
    else if (scroll === ScrollMode.WRAPPED) scrollMode.value = 'wrapped'
    else scrollMode.value = 'vertical'

    const spread = app.pdfViewer.spreadMode
    if (spread === SpreadMode.ODD) spreadMode.value = 'odd'
    else if (spread === SpreadMode.EVEN) spreadMode.value = 'even'
    else spreadMode.value = 'none'
  }

  onMounted(() => { syncInterval = setInterval(syncState, 400) })
  onUnmounted(() => { if (syncInterval) clearInterval(syncInterval) })

  // ── Page controls ──────────────────────────────────────────────────────────

  function prevPage() { dispatch('previouspage') }
  function nextPage() { dispatch('nextpage') }

  function commitPageInput() {
    const app = getApp()
    if (!app) return
    const page = parseInt(pageInputValue.value, 10)
    if (!isNaN(page) && page >= 1 && page <= totalPages.value) {
      app.pdfViewer.currentPageNumber = page
      currentPage.value = page
    } else {
      pageInputValue.value = String(currentPage.value)
    }
  }

  // ── Zoom controls ──────────────────────────────────────────────────────────

  function zoomIn() { dispatch('zoomin') }
  function zoomOut() { dispatch('zoomout') }

  function setZoom(value: string) {
    dispatch('scalechanged', { value })
    currentZoom.value = value
  }

  function zoomLabel(value: string): string {
    const option = ZOOM_OPTIONS.find((o) => o.value === value)
    if (option) return option.label
    const numeric = parseFloat(value)
    return !isNaN(numeric) ? Math.round(numeric * 100) + '%' : value
  }

  // ── Sidebar ────────────────────────────────────────────────────────────────

  function toggleSidebar() {
    const app = getApp()
    if (!app?.viewsManager) return
    if (app.viewsManager.isOpen) app.viewsManager.close()
    else app.viewsManager.open()
  }

  // ── File actions ───────────────────────────────────────────────────────────

  function download() { dispatch('download') }
  function print() { dispatch('print') }
  function presentationMode() { dispatch('presentationmode') }

  // ── Navigation ─────────────────────────────────────────────────────────────

  function firstPage() { dispatch('firstpage') }
  function lastPage() { dispatch('lastpage') }

  // ── Rotation ───────────────────────────────────────────────────────────────

  function rotateCw() { dispatch('rotatecw') }
  function rotateCcw() { dispatch('rotateccw') }

  // ── Cursor tool ────────────────────────────────────────────────────────────

  function setCursorTool(tool: 'select' | 'hand') {
    dispatch('switchcursortool', { tool: tool === 'hand' ? CursorTool.HAND : CursorTool.SELECT })
    cursorTool.value = tool
  }

  // ── Scroll mode ────────────────────────────────────────────────────────────

  function setScrollMode(mode: 'vertical' | 'horizontal' | 'wrapped' | 'page') {
    const modeMap = { vertical: ScrollMode.VERTICAL, horizontal: ScrollMode.HORIZONTAL, wrapped: ScrollMode.WRAPPED, page: ScrollMode.PAGE }
    dispatch('switchscrollmode', { mode: modeMap[mode] })
    scrollMode.value = mode
  }

  // ── Spread mode ────────────────────────────────────────────────────────────

  function setSpreadMode(mode: 'none' | 'odd' | 'even') {
    const modeMap = { none: SpreadMode.NONE, odd: SpreadMode.ODD, even: SpreadMode.EVEN }
    dispatch('switchspreadmode', { mode: modeMap[mode] })
    spreadMode.value = mode
  }

  // ── Document properties ────────────────────────────────────────────────────

  function documentProperties() { dispatch('documentproperties') }

  // ── Rectangle selection ────────────────────────────────────────────────────

  function rectangleSelect() {
    const win = getIframe()?.contentWindow as any
    win?.toggleRectangleSelection?.()
  }

  return {
    currentPage, totalPages, currentZoom, pageInputValue, pageInputFocused,
    cursorTool, scrollMode, spreadMode,
    prevPage, nextPage, commitPageInput,
    zoomIn, zoomOut, setZoom, zoomLabel,
    toggleSidebar,
    download, print, presentationMode,
    firstPage, lastPage,
    rotateCw, rotateCcw,
    setCursorTool, setScrollMode, setSpreadMode,
    documentProperties, rectangleSelect,
  }
}
