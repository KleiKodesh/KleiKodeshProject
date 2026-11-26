import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { t } from '@/i18n'
import { showToast } from '@/composables/useToast'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { savePdfAs } from '@/webview-host/bridge'
import { PRIMARY_SOURCE, assemblePdf, openPdfSource, type OrganizerPage, type OrganizerSource } from '@/features/pdf-organizer/pdfPageAssembly'
import { baseName, pdfBlob, saveBlobAs } from '@/features/pdf-organizer/organizerFiles'
import type { PdfScroller } from './usePdfScroller'
import type { PdfSave } from './usePdfSave'

/**
 * Page selection and the "Manage" menu of the thumbnails panel - the old PDF viewer's views
 * manager (PDFThumbnailViewer with enableSplitMerge): select pages, then copy / cut / delete
 * them or export them to a new file. Copy and cut enter paste mode (paste buttons between the
 * thumbnails); delete and cut show an undo bar, as do copies (whose undo leaves paste mode).
 *
 * Where PDF.js edits a page map and writes it on save, here each edit builds the new document
 * at once with the organizer's engine (assemblePdf: PDF.js's extractPages) and the view shows
 * it; the document is then dirty and Save writes it (usePdfSave).
 *
 * Nothing holds the file's bytes: each build copies only the pages it needs from the open
 * document (unsaved marks, notes and outline included) and from a small side document of the
 * pages taken away (cut / copied / deleted pages, built once from those pages only and kept in
 * the PDF.js worker). Undo rebuilds from the open document and those pages; an undo back to
 * the file as it was reopens the file itself.
 */
export type PageEditStatus = 'copy' | 'cut' | 'delete'

