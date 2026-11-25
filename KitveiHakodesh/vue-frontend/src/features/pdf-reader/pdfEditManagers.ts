import { markRaw, reactive, shallowRef } from 'vue'
import { useLocalStorage } from '@vueuse/core'
import { AnnotationEditorParamsType, CSSConstants, SignatureExtractor, applyOpacity, findContrastColor } from 'pdfjs-dist'
import type { EventBus } from 'pdfjs-dist/web/pdf_viewer.mjs'
import { t } from '@/i18n'

/**
 * Our Vue side of PDF.js's SignatureManager and CommentManager: the objects PDF.js's
 * AnnotationEditorUIManager / AnnotationLayer call (the PDF.js app's web/signature_manager.js and
 * web/comment_manager.js are not in pdfjs-dist). They only hold state; the dialogs and the popup are
 * PdfEditDialogs.vue, drawn in the host window.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */
/** PDF.js objects with no public type (editors, the UI manager, annotation-layer popups). */
type Obj = any

/** The height a new signature gets on the page, in PDF points (PDF.js's DEFAULT_HEIGHT_IN_PAGE). */
export const SIGNATURE_HEIGHT_IN_PAGE = 40
/** As PDF.js's signature storage: a few saved signatures, the oldest kept. */
export const MAX_SAVED_SIGNATURES = 5

/** A signature's lines, as PDF.js's editors make and take them. */
export interface SignatureLines {
  curves: { points: number[] }[]
  areContours: boolean
  thickness: number
  width: number
  height: number
}
/** A saved signature: PDF.js's compressed form (SignatureExtractor.compressSignature), as its own storage keeps it. */
interface StoredSignature { uuid: string; description: string; signatureData: string }
/** A saved signature ready to show and place. */
export interface SavedSignature { uuid: string; description: string; lines: SignatureLines; viewBox: string; path: string }

/** The SVG preview of a signature's lines (PDF.js's toolbar button preview). */
export function signaturePreview(lines: SignatureLines): { viewBox: string; path: string } | null {
  const maxDim = Math.max(lines.width, lines.height)
  const data = (SignatureExtractor as Obj).processDrawnLines({
    lines: { curves: lines.curves, thickness: lines.thickness, width: lines.width, height: lines.height },
    pageWidth: maxDim,
    pageHeight: maxDim,
    rotation: 0,
    innerMargin: 0,
    mustSmooth: false,
    areContours: lines.areContours,
  })
  return data ? { viewBox: data.outline.viewBox, path: data.outline.toSVGPath() } : null
}

