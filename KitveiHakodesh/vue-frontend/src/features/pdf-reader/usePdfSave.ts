import { computed, shallowRef, watch } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { t } from '@/i18n'
import { showToast, dismissToast } from '@/composables/useToast'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { savePdfAs, writePdfFile } from '@/webview-host/bridge'
import type { PdfScroller } from './usePdfScroller'
import type { PdfOutlineEdit } from './usePdfOutlineEdit'

/**
 * Save (in place) and Save As for the PDF view, with the document's dirty state.
 *
 * Dirty = annotation edits (PDF.js's annotationStorage "modified" signal) or outline edits.
 * A save writes PDF.js's saveDocument() bytes (the whole file, atomically, through the host /
 * service), then reopens those bytes at the same page and zoom; the reopened document starts
 * clean, so dirty clears only after a completed write.
 *
 * Outline edits: stock pdfjs-dist's saveDocument() cannot write an edited outline. When the
 * engine supports saveDocument({ outline }) (our fork, KleiKodesh/pdf.js) it is passed;
 * otherwise the save tells the reader the outline changes were not kept.
 */
export function usePdfSave(
  pdf: PdfScroller,
  outlineEdit: PdfOutlineEdit,
  file: {
    filePath: () => string | null
    fileName: () => string | null
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

  const dirty = computed(() => annotationsDirty.value || outlineEdit.dirty.value)
  const canSave = computed(() => !!pdf.pdfDocument.value && !!file.filePath() && dirty.value && !busy.value)
  const canSaveAs = computed(() => !!pdf.pdfDocument.value && !busy.value)

  /** True when this engine's saveDocument takes ({ outline }) - our fork; stock 6.5 takes (printToPDF). */
  function engineSavesOutline(doc: PDFDocumentProxy): boolean {
    const transport = (doc as unknown as { _transport?: { saveDocument?: unknown } })._transport
    return /\boutline\b/.test(String(transport?.saveDocument ?? ''))
  }

  /** The bytes to write, and whether pending outline edits are left out of them. */
  async function buildBytes(doc: PDFDocumentProxy): Promise<{ bytes: Uint8Array; outlineDropped: boolean }> {
    const outline = outlineEdit.buildSaveOutline()
    if (outline && engineSavesOutline(doc)) {
      // TODO(pdf.js fork): once the app runs the fork build (vue-frontend/pdf.js) instead of
      // pdfjs-dist, also pass mergeIntoLastUpdate: true (one incremental section per session).
      const save = doc.saveDocument as unknown as (p: { outline: unknown }) => Promise<Uint8Array>
      return { bytes: await save.call(doc, { outline }), outlineDropped: false }
    }
    return { bytes: await doc.saveDocument(), outlineDropped: !!outline }
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

  function savedToast(outlineDropped: boolean) {
    showToast(outlineDropped ? t('pdfReader.saveDoc.savedOutlineNotSaved') : t('pdfReader.saveDoc.saved'))
  }

  /** Save into the open file. No file (a downloaded or embedded document): Save As. True once written. */
  async function save(): Promise<boolean> {
    const doc = pdf.pdfDocument.value
    const path = file.filePath()
    if (!doc) return false
    if (!path) return saveAs()
    if (!dirty.value) return true
    // Only outline edits, and the engine cannot write them: nothing would change in the file.
    if (!annotationsDirty.value && outlineEdit.dirty.value && !engineSavesOutline(doc)) {
      showToast(t('pdfReader.saveDoc.outlineNotSaved'))
      return false
    }
    return run(async () => {
      const { bytes, outlineDropped } = await buildBytes(doc)
      const error = await writePdfFile(path, bytes)
      if (error) throw new Error(error)
      await reopen(bytes)
      savedToast(outlineDropped)
      return true
    })
  }

  /** Save to a file the reader picks; the tab then shows that file. True once written. */
  async function saveAs(): Promise<boolean> {
    const doc = pdf.pdfDocument.value
    if (!doc) return false
    return run(async () => {
      const { bytes, outlineDropped } = await buildBytes(doc)
      const result = await savePdfAs(bytes, suggestedName(), baseDir(file.filePath()))
      if (result.kind === 'cancelled') return dismissToast(), false
      if (result.kind === 'error') throw new Error(result.error)
      if (result.kind === 'unsupported') {
        // No host dialog (dev browser): the browser's own save. The tab keeps its file.
        if (!(await browserSaveAs(bytes))) return dismissToast(), false
        savedToast(outlineDropped)
        return true
      }
      await file.onSavedAs(result.filePath, result.fileName)
      await reopen(bytes)
      savedToast(outlineDropped)
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

  return { dirty, busy, canSave, canSaveAs, save, saveAs }
}

export type PdfSave = ReturnType<typeof usePdfSave>
