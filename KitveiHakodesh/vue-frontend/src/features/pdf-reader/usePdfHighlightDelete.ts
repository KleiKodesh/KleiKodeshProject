import { nextTick } from 'vue'
import { AnnotationEditorType, AnnotationType } from 'pdfjs-dist'
import type { PdfScroller } from './usePdfScroller'
import { highlightBoxes, highlightTouches, type Box } from './pdfHighlightHitTest'

/* eslint-disable @typescript-eslint/no-explicit-any */
type Obj = any

/** What a "Delete highlight" removes: this session's editors and highlights saved in the file. */
export interface HighlightTargets {
  editors: Obj[]
  /** Saved highlight annotations (annotation-layer ids) by page index, with no editor yet. */
  saved: Map<number, Set<string>>
}

/**
 * The menu's "Delete highlight": finds the highlights under a window point or a selection's rects,
 * and deletes them through PDF.js's own editor delete (one undo step, saving drops them).
 * A saved highlight becomes an editor only while an editing mode is on, as in PDF.js: the
 * highlight mode is entered for that moment, marked silent so our editing bar stays closed.
 */
export function usePdfHighlightDelete(pdf: PdfScroller, setSilentMode: (on: boolean) => void) {
  const uiManager = (): Obj => pdf.editManagers.getUiManager()

  /** The probe boxes on each page, in PDF space, from window-space rects. */
  function probesByPage(rects: readonly DOMRect[]): Map<number, Box[]> {
    const out = new Map<number, Box[]>()
    const viewer = pdf.viewer.value as Obj
    if (!viewer) return out
    for (let i = 0; i < viewer.pagesCount; i++) {
      const pv = viewer.getPageView(i)
      const el: HTMLElement | undefined = pv?.div?.querySelector('.canvasWrapper') ?? pv?.div
      if (!el || !pv.viewport) continue
      const pr = el.getBoundingClientRect()
      for (const r of rects) {
        const l = Math.max(r.left, pr.left), rt = Math.min(r.right, pr.right)
        const t = Math.max(r.top, pr.top), b = Math.min(r.bottom, pr.bottom)
        if (l > rt || t > b) continue
        const [x1, y1] = pv.viewport.convertToPdfPoint(l - pr.left, t - pr.top)
        const [x2, y2] = pv.viewport.convertToPdfPoint(rt - pr.left, b - pr.top)
        let list = out.get(i)
        if (!list) out.set(i, (list = []))
        list.push([x1, y1, x2, y2])
      }
    }
    return out
  }

  /** The highlights touched by these window-space rects (a point = a zero-size rect). */
  function find(rects: readonly DOMRect[]): HighlightTargets {
    const targets: HighlightTargets = { editors: [], saved: new Map() }
    const ui = uiManager()
    const viewer = pdf.viewer.value as Obj
    if (!ui || !viewer) return targets
    for (const [pageIndex, probes] of probesByPage(rects)) {
      const annotationLayer = viewer.getPageView(pageIndex)?.annotationLayer?.annotationLayer
      const withEditor = new Set<string>()
      for (const editor of ui.getEditors(pageIndex)) {
        if (editor.editorType !== 'highlight' || editor.deleted || editor.isEmpty?.()) continue
        if (editor.annotationElementId) withEditor.add(editor.annotationElementId)
        let data: Obj = null
        try { data = editor.serialize() } catch { /* no geometry: its saved annotation's below */ }
        if (!data && editor.annotationElementId) data = annotationLayer?.getEditableAnnotation(editor.annotationElementId)?.data
        if (data && highlightTouches(highlightBoxes(data.quadPoints, data.rect), probes)) targets.editors.push(editor)
      }
      if (!annotationLayer) continue
      for (const editable of annotationLayer.getEditableAnnotations()) {
        const data = editable.data
        if (data?.annotationType !== AnnotationType.HIGHLIGHT || withEditor.has(data.id) || ui.isDeletedAnnotationElement(data.id)) continue
        if (!highlightTouches(highlightBoxes(data.quadPoints, data.rect), probes)) continue
        let ids = targets.saved.get(pageIndex)
        if (!ids) targets.saved.set(pageIndex, (ids = new Set()))
        ids.add(data.id)
      }
    }
    return targets
  }

  const isEmpty = (t: HighlightTargets) => t.editors.length === 0 && t.saved.size === 0

  /** Switch PDF.js's editing mode and wait until it is on (its editors made). */
  function switchMode(mode: number): Promise<void> {
    const viewer = pdf.viewer.value as Obj
    if (!viewer || viewer.annotationEditorMode === mode) return Promise.resolve()
    return new Promise((resolve) => {
      const done = (e: { mode: number }) => {
        if (e.mode !== mode) return
        pdf.eventBus.off('annotationeditormodechanged', done)
        resolve()
      }
      pdf.eventBus.on('annotationeditormodechanged', done)
      viewer.annotationEditorMode = { mode }
    })
  }

  /** Delete them, as PDF.js's own delete of the selected editors. */
  async function remove(t: HighlightTargets) {
    const ui = uiManager()
    if (!ui || isEmpty(t)) return
    const editors = [...t.editors]
    const silent = t.saved.size > 0
    if (silent) {
      setSilentMode(true)
      await switchMode(AnnotationEditorType.HIGHLIGHT)
      for (const [pageIndex, ids] of t.saved) {
        for (const editor of ui.getEditors(pageIndex)) if (ids.has(editor.annotationElementId)) editors.push(editor)
      }
    }
    try {
      if (editors.length) {
        ui.setSelected(editors[0])
        for (const editor of editors.slice(1)) ui.toggleSelected(editor)
        ui.delete()
      }
    } finally {
      if (silent) {
        await switchMode(AnnotationEditorType.NONE)
        // After the mode watchers have seen NONE.
        await nextTick()
        setSilentMode(false)
      }
    }
  }

  return { find, isEmpty, remove }
}