export function createPdfEditManagers(eventBus: EventBus) {
  // ---- signatures ----
  /**
   * Saved signatures live in the page's localStorage: the WebView2 profile folder, which the app
   * keeps beside itself (AppViewer.UserDataFolderFor: <app>/KitveiHakodesh/<webcache>).
   */
  const stored = useLocalStorage<StoredSignature[]>('pdfReader.savedSignatures', [])
  const saved = shallowRef<SavedSignature[]>([])
  let loaded: Promise<void> | null = null

  async function loadSignatures(reload = false) {
    if (loaded && !reload) return loaded
    loaded = (async () => {
      const out: SavedSignature[] = []
      for (const s of stored.value) {
        try {
          const d = await (SignatureExtractor as Obj).decompressSignature(s.signatureData)
          if (!d) continue
          const lines: SignatureLines = { curves: d.outlines.map((points: number[]) => ({ points })), areContours: d.areContours, thickness: d.thickness, width: d.width, height: d.height }
          const preview = signaturePreview(lines)
          if (preview) out.push({ uuid: s.uuid, description: s.description, lines, ...preview })
        } catch { /* a damaged entry is skipped */ }
      }
      saved.value = out
    })()
    return loaded
  }

  async function saveSignature(lines: { newCurves: number[][]; areContours: boolean; thickness: number; width: number; height: number }, description: string) {
    if (stored.value.length >= MAX_SAVED_SIGNATURES) return null
    const signatureData = await (SignatureExtractor as Obj).compressSignature({
      outlines: lines.newCurves, areContours: lines.areContours, thickness: lines.thickness, width: lines.width, height: lines.height,
    })
    const uuid = crypto.randomUUID()
    stored.value = [...stored.value, { uuid, description, signatureData }]
    await loadSignatures(true)
    return uuid
  }
  async function deleteSignature(uuid: string) {
    stored.value = stored.value.filter((s) => s.uuid !== uuid)
    await loadSignatures(true)
  }
  /** Place a saved signature (the signature editor must be on): PDF.js's saved-signature button. */
  function placeSignature(s: SavedSignature) {
    eventBus.dispatch('switchannotationeditorparams', {
      source: null,
      type: AnnotationEditorParamsType.CREATE,
      value: {
        signatureData: {
          lines: { curves: s.lines.curves, thickness: s.lines.thickness, width: s.lines.width, height: s.lines.height },
          mustSmooth: false,
          areContours: s.lines.areContours,
          description: s.description,
          uuid: s.uuid,
          heightInPage: SIGNATURE_HEIGHT_IN_PAGE,
        },
      },
    })
  }
  /** Start a new signature: PDF.js makes an empty signature editor, which asks us (getSignature). */
  const newSignature = () => eventBus.dispatch('switchannotationeditorparams', { source: null, type: AnnotationEditorParamsType.CREATE, value: null })

  /** The open signature dialog: the empty editor waiting for its signature. */
  const signatureDialog = shallowRef<{ uiManager: Obj; editor: Obj } | null>(null)
  /** The open "edit description" dialog of a placed signature. */
  const descriptionDialog = shallowRef<{ editor: Obj } | null>(null)

  const signatureManager = markRaw({
    loadSignatures: () => loadSignatures(),
    getSignature({ uiManager, editor }: { uiManager: Obj; editor: Obj }) {
      uiManager?.removeEditListeners?.()
      signatureDialog.value = { uiManager: markRaw(uiManager), editor: markRaw(editor) }
    },
    /** The "edit description" button in a signature's own toolbar. */
    async renderEditButton(editor: Obj) {
      const doc: Document = editor.div?.ownerDocument ?? editor.parent?.div?.ownerDocument
      const button = doc.createElement('button')
      button.classList.add('altText', 'editDescription')
      button.tabIndex = 0
      if (editor.description) button.title = editor.description
      const span = doc.createElement('span')
      span.textContent = t('pdfReader.edit.signature.editDescription')
      button.append(span)
      button.addEventListener('click', () => {
        editor._uiManager?.removeEditListeners?.()
        descriptionDialog.value = { editor: markRaw(editor) }
      }, { passive: true })
      return button
    },
    destroy() {
      closeSignatureDialog()
      descriptionDialog.value = null
    },
  })

  /** Close the signature dialog; an editor that got no signature is removed (as PDF.js). */
  function closeSignatureDialog() {
    const d = signatureDialog.value
    if (!d) return
    signatureDialog.value = null
    if (d.editor._drawId === null) d.editor.remove()
    d.uiManager?.addEditListeners?.()
  }
  function closeDescriptionDialog(description?: string) {
    const d = descriptionDialog.value
    if (!d) return
    descriptionDialog.value = null
    if (description !== undefined) d.editor.description = description
    d.editor._uiManager?.addEditListeners?.()
  }

  // ---- comments ----
  /** PDF.js's UI manager (handed to us at its creation): the notes tool adds editors through it. */
  let uiManager: Obj = null
  /** Bumped on every comment change: the side panel re-reads. */
  const commentsVersion = shallowRef(0)
  const changed = () => {
    commentsVersion.value++
    eventBus.dispatch('kkcommentschanged', { source: null })
  }

  /** The comment dialog (add / edit a comment's text). `pendingNote`: a new note removed if left empty. */
  const commentDialog = shallowRef<{ uiManager: Obj; editor: Obj; text: string; isNew: boolean } | null>(null)
  /** The comment popup over the page (hover or selected). */
  const popup = reactive({ editor: null as Obj, selected: false, editable: true, x: 0, y: 0, text: '', date: null as Date | null })
  const pendingNotes = new WeakSet<object>()

  function dataOf(editor: Obj): { str: string; date: Date | null } {
    try {
      const d = editor.getData()
      const raw = d.modificationDate || d.creationDate
      const date = raw instanceof Date ? raw : typeof raw === 'string' ? parsePdfDate(raw) : null
      return { str: d.contentsObj?.str || d.richText?.str || '', date }
    } catch {
      const c = editor.comment
      return { str: (typeof c === 'string' ? c : c?.text) || '', date: c?.date ? new Date(c.date) : null }
    }
  }

  function hidePopup() {
    popup.editor?.setCommentButtonStates?.({ selected: false, hasPopup: false })
    popup.editor = null
    popup.selected = false
  }

  const commentManager = markRaw({
    dialogElement: null as HTMLElement | null,
    setSidebarUiManager(ui: Obj) {
      uiManager = ui ? markRaw(ui) : null
    },
    showSidebar: changed,
    hideSidebar() {},
    removeComments: changed,
    selectComment() {},
    addComment: changed,
    updateComment: changed,
    /** PDF.js's CommentPopup.toggle: hover shows, a click pins (selected), a second click hides. */
    toggleCommentPopup(editor: Obj, isSelected: boolean, visibility?: boolean, isEditable = true) {
      if (!editor) return hidePopup()
      if (isSelected) visibility ??= popup.editor === editor ? !popup.selected : true
      else {
        if (popup.selected) return
        visibility ??= !popup.editor
      }
      if (!visibility) return hidePopup()
      if (popup.editor && popup.editor !== editor) popup.editor.setCommentButtonStates?.({ selected: false, hasPopup: false })
      const { str, date } = dataOf(editor)
      const r: DOMRect = editor.parentBoundingClientRect
      const [px, py] = editor.commentPopupPosition ?? [0, 0]
      popup.editor = markRaw(editor)
      popup.selected = isSelected
      popup.editable = isEditable
      popup.text = str
      popup.date = date
      popup.x = r.x + px * r.width
      popup.y = r.y + py * r.height
      editor.setCommentButtonStates?.({ selected: isSelected, hasPopup: true })
    },
    destroyPopup: hidePopup,
    updatePopupColor() {},
    showDialog(ui: Obj, editor: Obj) {
      ui?.removeEditListeners?.()
      hidePopup()
      commentDialog.value = { uiManager: ui ? markRaw(ui) : null, editor: markRaw(editor), text: dataOf(editor).str, isNew: pendingNotes.has(editor) }
    },
    makeCommentColor(color: number[] | null, opacity?: number) {
      return color ? findContrastColor(applyOpacity(color as [number, number, number], opacity ?? 1), (CSSConstants as Obj).commentForegroundColor) : null
    },
    destroy() {
      hidePopup()
      commentDialog.value = null
    },
  })

  /** Save (text) or cancel (undefined) the comment dialog. A new note left empty is removed. */
  function closeCommentDialog(text?: string) {
    const d = commentDialog.value
    if (!d) return
    commentDialog.value = null
    pendingNotes.delete(d.editor)
    if (text !== undefined && text.trim()) d.editor.comment = text
    else if (text !== undefined && !text.trim() && !d.isNew) d.editor.comment = null
    else if (d.isNew) d.editor.remove()
    d.uiManager?.addEditListeners?.()
    changed()
  }
  /** The popup's Delete: as PDF.js, undoable through the UI manager when it can. */
  function deletePopupComment() {
    const editor = popup.editor
    if (!editor) return
    const saved = editor.comment
    hidePopup()
    if (saved?.text && editor._uiManager?.deleteComment) editor._uiManager.deleteComment(editor, saved)
    else editor.comment = null
    changed()
  }
  function editPopupComment() {
    const editor = popup.editor
    if (!editor) return
    hidePopup()
    editor.editComment?.({})
  }

  /**
   * The notes tool: a click on a page's editor layer (image editor on) adds a small note image
   * there and opens its comment - a PDF.js stamp with a comment, saved in the file as such.
   */
  function addNoteAt(layerDiv: HTMLElement, clientX: number, clientY: number, bitmapUrl: string) {
    const pageDiv = layerDiv.closest<HTMLElement>('.page')
    const pageIndex = Number(pageDiv?.dataset.pageNumber) - 1
    const layer = uiManager?.getLayer?.(pageIndex)
    if (!layer || Number.isNaN(pageIndex)) return
    const rect = layerDiv.getBoundingClientRect()
    const editor = layer.createAndAddNewEditor({ offsetX: clientX - rect.left, offsetY: clientY - rect.top }, false, { bitmapUrl })
    if (!editor) return
    pendingNotes.add(editor)
    commentManager.showDialog(uiManager, editor)
  }

  return {
    signatureManager, commentManager,
    saved, loadSignatures, saveSignature, deleteSignature, placeSignature, newSignature,
    signatureDialog, closeSignatureDialog, descriptionDialog, closeDescriptionDialog,
    commentDialog, closeCommentDialog, popup, hidePopup, deletePopupComment, editPopupComment,
    commentsVersion, addNoteAt,
    canSaveMore: () => stored.value.length < MAX_SAVED_SIGNATURES,
  }
}

/** PDF date "D:YYYYMMDDHHmmSS" to a Date. */
function parsePdfDate(s: string): Date | null {
  const m = /^(?:D:)?(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?(\d{2})?/.exec(s)
  if (!m) return null
  const d = new Date(Number(m[1]), Number(m[2] ?? 1) - 1, Number(m[3] ?? 1), Number(m[4] ?? 0), Number(m[5] ?? 0), Number(m[6] ?? 0))
  return Number.isNaN(d.getTime()) ? null : d
}

export type PdfEditManagers = ReturnType<typeof createPdfEditManagers>
