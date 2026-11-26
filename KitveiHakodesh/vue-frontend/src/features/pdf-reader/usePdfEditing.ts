import { reactive, ref } from 'vue'
import { AnnotationEditorParamsType, AnnotationEditorType } from 'pdfjs-dist'
import type { PdfScroller } from './usePdfScroller'

export { AnnotationEditorType }

/** The editors PDF.js's scroller supports without its own app UI. */
export const EDIT_MODES = [
  AnnotationEditorType.FREETEXT,
  AnnotationEditorType.HIGHLIGHT,
  AnnotationEditorType.INK,
  AnnotationEditorType.STAMP,
  AnnotationEditorType.SIGNATURE,
] as const
export type EditMode = (typeof EDIT_MODES)[number]

/** PDF.js's own highlight colours (passed to PDFViewer as annotationEditorHighlightColors). */
export const HIGHLIGHT_COLORS = { yellow: '#FFFF98', green: '#53FFBC', blue: '#80EBFF', pink: '#FFCBE6', red: '#FF4F5F' }
export const HIGHLIGHT_COLORS_OPTION = Object.entries(HIGHLIGHT_COLORS).map(([k, v]) => `${k}=${v}`).join(',')

/**
 * Editing on the PDF.js scroller: pick an editor (text, highlight, draw, image), set its
 * colour / size / thickness / opacity, undo / redo / delete. All through PDF.js's EventBus.
 */
export function usePdfEditing(pdf: PdfScroller) {
  const mode = ref<number>(AnnotationEditorType.NONE)
  const states = reactive({ hasSomethingToUndo: false, hasSomethingToRedo: false, hasSelectedEditor: false })
  const params = reactive({
    freeTextColor: '#000000',
    freeTextSize: 10,
    inkColor: '#000000',
    inkThickness: 1,
    inkOpacity: 1,
    highlightColor: HIGHLIGHT_COLORS.yellow,
    highlightThickness: 12,
    highlightShowAll: true,
  })

  pdf.eventBus.on('annotationeditormodechanged', (e: { mode: number }) => {
    mode.value = e.mode
  })
  // PDF.js asks its viewer app to switch editor (e.g. "add note" / highlight on a selection made
  // in reading mode: the highlight is created only once the mode has changed). We are that app.
  pdf.eventBus.on('showannotationeditorui', (e: { mode: number }) => {
    if (pdf.viewer.value) pdf.viewer.value.annotationEditorMode = { mode: e.mode }
  })
  pdf.eventBus.on('editingstateschanged', (e: { details: Partial<typeof states> }) => {
    for (const k of Object.keys(states) as (keyof typeof states)[]) if (k in e.details) states[k] = !!e.details[k]
  })
  // An editor selected on the page reports its own values: show them.
  pdf.eventBus.on('annotationeditorparamschanged', (e: { details: [number, unknown][] }) => {
    for (const [type, value] of e.details) {
      if (type === AnnotationEditorParamsType.FREETEXT_COLOR) params.freeTextColor = String(value)
      else if (type === AnnotationEditorParamsType.FREETEXT_SIZE) params.freeTextSize = Number(value)
      else if (type === AnnotationEditorParamsType.INK_COLOR) params.inkColor = String(value)
      else if (type === AnnotationEditorParamsType.INK_THICKNESS) params.inkThickness = Number(value)
      else if (type === AnnotationEditorParamsType.INK_OPACITY) params.inkOpacity = Number(value)
      else if (type === AnnotationEditorParamsType.HIGHLIGHT_COLOR) params.highlightColor = String(value)
      else if (type === AnnotationEditorParamsType.HIGHLIGHT_THICKNESS) params.highlightThickness = Number(value)
    }
  })

  /** Switch editor; picking the active one again goes back to plain reading. */
  function setMode(m: number) {
    const v = pdf.viewer.value
    if (!v) return
    v.annotationEditorMode = { mode: mode.value === m ? AnnotationEditorType.NONE : m }
  }
  const stopEditing = () => pdf.viewer.value && (pdf.viewer.value.annotationEditorMode = { mode: AnnotationEditorType.NONE })

  function setParam(type: number, value: unknown) {
    pdf.eventBus.dispatch('switchannotationeditorparams', { source: null, type, value })
  }
  const P = AnnotationEditorParamsType
  const setFreeTextColor = (c: string) => setParam(P.FREETEXT_COLOR, (params.freeTextColor = c))
  const setFreeTextSize = (s: number) => setParam(P.FREETEXT_SIZE, (params.freeTextSize = s))
  const setInkColor = (c: string) => setParam(P.INK_COLOR, (params.inkColor = c))
  const setInkThickness = (t: number) => setParam(P.INK_THICKNESS, (params.inkThickness = t))
  const setInkOpacity = (o: number) => setParam(P.INK_OPACITY, (params.inkOpacity = o))
  const setHighlightColor = (c: string) => setParam(P.HIGHLIGHT_COLOR, (params.highlightColor = c))
  const setHighlightThickness = (t: number) => setParam(P.HIGHLIGHT_THICKNESS, (params.highlightThickness = t))

  const setHighlightShowAll = (v: boolean) => setParam(P.HIGHLIGHT_SHOW_ALL, (params.highlightShowAll = v))
  /** Image editor: open the picture picker and place it (PDF.js's "Add image" button). */
  const addImage = () => {
    if (mode.value === AnnotationEditorType.STAMP) return setParam(P.CREATE, null)
    // The image editor must be on first; place the picture once PDF.js has switched to it.
    pdf.eventBus.on('annotationeditormodechanged', () => setParam(P.CREATE, null), { once: true })
    if (pdf.viewer.value) pdf.viewer.value.annotationEditorMode = { mode: AnnotationEditorType.STAMP }
  }

  // ---- signature: the panel's "new signature" opens our dialog; saved ones are placed directly ----
  /** Turn the signature editor on (if needed), then run `then` once PDF.js has switched. */
  function withSignatureMode(then: () => void) {
    if (mode.value === AnnotationEditorType.SIGNATURE) return then()
    pdf.eventBus.on('annotationeditormodechanged', () => then(), { once: true })
    if (pdf.viewer.value) pdf.viewer.value.annotationEditorMode = { mode: AnnotationEditorType.SIGNATURE }
  }
  const newSignature = () => withSignatureMode(() => pdf.editManagers.newSignature())

  const action = (name: 'undo' | 'redo' | 'delete' | 'selectAll') => pdf.eventBus.dispatch('editingaction', { source: null, name })

  return {
    mode, states, params, setMode, stopEditing, newSignature, managers: pdf.editManagers,
    setFreeTextColor, setFreeTextSize, setInkColor, setInkThickness, setInkOpacity, setHighlightColor, setHighlightThickness, setHighlightShowAll, addImage,
    undo: () => action('undo'), redo: () => action('redo'), deleteSelected: () => action('delete'), selectAll: () => action('selectAll'),
  }
}

export type PdfEditing = ReturnType<typeof usePdfEditing>
