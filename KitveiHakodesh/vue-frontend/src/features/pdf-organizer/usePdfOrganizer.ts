import { computed, ref, shallowRef, watch, type Ref } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { disposeThumbnailEngine } from '@/features/pdf-reader/pdfThumbnailEngine'
import { PRIMARY_SOURCE, assemblePdf, type OrganizerPage, type OrganizerSource } from './pdfPageAssembly'

const UNDO_LIMIT = 100

/** Another PDF's pages are gone from the organizer: its thumbnails and the file are freed. */
function closeSource(doc: PDFDocumentProxy) {
  disposeThumbnailEngine(doc)
  void doc.loadingTask.destroy()
}

/**
 * The organizer's state: the pages in their new order (with turns, copies, blanks and pages
 * of other files), the selection, and undo / redo. Nothing touches the document until the
 * pages are assembled (assemble) and written.
 */
export function usePdfOrganizer(document: Ref<PDFDocumentProxy | null>) {
  let nextKey = 1
  let nextSource = PRIMARY_SOURCE + 1
  const sources = shallowRef(new Map<number, OrganizerSource>())
  const pages = ref<OrganizerPage[]>([])
  const selected = ref(new Set<number>())
  /** The page a shift-click extends from. */
  let anchorKey: number | null = null
  const undoStack = ref<OrganizerPage[][]>([])
  const redoStack = ref<OrganizerPage[][]>([])
  /** The order the document has on disk: dirty = anything else. */
  let savedSignature = ''

  const signature = (list: OrganizerPage[]) =>
    list.map((p) => `${p.source}:${p.pageIndex}:${p.rotation}`).join(',')
  const dirty = computed(() => signature(pages.value) !== savedSignature)

  /** Start over from the document as it is (on open, and after it was saved and reloaded). */
  function reset(doc: PDFDocumentProxy | null) {
    for (const [id, s] of sources.value) if (id !== PRIMARY_SOURCE) closeSource(s.doc)
    const map = new Map<number, OrganizerSource>()
    pages.value = []
    if (doc) {
      map.set(PRIMARY_SOURCE, { id: PRIMARY_SOURCE, doc, name: '' })
      pages.value = Array.from({ length: doc.numPages }, (_, i) => ({ key: nextKey++, source: PRIMARY_SOURCE, pageIndex: i, rotation: 0 }))
    }
    sources.value = map
    selected.value = new Set()
    anchorKey = null
    undoStack.value = []
    redoStack.value = []
    savedSignature = signature(pages.value)
  }
  watch(document, reset, { immediate: true })

  // -- Undo / redo: a snapshot of the page list before each change.
  const clone = (list: OrganizerPage[]) => list.map((p) => ({ ...p }))
  function change(next: OrganizerPage[]) {
    undoStack.value.push(clone(pages.value))
    if (undoStack.value.length > UNDO_LIMIT) undoStack.value.shift()
    redoStack.value = []
    pages.value = next
    // Pages that are gone leave the selection.
    const keys = new Set(next.map((p) => p.key))
    selected.value = new Set([...selected.value].filter((k) => keys.has(k)))
  }
  function undo() {
    const prev = undoStack.value.pop()
    if (!prev) return
    redoStack.value.push(clone(pages.value))
    pages.value = prev
  }
  function redo() {
    const next = redoStack.value.pop()
    if (!next) return
    undoStack.value.push(clone(pages.value))
    pages.value = next
  }

  // -- Selection
  const selectedPages = computed(() => pages.value.filter((p) => selected.value.has(p.key)))
  function select(key: number, mode: 'only' | 'toggle' | 'range') {
    const set = new Set(mode === 'only' ? [] : selected.value)
    if (mode === 'range' && anchorKey != null) {
      const a = pages.value.findIndex((p) => p.key === anchorKey)
      const b = pages.value.findIndex((p) => p.key === key)
      if (a >= 0 && b >= 0) {
        for (let i = Math.min(a, b); i <= Math.max(a, b); i++) set.add(pages.value[i]!.key)
        selected.value = set
        return
      }
    }
    if (mode === 'toggle' && set.has(key)) set.delete(key)
    else set.add(key)
    anchorKey = key
    selected.value = set
  }
  const selectAll = () => (selected.value = new Set(pages.value.map((p) => p.key)))
  const selectNone = () => (selected.value = new Set())
  const invertSelection = () => (selected.value = new Set(pages.value.filter((p) => !selected.value.has(p.key)).map((p) => p.key)))

  /** Where inserted pages go: after the last selected page, else at the end. */
  function insertionIndex(): number {
    let last = -1
    pages.value.forEach((p, i) => { if (selected.value.has(p.key)) last = i })
    return last >= 0 ? last + 1 : pages.value.length
  }

  // -- Page actions (each one undoable)
  function rotateSelected(delta: 90 | -90) {
    if (!selected.value.size) return
    change(pages.value.map((p) => (selected.value.has(p.key) ? { ...p, rotation: (((p.rotation + delta) % 360) + 360) % 360 } : p)))
  }
  function deleteSelected() {
    if (!selected.value.size || selected.value.size >= pages.value.length) return
    change(pages.value.filter((p) => !selected.value.has(p.key)))
  }
  function duplicateSelected() {
    if (!selected.value.size) return
    const copies: number[] = []
    const next: OrganizerPage[] = []
    for (const p of pages.value) {
      next.push(p)
      if (selected.value.has(p.key)) {
        const copy = { ...p, key: nextKey++ }
        copies.push(copy.key)
        next.push(copy)
      }
    }
    change(next)
    selected.value = new Set(copies)
  }
  /** Move the given pages (in their current order) so they land before index `to` of the current list. */
  function movePages(keys: Set<number>, to: number) {
    const moving = pages.value.filter((p) => keys.has(p.key))
    if (!moving.length) return
    const before = pages.value.slice(0, to).filter((p) => !keys.has(p.key))
    const after = pages.value.slice(to).filter((p) => !keys.has(p.key))
    const next = [...before, ...moving, ...after]
    if (signature(next) === signature(pages.value) && next.every((p, i) => p.key === pages.value[i]!.key)) return
    change(next)
  }
  async function insertBlank() {
    const at = insertionIndex()
    const neighbour = pages.value[at - 1] ?? pages.value[at]
    let blankSize = { width: 612, height: 792 }
    const src = neighbour && neighbour.pageIndex >= 0 ? sources.value.get(neighbour.source) : null
    if (src && neighbour) {
      const page = await src.doc.getPage(neighbour.pageIndex + 1)
      const [x0, y0, x1, y1] = page.view as [number, number, number, number]
      blankSize = page.rotate % 180 ? { width: y1 - y0, height: x1 - x0 } : { width: x1 - x0, height: y1 - y0 }
    } else if (neighbour?.blankSize) blankSize = neighbour.blankSize
    const page: OrganizerPage = { key: nextKey++, source: PRIMARY_SOURCE, pageIndex: -1, rotation: 0, blankSize }
    change([...pages.value.slice(0, at), page, ...pages.value.slice(at)])
    selected.value = new Set([page.key])
    anchorKey = page.key
  }
  /** Adds another PDF as a source and inserts all its pages after the selection (or at the end). */
  function insertDocument(doc: PDFDocumentProxy, name: string) {
    const id = nextSource++
    sources.value = new Map(sources.value).set(id, { id, doc, name })
    const added = Array.from({ length: doc.numPages }, (_, i): OrganizerPage => ({ key: nextKey++, source: id, pageIndex: i, rotation: 0 }))
    const at = insertionIndex()
    change([...pages.value.slice(0, at), ...added, ...pages.value.slice(at)])
    selected.value = new Set(added.map((p) => p.key))
  }

  /** The PDF of the given pages (default: all, in the organizer's order). */
  function assemble(list: OrganizerPage[] = pages.value): Promise<Uint8Array> {
    const doc = document.value
    if (!doc) return Promise.reject(new Error('no document'))
    return assemblePdf(doc, list, sources.value)
  }
  /** After a save: the document as it now is becomes the clean state (until it reloads). */
  function markSaved() {
    savedSignature = signature(pages.value)
  }

  function dispose() {
    for (const [id, s] of sources.value) if (id !== PRIMARY_SOURCE) closeSource(s.doc)
  }

  return {
    sources, pages, selected, selectedPages, dirty,
    canUndo: computed(() => undoStack.value.length > 0),
    canRedo: computed(() => redoStack.value.length > 0),
    undo, redo,
    select, selectAll, selectNone, invertSelection,
    rotateSelected, deleteSelected, duplicateSelected, movePages, insertBlank, insertDocument,
    assemble, markSaved, dispose,
  }
}

export type PdfOrganizer = ReturnType<typeof usePdfOrganizer>
