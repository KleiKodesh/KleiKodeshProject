import { computed, shallowRef, watch } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { t } from '@/i18n'
import { showToast, dismissToast } from '@/composables/useToast'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { savePdfAs, writePdfFile, writePdfTail } from '@/webview-host/bridge'
import type { PdfScroller } from './usePdfScroller'
import type { PdfOutlineEdit } from './usePdfOutlineEdit'
import type { PdfNotes } from './usePdfNotes'

/** saveDocument({ sectionOnly }) in our PDF.js fork (KleiKodesh/pdf.js): the update section only. */
interface SaveSection { data: Uint8Array; start: number; length: number; anchor: Uint8Array }
interface ForkSaveParams { outline?: unknown; mergeIntoLastUpdate?: boolean; sectionOnly?: boolean }
type ForkSave = {
  (p: ForkSaveParams & { sectionOnly: true }): Promise<SaveSection>
  (p: ForkSaveParams): Promise<Uint8Array>
}

/**
 * Save (in place) and Save As for the PDF view, with the document's dirty state.
 *
 * Dirty = annotation edits (PDF.js's annotationStorage "modified" signal), notes (usePdfNotes,
 * staged into annotationStorage so PDF.js writes them as sticky notes) or outline edits
 * (saveDocument({ outline })) - all written by PDF.js's own save, in one update section.
 *
 * Save in place: PDF.js returns only its update section (sectionOnly), merged into the file's
 * last update section (mergeIntoLastUpdate: repeated saves rewrite the app's own section instead
 * of adding one each time). Only that section travels to the host / service, which writes it at
 * its offset after checking the bytes before it (writePdfTail); the whole file is never held
 * twice. The view then reopens the file from disk at the same page and zoom.
 * Save As writes the whole file.
 *
 * Page edits from the thumbnails panel (usePdfPageEdits) open a new document built from the old
 * one's pages: that document is dirty as a whole, and Save writes the whole file (writePdfFile).
 */
