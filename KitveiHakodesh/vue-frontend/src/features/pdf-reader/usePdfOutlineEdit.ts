import { computed, shallowRef, watch, type Ref } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import type { PdfOutline, PdfOutlineEntry } from './usePdfOutline'

/** One entry of saveDocument({ outline }) in our PDF.js fork (KleiKodesh/pdf.js 954500124). */
export interface EditedOutlineItem {
  title: string
  /** 1-based page number (used by entries without `src`). */
  page: number
  /** Index of the original item in the outline flattened breadth-first: keeps its /Dest or /A, style, colour, closed state. */
  src?: number
  items?: EditedOutlineItem[]
}

export type OutlineDropPosition = 'before' | 'after' | 'into'

/**
 * Editing the PDF outline (the old viewer's outline-search.js editing, on the Vue TOC):
 * add / rename / delete (children promoted) / retarget to the current page / move among
 * siblings / indent / outdent / drag to before, after or into a row. The model is
 * usePdfOutline's tree; `dirty` is true after any real change, until a new document loads.
 * There is deliberately no visual dirty cue in the panel (as the old viewer).
 */
export function usePdfOutlineEdit(outline: PdfOutline, pdfDocument: Ref<PDFDocumentProxy | null>, pageNumber: Ref<number>) {
  const dirty = shallowRef(false)
  watch(pdfDocument, () => (dirty.value = false))

  /** XFA documents: the save skips their outline, so edits would be silently lost. */
  const canEdit = computed(() => !!pdfDocument.value && !pdfDocument.value.isPureXfa)

  interface Location { list: PdfOutlineEntry[]; index: number; parent: PdfOutlineEntry | null }

  function locate(id: number, list = outline.roots(), parent: PdfOutlineEntry | null = null): Location | null {
    for (let index = 0; index < list.length; index++) {
      const entry = list[index]!
      if (entry.id === id) return { list, index, parent }
      const inner = locate(id, entry.children, entry)
      if (inner) return inner
    }
    return null
  }

  function contains(entry: PdfOutlineEntry, id: number): boolean {
    return entry.id === id || entry.children.some((c) => contains(c, id))
  }

  function changed() {
    dirty.value = true
    outline.commit()
  }

  function newEntry(title: string): PdfOutlineEntry {
    return { id: outline.newId(), title, dest: null, url: null, pageIndex: pageNumber.value - 1, children: [] }
  }

  /** Renames; only an actual text change counts. Empty titles are refused. */
  function rename(id: number, title: string): boolean {
    const loc = locate(id)
    const text = title.trim()
    if (!loc || !text || loc.list[loc.index]!.title === text) return false
    loc.list[loc.index]!.title = text
    changed()
    return true
  }

  /** A new entry at the current page, after `afterId` (same level), or at the end. Returns its id. */
  function addAfter(afterId: number | null | undefined, title: string): number | null {
    if (!canEdit.value) return null
    const entry = newEntry(title)
    const loc = afterId == null ? null : locate(afterId)
    if (loc) loc.list.splice(loc.index + 1, 0, entry)
    else outline.roots().push(entry)
    changed()
    return entry.id
  }

  /** A new entry at the current page, as the last child of `parentId`. Returns its id. */
  function addChild(parentId: number, title: string): number | null {
    const loc = locate(parentId)
    if (!canEdit.value || !loc) return null
    const entry = newEntry(title)
    loc.list[loc.index]!.children.push(entry)
    changed()
    return entry.id
  }

  /** Deletes the entry; its children take its place (promoted, not dropped). */
  function remove(id: number) {
    const loc = locate(id)
    if (!canEdit.value || !loc) return
    const entry = loc.list[loc.index]!
    loc.list.splice(loc.index, 1, ...entry.children)
    changed()
  }

  /** Points the entry at the current page; it becomes editor-owned (its original target and style link dropped). */
  function retarget(id: number) {
    const loc = locate(id)
    if (!canEdit.value || !loc) return
    const entry = loc.list[loc.index]!
    entry.pageIndex = pageNumber.value - 1
    entry.dest = null
    entry.url = null
    entry.src = undefined
    changed()
  }

  /** Moves among its siblings (-1 up, +1 down). */
  function moveSibling(id: number, delta: -1 | 1): boolean {
    const loc = locate(id)
    if (!canEdit.value || !loc) return false
    const to = loc.index + delta
    if (to < 0 || to >= loc.list.length) return false
    const [entry] = loc.list.splice(loc.index, 1)
    loc.list.splice(to, 0, entry!)
    changed()
    return true
  }

  /** Nests it as the last child of its previous sibling. Returns the new parent's id. */
  function indent(id: number): number | null {
    const loc = locate(id)
    if (!canEdit.value || !loc || loc.index === 0) return null
    const parent = loc.list[loc.index - 1]!
    const [entry] = loc.list.splice(loc.index, 1)
    parent.children.push(entry!)
    changed()
    return parent.id
  }

  /** Moves it out of its parent, right after the parent. */
  function outdent(id: number): boolean {
    const loc = locate(id)
    if (!canEdit.value || !loc || !loc.parent) return false
    const parentLoc = locate(loc.parent.id)!
    const [entry] = loc.list.splice(loc.index, 1)
    parentLoc.list.splice(parentLoc.index + 1, 0, entry!)
    changed()
    return true
  }

  /** Whether `id` may be dropped at `targetId` (never into or beside its own subtree). */
  function canDrop(id: number, targetId: number): boolean {
    const loc = locate(id)
    return !!loc && canEdit.value && id !== targetId && !contains(loc.list[loc.index]!, targetId)
  }

  /** Drag & drop: before / after the target row, or into it (as its last child). */
  function moveTo(id: number, targetId: number, position: OutlineDropPosition): boolean {
    if (!canDrop(id, targetId)) return false
    const loc = locate(id)!
    const [entry] = loc.list.splice(loc.index, 1)
    const target = locate(targetId)!
    if (position === 'into') target.list[target.index]!.children.push(entry!)
    else target.list.splice(target.index + (position === 'after' ? 1 : 0), 0, entry!)
    changed()
    return true
  }

  /**
   * The edited outline in saveDocument({ outline }) form, or null when nothing was edited
   * (then the save must leave the outline as it is). Entries whose destination never
   * resolved take the nearest previous entry's page, as the old viewer did.
   *
   * usePdfSave passes it to saveDocument({ outline }) when the engine supports that (our
   * PDF.js fork build, KleiKodesh/pdf.js 954500124); stock pdfjs-dist cannot, and the save
   * tells the reader the outline changes were not kept.
   */
  function buildSaveOutline(): EditedOutlineItem[] | null {
    if (!dirty.value) return null
    let lastPage = 1
    const convert = (list: PdfOutlineEntry[]): EditedOutlineItem[] =>
      list.map((entry) => {
        if (entry.pageIndex != null) lastPage = entry.pageIndex + 1
        const item: EditedOutlineItem = { title: entry.title, page: lastPage }
        if (entry.src != null) item.src = entry.src
        if (entry.children.length) item.items = convert(entry.children)
        return item
      })
    return convert(outline.roots())
  }

  return {
    dirty, canEdit,
    rename, addAfter, addChild, remove, retarget, moveSibling, indent, outdent, canDrop, moveTo,
    buildSaveOutline,
  }
}

export type PdfOutlineEdit = ReturnType<typeof usePdfOutlineEdit>
