import { computed, shallowRef, watch, type Ref } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import type { TreeNodeItem } from '@/components/treeTypes'

/** One TOC row for a PDF outline item; `pageIndex` is filled once its destination is resolved. */
export interface PdfOutlineNode extends TreeNodeItem {
  dest: string | unknown[] | null
  url: string | null
  pageIndex?: number
}

/**
 * One outline item in the editable model. Items read from the PDF carry `src` (their index in
 * the original outline flattened breadth-first - what saveDocument({ outline }) uses to keep the
 * item's /Dest or /A, style, colour and closed state). Added or retargeted items have no `src`,
 * no dest/url, and point at `pageIndex`.
 */
export interface PdfOutlineEntry {
  id: number
  title: string
  dest: string | unknown[] | null
  url: string | null
  pageIndex?: number
  src?: number
  children: PdfOutlineEntry[]
}

type RawOutlineItem = Awaited<ReturnType<PDFDocumentProxy['getOutline']>>[number]

/**
 * The PDF's outline (bookmarks): an editable tree model (`roots`) and its flat TreeView rows in
 * document order (`nodes`), plus the current entry: the deepest row whose destination page is
 * at or before the current page. Pages are resolved lazily, once per document, after the
 * outline is shown. Edits (usePdfOutlineEdit) change `roots` and call `commit()`.
 */
export function usePdfOutline(pdfDocument: Ref<PDFDocumentProxy | null>, pageNumber: Ref<number>) {
  const nodes = shallowRef<PdfOutlineNode[]>([])
  /** The editable tree; plain objects - `commit()` redraws the rows from it. */
  let roots: PdfOutlineEntry[] = []
  let nextId = 1
  /** Row objects by id, reused while a row is unchanged so an edit redraws only what changed. */
  let rowCache = new Map<number, PdfOutlineNode>()
  /** Bumped when page indexes land, so the current entry recomputes. */
  const resolvedVersion = shallowRef(0)

  function build(items: readonly RawOutlineItem[]): PdfOutlineEntry[] {
    return items.map((item) => ({
      id: nextId++,
      title: item.title,
      dest: (item.dest as string | unknown[] | null) ?? null,
      url: item.url ?? null,
      children: build(item.items),
    }))
  }

  /** Stamps `src` in the same breadth-first order the save (the PDF.js worker) flattens in. */
  function stampSources(list: PdfOutlineEntry[]) {
    let src = 0
    let level = list
    while (level.length) {
      const next: PdfOutlineEntry[] = []
      for (const entry of level) {
        entry.src = src++
        next.push(...entry.children)
      }
      level = next
    }
  }

  function* walk(list: PdfOutlineEntry[]): Generator<PdfOutlineEntry> {
    for (const entry of list) {
      yield entry
      yield* walk(entry.children)
    }
  }

  /** Redraws the rows from `roots` (after load, page resolution or an edit). */
  function commit() {
    const out: PdfOutlineNode[] = []
    const cache = new Map<number, PdfOutlineNode>()
    const visit = (list: PdfOutlineEntry[], parentId: number | null, level: number) => {
      for (const entry of list) {
        const trailing = entry.pageIndex == null ? undefined : String(entry.pageIndex + 1)
        const prev = rowCache.get(entry.id)
        const same = prev && prev.parentId === parentId && prev.level === level && prev.text === entry.title
          && prev.hasChildren === entry.children.length > 0 && prev.trailing === trailing
          && prev.dest === entry.dest && prev.url === entry.url && prev.pageIndex === entry.pageIndex
        const row: PdfOutlineNode = same ? prev : {
          id: entry.id, parentId, level, hasChildren: entry.children.length > 0, text: entry.title,
          dest: entry.dest, url: entry.url, pageIndex: entry.pageIndex, trailing,
        }
        cache.set(entry.id, row)
        out.push(row)
        visit(entry.children, entry.id, level + 1)
      }
    }
    visit(roots, null, 0)
    rowCache = cache
    nodes.value = out
    resolvedVersion.value++
  }

  async function resolvePages(doc: PDFDocumentProxy) {
    for (const entry of [...walk(roots)]) {
      if (pdfDocument.value !== doc) return
      if (entry.dest == null || entry.pageIndex != null) continue
      try {
        const explicit = typeof entry.dest === 'string' ? await doc.getDestination(entry.dest) : entry.dest
        const ref = explicit?.[0]
        if (ref == null) continue
        entry.pageIndex = typeof ref === 'number' ? ref : await doc.getPageIndex(ref as Parameters<PDFDocumentProxy['getPageIndex']>[0])
      } catch {
        // unresolvable destination: the row stays without a page
      }
    }
    if (pdfDocument.value !== doc) return
    commit()
  }

  watch(pdfDocument, async (doc) => {
    roots = []
    rowCache = new Map()
    nodes.value = []
    if (!doc) return
    const outline = (await doc.getOutline().catch(() => null)) ?? []
    if (pdfDocument.value !== doc) return
    roots = build(outline)
    stampSources(roots)
    commit()
    void resolvePages(doc)
  }, { immediate: true })

  const activeNodeId = computed(() => {
    void resolvedVersion.value
    const pageIndex = pageNumber.value - 1
    let best: PdfOutlineNode | undefined
    for (const node of nodes.value) {
      if (node.pageIndex == null || node.pageIndex > pageIndex) continue
      // document order: a later row at or before the page wins (deeper rows follow their parents)
      if (!best || node.pageIndex >= best.pageIndex!) best = node
    }
    return best?.id
  })

  return {
    nodes,
    activeNodeId,
    /** The editable tree (read it fresh each time: a new document replaces it). */
    roots: () => roots,
    newId: () => nextId++,
    commit,
  }
}

export type PdfOutline = ReturnType<typeof usePdfOutline>