export function usePdfSave(
  pdf: PdfScroller,
  outlineEdit: PdfOutlineEdit,
  notes: Pick<PdfNotes, 'dirty' | 'stage'>,
  file: {
    filePath: () => string | null
    fileName: () => string | null
    /** The URL the view reads the open file from (reopened after a save in place). */
    url: () => string | null
    /** Save As wrote a new file: point the tab at it (before the view reopens the bytes). */
    onSavedAs: (filePath: string, fileName: string) => Promise<void> | void
  },
) {
  const hostWindow = useHostWindow()
  const annotationsDirty = shallowRef(false)
  const busy = shallowRef(false)

  watch(pdf.pdfDocument, (doc) => {
    annotationsDirty.value = false
    if (!doc) return
    const storage = doc.annotationStorage as unknown as { onSetModified: (() => void) | null; onResetModified: (() => void) | null }
    const prevSet = storage.onSetModified
    const prevReset = storage.onResetModified
    storage.onSetModified = () => { annotationsDirty.value = true; prevSet?.() }
    storage.onResetModified = () => { annotationsDirty.value = false; prevReset?.() }
  }, { immediate: true })

  /** The open document came from page edits (deleted / pasted pages), not from the file. */
  const pagesEditedDoc = shallowRef<PDFDocumentProxy | null>(null)
  const pagesEdited = computed(() => !!pagesEditedDoc.value && pagesEditedDoc.value === pdf.pdfDocument.value)
  /** Unsaved edits on the pages' content (marks, notes, outline) - the ones only PDF.js's save writes. */
  const contentDirty = computed(() => annotationsDirty.value || notes.dirty.value || outlineEdit.dirty.value)
  const dirty = computed(() => contentDirty.value || pagesEdited.value)
  const canSave = computed(() => !!pdf.pdfDocument.value && !!file.filePath() && dirty.value && !busy.value)
  const canSaveAs = computed(() => !!pdf.pdfDocument.value && !busy.value)

  /** PDF.js's save, with the notes staged in only for it (the notes list stays their source). */
  async function engineSave<T>(doc: PDFDocumentProxy, params: ForkSaveParams): Promise<T> {
    const unstage = notes.stage(doc)
    try {
      const outline = outlineEdit.buildSaveOutline()
      const save = doc.saveDocument as unknown as ForkSave
      return (await save.call(doc, { ...params, mergeIntoLastUpdate: true, ...(outline ? { outline } : {}) })) as T
    } finally {
      unstage()
    }
  }

  function baseDir(path: string | null): string | undefined {
    if (!path) return undefined
    const i = Math.max(path.lastIndexOf('\\'), path.lastIndexOf('/'))
    return i > 0 ? path.slice(0, i) : undefined
  }

  function suggestedName(): string {
    const name = file.fileName() || 'document.pdf'
    return /\.pdf$/i.test(name) ? name : `${name}.pdf`
  }

  /** Reopen the written bytes where the reader was; the new document starts clean. */
  async function reopen(bytes: Uint8Array) {
    await pdf.open(bytes, pdf.viewState())
  }
  /** Reopen the file from disk where the reader was (a fresh URL: no cached read of the old bytes). */
  async function reopenFromDisk(url: string) {
    const fresh = `${url}${url.includes('?') ? '&' : '?'}kk-saved=${Date.now()}`
    await pdf.open(fresh, pdf.viewState())
  }

  async function run(work: () => Promise<boolean>): Promise<boolean> {
    if (busy.value) return false
    busy.value = true
    showToast(t('pdfReader.saveDoc.saving'), { duration: 0 })
    try {
      return await work()
    } catch (err) {
      console.error('[PdfReader] save failed', err)
      showToast(t('pdfReader.saveDoc.failed', { error: err instanceof Error ? err.message : String(err) }))
      return false
    } finally {
      busy.value = false
    }
  }

  /** Save into the open file. No file (a downloaded or embedded document): Save As. True once written. */
  async function save(): Promise<boolean> {
    const doc = pdf.pdfDocument.value
    const path = file.filePath()
    const url = file.url()
    if (!doc) return false
    if (!path || !url) return saveAs()
    if (!dirty.value) return true
    return run(async () => {
      if (pagesEdited.value) {
        // A document built from page edits: the whole file is new.
        const bytes = await engineSave<Uint8Array>(doc, {})
        const error = await writePdfFile(path, bytes)
        if (error) throw new Error(error)
        await reopenFromDisk(url)
        showToast(t('pdfReader.saveDoc.saved'))
        return true
      }
      const section = await engineSave<SaveSection>(doc, { sectionOnly: true })
      if (section.data.length) {
        const error = await writePdfTail(path, section.start, section.data, section.anchor, section.length)
        if (error) throw new Error(error)
      }
      await reopenFromDisk(url)
      showToast(t('pdfReader.saveDoc.saved'))
      return true
    })
  }

  /** Save to a file the reader picks; the tab then shows that file. True once written. */
  async function saveAs(): Promise<boolean> {
    const doc = pdf.pdfDocument.value
    if (!doc) return false
    return run(async () => {
      const bytes = await engineSave<Uint8Array>(doc, {})
      const result = await savePdfAs(bytes, suggestedName(), baseDir(file.filePath()))
      if (result.kind === 'cancelled') return dismissToast(), false
      if (result.kind === 'error') throw new Error(result.error)
      if (result.kind === 'unsupported') {
        // No host dialog (dev browser): the browser's own save. The tab keeps its file.
        if (!(await browserSaveAs(bytes))) return dismissToast(), false
        showToast(t('pdfReader.saveDoc.saved'))
        return true
      }
      await file.onSavedAs(result.filePath, result.fileName)
      await reopen(bytes)
      showToast(t('pdfReader.saveDoc.saved'))
      return true
    })
  }

  async function browserSaveAs(bytes: Uint8Array): Promise<boolean> {
    const blob = new Blob([bytes as BlobPart], { type: 'application/pdf' })
    const picker = (hostWindow as unknown as { showSaveFilePicker?: (o: unknown) => Promise<FileSystemFileHandle> }).showSaveFilePicker
    if (picker) {
      let handle: FileSystemFileHandle
      try {
        handle = await picker.call(hostWindow, { suggestedName: suggestedName(), types: [{ description: 'PDF', accept: { 'application/pdf': ['.pdf'] } }] })
      } catch {
        return false // cancelled
      }
      const writable = await handle.createWritable()
      await writable.write(blob)
      await writable.close()
      return true
    }
    const url = URL.createObjectURL(blob)
    const a = hostWindow.document.createElement('a')
    a.href = url
    a.download = suggestedName()
    hostWindow.document.body.append(a)
    a.click()
    a.remove()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
    return true
  }

  /** The open document's bytes as they now are, unsaved marks, notes and outline included. */
  async function currentBytes(): Promise<Uint8Array> {
    const doc = pdf.pdfDocument.value
    if (!doc) throw new Error('no document')
    return contentDirty.value ? engineSave<Uint8Array>(doc, {}) : doc.getData()
  }
  /** Show a document built by page edits where the reader was; `edited` = it differs from the file. */
  async function showEditedPages(bytes: Uint8Array, edited = true, keep = pdf.viewState()) {
    await pdf.open(bytes, keep)
    pagesEditedDoc.value = edited ? pdf.pdfDocument.value : null
  }

  return { dirty, contentDirty, busy, canSave, canSaveAs, save, saveAs, currentBytes, showEditedPages }
}

export type PdfSave = ReturnType<typeof usePdfSave>