export function usePdfPageEdits(
  pdf: PdfScroller,
  saving: PdfSave,
  file: { fileName: () => string | null; filePath: () => string | null; url: () => string | null },
) {
  const hostWindow = useHostWindow()
  /** Selected page numbers (1-based). */
  const selected = ref(new Set<number>())
  /** Copied / cut pages: a side document of just those pages (in the open document's worker). */
  const clipboard = shallowRef<{ doc: PDFDocumentProxy; count: number } | null>(null)
  /** The undo bar ("N pages deleted"), or null for the selection status. */
  const undoBar = shallowRef<{ kind: PageEditStatus; count: number } | null>(null)
  /**
   * What the undo of a delete / cut brings back: the removed pages (a side document, in order)
   * and the positions they had; `edited` = the document differed from the file before.
   */
  let undoState: { removed: PDFDocumentProxy; positions: number[]; edited: boolean } | null = null
  let isCut = false
  const busy = shallowRef(false)

  const pageCount = computed(() => pdf.pdfDocument.value?.numPages ?? 0)
  const pasteMode = computed(() => !!clipboard.value)
  const size = computed(() => selected.value.size)
  /** Never every page: a PDF keeps at least one. */
  const canDelete = computed(() => size.value > 0 && size.value < pageCount.value)
  const allSelected = computed(() => pageCount.value > 0 && size.value >= pageCount.value)
  const someSelected = computed(() => size.value > 0 && size.value < pageCount.value)

  // A new document (an edit, a save, another file): page numbers mean other pages.
  watch(pdf.pdfDocument, () => { selected.value = new Set() })
  // Another file in this view: nothing to paste or undo from the old one.
  watch(file.url, () => {
    setClipboard(null)
    setUndo(null)
    undoBar.value = null
    isCut = false
  })

  /** Side documents are closed when nothing refers to them any more. */
  function closeIfUnused(doc: PDFDocumentProxy | null | undefined) {
    if (doc && doc !== clipboard.value?.doc && doc !== undoState?.removed) void doc.loadingTask.destroy()
  }
  function setClipboard(next: { doc: PDFDocumentProxy; count: number } | null) {
    const old = clipboard.value?.doc
    clipboard.value = next
    closeIfUnused(old)
  }
  function setUndo(next: typeof undoState) {
    const old = undoState?.removed
    undoState = next
    closeIfUnused(old)
  }

  function clearSelection() {
    selected.value = new Set()
  }
  // Closing the viewer frees the side documents (copied pages, pages kept for undo): they live in
  // the shared PDF.js worker, which outlives this view.
  onBeforeUnmount(() => {
    setClipboard(null)
    setUndo(null)
  })
  function dismissUndo() {
    setClipboard(null)
    setUndo(null)
    isCut = false
    undoBar.value = null
  }
  function selectPage(pageNumber: number, checked: boolean) {
    if (undoBar.value) dismissUndo()
    const set = new Set(selected.value)
    if (checked) set.add(pageNumber)
    else set.delete(pageNumber)
    selected.value = set
  }
  function toggleSelectAll(checked: boolean) {
    if (!pageCount.value) return
    if (checked) {
      if (undoBar.value) dismissUndo()
      selected.value = new Set(Array.from({ length: pageCount.value }, (_, i) => i + 1))
    } else clearSelection()
  }

  /** A new PDF of `pages` (sources: the open document as it now is, plus `extra`). */
  async function build(doc: PDFDocumentProxy, pages: OrganizerPage[], extra: [number, PDFDocumentProxy][] = []): Promise<Uint8Array> {
    const sources = primaryOnly(doc)
    for (const [id, d] of extra) sources.set(id, { id, doc: d, name: '' })
    const list = pages.map((p, key) => ({ ...p, key }))
    return saving.withEditsStaged(doc, (options) => assemblePdf(doc, list, sources, options))
  }
  /** The given pages of the open document as a side document (only those pages are copied). */
  async function sideDocument(doc: PDFDocumentProxy, indices: number[]): Promise<PDFDocumentProxy> {
    return openPdfSource({ bytes: await build(doc, pagesOf(indices)) }, doc)
  }
  function currentDocument(): PDFDocumentProxy {
    const doc = pdf.pdfDocument.value
    if (!doc) throw new Error('no document')
    return doc
  }
  const primaryOnly = (doc: PDFDocumentProxy) => new Map<number, OrganizerSource>([[PRIMARY_SOURCE, { id: PRIMARY_SOURCE, doc, name: '' }]])
  const SIDE_SOURCE = PRIMARY_SOURCE + 1
  const pagesOf = (indices: number[], source = PRIMARY_SOURCE): OrganizerPage[] =>
    indices.map((pageIndex, key) => ({ key, source, pageIndex, rotation: 0 }))
  const sortedSelection = () => [...selected.value].sort((a, b) => a - b)

  async function run(work: () => Promise<void>) {
    if (busy.value || saving.busy.value) return
    busy.value = true
    try {
      await work()
    } catch (err) {
      console.error('[PdfReader] page edit failed', err)
      showToast(t('pdfReader.pageManage.failed', { error: err instanceof Error ? err.message : String(err) }), { variant: 'error' })
    } finally {
      busy.value = false
    }
  }

  /** Copy: the selected pages wait to be pasted (paste mode); the undo bar says how many. */
  async function copyPages(clear = true): Promise<PDFDocumentProxy | null> {
    if (!size.value) return null
    const indices = sortedSelection().map((n) => n - 1)
    const side = await sideDocument(currentDocument(), indices)
    setClipboard({ doc: side, count: indices.length })
    undoBar.value = { kind: isCut ? 'cut' : 'copy', count: indices.length }
    if (!isCut) setUndo(null)
    if (clear) clearSelection()
    return side
  }

  /** Delete the selected pages (never all of them); the undo bar brings them back. */
  async function deletePages(kind: 'delete' | 'cut' = 'delete', taken: PDFDocumentProxy | null = null) {
    if (!canDelete.value) return
    const doc = currentDocument()
    const gone = new Set(selected.value)
    const count = gone.size
    const edited = saving.dirty.value
    const view = pdf.viewState()
    // Where the reader was: the same page when it stays, else the first page after the deleted ones.
    const all = Array.from({ length: pageCount.value }, (_, i) => i + 1)
    const remaining = all.filter((n) => !gone.has(n))
    const removedNumbers = all.filter((n) => gone.has(n))
    const at = remaining.findIndex((n) => n >= view.pageNumber)
    const newIndex = at < 0 ? remaining.length - 1 : at
    const keep = remaining[newIndex] === view.pageNumber
      ? { ...view, pageNumber: newIndex + 1 }
      : { ...view, pageNumber: newIndex + 1, top: undefined, left: undefined }
    // The removed pages for the undo: the cut's clipboard document, or one built for them.
    const removed = taken ?? (await sideDocument(doc, removedNumbers.map((n) => n - 1)))
    const after = await build(doc, pagesOf(remaining.map((n) => n - 1)))
    await saving.showEditedPages(after, true, keep)
    setUndo({ removed, positions: removedNumbers.map((n) => n - 1), edited })
    undoBar.value = { kind, count }
  }

  /** Cut = copy (keeping the selection) then delete; nothing when every page is selected. */
  async function cutPages() {
    if (!canDelete.value) return
    isCut = true
    const taken = await copyPages(false)
    await deletePages('cut', taken)
  }

  /** Paste the copied / cut pages before index `index` (0 = before the first page). */
  async function pastePages(index: number) {
    const clip = clipboard.value
    if (!clip) return
    const doc = currentDocument()
    const own = Array.from({ length: pageCount.value }, (_, i) => i)
    const pasted = pagesOf(Array.from({ length: clip.count }, (_, i) => i), SIDE_SOURCE)
    const keep = pdf.viewState()
    const after = await build(doc, [...pagesOf(own.slice(0, index)), ...pasted, ...pagesOf(own.slice(index))], [[SIDE_SOURCE, clip.doc]])
    // The old viewer goes to the first pasted page.
    await saving.showEditedPages(after, true, { ...keep, pageNumber: index + 1, top: undefined, left: undefined })
    setClipboard(null)
    isCut = false
    setUndo(null)
    undoBar.value = null
  }

  /** Undo of the bar: leaves paste mode, and brings deleted / cut pages back. */
  async function undo() {
    clearSelection()
    const state = undoState
    undoBar.value = null
    isCut = false
    setClipboard(null)
    if (!state) return
    try {
      const url = file.url()
      if (!state.edited && url) {
        // Back to the file as it is on disk: reopen it (nothing to build).
        await pdf.open(`${url}${url.includes('?') ? '&' : '?'}kk-undo=${Date.now()}`, pdf.viewState())
        return
      }
      // The open pages with the removed ones back at their places.
      const doc = currentDocument()
      const total = pageCount.value + state.positions.length
      const back = new Map(state.positions.map((pos, i) => [pos, i]))
      const list: OrganizerPage[] = []
      for (let pos = 0, own = 0; pos < total; pos++) {
        const r = back.get(pos)
        list.push(r != null ? { key: 0, source: SIDE_SOURCE, pageIndex: r, rotation: 0 } : { key: 0, source: PRIMARY_SOURCE, pageIndex: own++, rotation: 0 })
      }
      await saving.showEditedPages(await build(doc, list, [[SIDE_SOURCE, state.removed]]), true)
    } finally {
      setUndo(null)
    }
  }

  /** "Export selected...": the selected pages, in order, as a new file the reader names. */
  async function exportSelected() {
    if (!size.value) return
    const indices = sortedSelection().map((n) => n - 1)
    // Only the selected pages are read and written.
    const bytes = await build(currentDocument(), pagesOf(indices))
    clearSelection()
    const name = `${baseName(file.fileName())} - ${t('pdfOrganizer.extractSuffix')}.pdf`
    const path = file.filePath()
    const i = path ? Math.max(path.lastIndexOf('\\'), path.lastIndexOf('/')) : -1
    const result = await savePdfAs({ bytes }, name, i > 0 ? path!.slice(0, i) : undefined)
    if (result.kind === 'unsupported') {
      if (await saveBlobAs(hostWindow, pdfBlob(bytes), name)) showToast(t('pdfReader.saveDoc.saved'))
    } else if (result.kind === 'error') throw new Error(result.error)
    else if (result.kind === 'saved') showToast(t('pdfReader.saveDoc.saved'))
  }

  return {
    selected, size, pageCount, allSelected, someSelected, canDelete, pasteMode, undoBar, busy,
    selectPage, toggleSelectAll, dismissUndo,
    copy: () => run(async () => { await copyPages() }),
    cut: () => run(cutPages),
    remove: () => run(() => deletePages('delete')),
    paste: (index: number) => run(() => pastePages(index)),
    undo: () => run(undo),
    exportSelected: () => run(exportSelected),
  }
}

export type PdfPageEdits = ReturnType<typeof usePdfPageEdits>
