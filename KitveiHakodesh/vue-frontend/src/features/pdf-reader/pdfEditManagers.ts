import { markRaw, reactive, shallowRef } from 'vue'
import { useLocalStorage } from '@vueuse/core'
import { AnnotationEditorParamsType, CSSConstants, PDFDateString, SignatureExtractor, applyOpacity, findContrastColor } from 'pdfjs-dist'
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

  // ---- comments (PDF.js's web/comment_manager.js: CommentManager, CommentDialog, CommentPopup) ----
  /** PDF.js's UI manager (handed to us at its creation): its undo stack also holds our notes' steps. */
  let uiManager: Obj = null
  /** Bumped on every comment change: the side panel re-reads. */
  const commentsVersion = shallowRef(0)
  const changed = () => {
    commentsVersion.value++
    eventBus.dispatch('kkcommentschanged', { source: null })
  }

  /** The annotation's colour behind a comment (dialog, popup), as PDF.js: null = the theme's. */
  function commentColor(color: number[] | null | undefined, opacity?: number): string | null {
    return color ? findContrastColor(applyOpacity(color as [number, number, number], opacity ?? 1), (CSSConstants as Obj).commentForegroundColor) : null
  }
  const hex = (c: number[]) => '#' + c.map((v) => v.toString(16).padStart(2, '0')).join('')

  /**
   * The comment dialog (PDF.js's CommentDialog): opened next to the comment button, at window
   * point (x, y) in px.
   */
  const commentDialog = shallowRef<{
    uiManager: Obj; editor: Obj; text: string
    x: number; y: number; height: number | null; parent: DOMRect | null; defaultPosition: boolean
    background: string | null; border: string | null
  } | null>(null)
  /** The <dialog> element (PdfEditDialogs.vue); the editors' comment buttons point at it (aria-controls). */
  let dialogEl: HTMLElement | null = null
  const setDialogElement = (el: HTMLElement | null) => { dialogEl = el }

  /** The comment popup (PDF.js's CommentPopup): drawn inside the page, after the annotation, at x / y (fractions of the page). */
  const popup = reactive({
    editor: null as Obj, host: null as HTMLElement | null, selected: false, editable: true,
    x: 0, y: 0, correct: false, text: '', date: null as Date | null, background: '' as string,
  })

  function dataOf(editor: Obj): { str: string; date: Date | null; color: number[] | null; opacity?: number } {
    try {
      const d = editor.getData()
      const raw = d.modificationDate || d.creationDate
      const date = raw instanceof Date ? raw : typeof raw === 'string' ? parsePdfDate(raw) : null
      const str = d.richText?.str && (!d.contentsObj?.str || d.richText.str === d.contentsObj.str) ? d.richText.str : d.contentsObj?.str
      return { str: str || '', date, color: d.color ?? null, opacity: d.opacity }
    } catch {
      const c = editor.comment
      return { str: (typeof c === 'string' ? c : c?.text) || '', date: c?.date ? new Date(c.date) : null, color: null }
    }
  }

  /** The comment still at its default place (PDF.js's; an editor with no comment yet has none to ask). */
  function hasDefaultPosition(editor: Obj): boolean {
    try {
      return editor.hasDefaultPopupPosition?.() ?? true
    } catch {
      return true
    }
  }

  function hidePopup() {
    popup.editor?.setCommentButtonStates?.({ selected: false, hasPopup: false })
    popup.editor = null
    popup.host = null
    popup.selected = false
  }

  const commentManager = markRaw({
    get dialogElement(): HTMLElement | null {
      return dialogEl
    },
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
      const { str, date, color, opacity } = dataOf(editor)
      const [x, y] = editor.commentPopupPosition ?? [0, 0]
      popup.editor = markRaw(editor)
      popup.host = (editor.elementBeforePopup as HTMLElement | null)?.parentElement ?? null
      popup.selected = isSelected
      popup.editable = isEditable
      popup.text = str
      popup.date = date
      popup.background = commentColor(color, opacity) ?? ''
      popup.x = x
      popup.y = y
      popup.correct = hasDefaultPosition(editor)
      editor.setCommentButtonStates?.({ selected: isSelected, hasPopup: true })
    },
    destroyPopup: hidePopup,
    updatePopupColor(editor: Obj) {
      if (popup.editor !== editor) return
      const { color, opacity } = dataOf(editor)
      popup.background = commentColor(color, opacity) ?? ''
    },
    showDialog(ui: Obj, editor: Obj, posX?: number, posY?: number, options?: { height?: number; parentDimensions?: DOMRect }) {
      ui?.removeEditListeners?.()
      hidePopup()
      const { str, color, opacity } = dataOf(editor)
      let x = posX
      let y = posY
      if (x === undefined || y === undefined) {
        // Not from a comment button: beside the annotation.
        const r = (editor.div as HTMLElement | undefined)?.getBoundingClientRect()
        x = r ? r.right : 0
        y = r ? r.bottom : 0
      }
      commentDialog.value = {
        uiManager: ui ? markRaw(ui) : null, editor: markRaw(editor), text: str,
        x, y, height: options?.height ?? null, parent: options?.parentDimensions ?? null,
        defaultPosition: hasDefaultPosition(editor),
        background: commentColor(color, opacity), border: color ? hex(color) : null,
      }
    },
    makeCommentColor: commentColor,
    destroy() {
      hidePopup()
      commentDialog.value = null
    },
  })

  /** Save (text) or cancel (undefined) the comment dialog, as PDF.js's. */
  function closeCommentDialog(text?: string) {
    const d = commentDialog.value
    if (!d) return
    commentDialog.value = null
    if (text !== undefined) d.editor.comment = text
    d.editor.focusCommentButton?.()
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
    else if (typeof saved === 'string' && saved && uiManager?.addCommands) {
      // A comment on the page's annotation layer (reading mode): its text is a plain string and
      // PDF.js adds no undo step for it - add one, so Ctrl+Z brings it back as for an editor's.
      uiManager.addCommands({
        cmd: () => { editor.comment = null; changed() },
        undo: () => { editor.comment = saved; changed() },
        mustExec: true,
      })
    } else editor.comment = null
    editor.focus?.()
    changed()
  }
  /** The popup's Edit: the dialog, as tall as the popup's text. */
  function editPopupComment(height?: number) {
    const editor = popup.editor
    if (!editor) return
    hidePopup()
    editor.editComment?.(height ? { height } : {})
  }

  /** Open the popup of a comment listed in the side panel (PDF.js's CommentSidebar click). */
  function selectComment(pageIndex: number, uid: string) {
    uiManager?.selectComment?.(pageIndex, uid)
  }
  const waitForEditorsRendered = (pageNumber: number): Promise<void> =>
    uiManager?.waitForEditorsRendered?.(pageNumber) ?? Promise.resolve()

  return {
    signatureManager, commentManager,
    saved, loadSignatures, saveSignature, deleteSignature, placeSignature, newSignature,
    signatureDialog, closeSignatureDialog, descriptionDialog, closeDescriptionDialog,
    commentDialog, closeCommentDialog, setDialogElement, popup, hidePopup, deletePopupComment, editPopupComment,
    commentsVersion, selectComment, waitForEditorsRendered,
    /** PDF.js's UI manager of the open document (its undo / redo stack), or null. */
    getUiManager: (): Obj => uiManager,
    canSaveMore: () => stored.value.length < MAX_SAVED_SIGNATURES,
  }
}

/** PDF date "D:YYYYMMDDHHmmSS+HH'mm'" to a Date (PDF.js's reading, time zone included). */
function parsePdfDate(s: string): Date | null {
  return PDFDateString.toDateObject(s)
}

export type PdfEditManagers = ReturnType<typeof createPdfEditManagers>
