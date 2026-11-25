import { shallowRef, watch, type Ref } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import type { PdfScroller } from './usePdfScroller'

/** One comment in the side panel: an annotation with text, from the file or made in this session. */
export interface PdfComment {
  key: string
  pageIndex: number
  /** PDF user-space rect [x1, y1, x2, y2] - where a click scrolls to. */
  rect: number[]
  /** PDF.js annotation subtype (Text, FreeText, Highlight, ...) or the editor's type. */
  kind: string
  text: string
  author?: string
  date?: Date
}

/** The annotation subtypes that never hold a reader's comment of their own. */
const SKIPPED = new Set(['Link', 'Popup', 'Widget'])

interface RawAnnotation {
  id: string
  subtype: string
  rect: number[]
  contentsObj?: { str?: string }
  richText?: { str?: string }
  titleObj?: { str?: string }
  modificationDate?: string | null
  creationDate?: string | null
  inReplyTo?: string
}

/** PDF date "D:YYYYMMDDHHmmSS..." to a Date, or undefined. */
function parsePdfDate(s: string | null | undefined): Date | undefined {
  const m = s && /^(?:D:)?(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?(\d{2})?/.exec(s)
  if (!m) return undefined
  const d = new Date(Number(m[1]), Number(m[2] ?? 1) - 1, Number(m[3] ?? 1), Number(m[4] ?? 0), Number(m[5] ?? 0), Number(m[6] ?? 0))
  return Number.isNaN(d.getTime()) ? undefined : d
}

/** The editor objects PDF.js keeps in the document's annotationStorage (duck-typed: no public type). */
interface EditorLike {
  id: string
  pageIndex: number
  annotationElementId?: string | null
  deleted?: boolean
  editorType?: string
  comment?: { text?: string; deleted?: boolean; date?: Date | string } | null
  isEmpty?: () => boolean
  serialize?: (isForCopying?: boolean) => { rect?: number[]; value?: string; contents?: string } | null
}
function isEditor(v: unknown): v is EditorLike {
  return !!v && typeof v === 'object' && typeof (v as EditorLike).pageIndex === 'number' && typeof (v as EditorLike).serialize === 'function'
}

/**
 * Every comment in the document, read lazily (only while `active`), page by page through
 * page.getAnnotations(), plus what the editors added, changed or deleted in this session
 * (read from annotationStorage). Re-read on editing changes.
 */
export function usePdfComments(pdf: PdfScroller, active: Ref<boolean>) {
  const comments = shallowRef<PdfComment[]>([])
  const loading = shallowRef(false)
  /** The file's own annotations, by page - read once per document. */
  let fileRead: Promise<PdfComment[][]> | null = null
  let fileDoc: PDFDocumentProxy | null = null
  let generation = 0

  async function readFile(doc: PDFDocumentProxy): Promise<PdfComment[][]> {
    const pages: PdfComment[][] = []
    for (let i = 0; i < doc.numPages; i++) {
      const page = await doc.getPage(i + 1)
      const annots = (await page.getAnnotations({ intent: 'display' })) as RawAnnotation[]
      if (fileDoc !== doc) return pages
      pages.push(annots
        .filter((a) => !SKIPPED.has(a.subtype) && (a.contentsObj?.str || a.richText?.str)?.trim())
        .map((a) => ({
          key: a.id,
          pageIndex: i,
          rect: a.rect,
          kind: a.subtype,
          text: (a.contentsObj?.str || a.richText?.str || '').trim(),
          author: a.titleObj?.str || undefined,
          date: parsePdfDate(a.modificationDate ?? a.creationDate),
        })))
      // Show what is read so far on a long document.
      if (i % 20 === 19) comments.value = merge(pages, doc)
    }
    return pages
  }

  /** The file's comments with this session's editors laid over them (edited, deleted, new). */
  function merge(pages: PdfComment[][], doc: PDFDocumentProxy): PdfComment[] {
    const overrides = new Map<string, PdfComment | null>()
    const added: PdfComment[] = []
    for (const [, value] of doc.annotationStorage as unknown as Iterable<[string, unknown]>) {
      if (!isEditor(value)) continue
      let item: PdfComment | null = null
      if (!value.deleted && !value.isEmpty?.()) {
        const s = value.serialize?.(false) ?? null
        const text = (value.comment && !value.comment.deleted ? value.comment.text : '') || s?.value || s?.contents || ''
        if (text.trim() && s?.rect) {
          const date = value.comment?.date
          item = { key: value.id, pageIndex: value.pageIndex, rect: s.rect, kind: value.editorType ?? '', text: text.trim(), date: date ? new Date(date) : undefined }
        }
      }
      if (value.annotationElementId) overrides.set(value.annotationElementId, item)
      else if (item) added.push(item)
    }
    const out: PdfComment[] = []
    for (const page of pages) {
      for (const c of page) {
        if (!overrides.has(c.key)) out.push(c)
        else {
          const o = overrides.get(c.key)
          if (o) out.push({ ...o, author: c.author, key: c.key })
        }
      }
    }
    out.push(...added)
    return out.sort((a, b) => a.pageIndex - b.pageIndex || (b.rect[3] ?? 0) - (a.rect[3] ?? 0))
  }

  async function refresh() {
    const doc = pdf.pdfDocument.value
    if (!doc || !active.value) return
    const gen = ++generation
    if (fileDoc !== doc || !fileRead) {
      fileDoc = doc
      fileRead = readFile(doc).catch(() => [])
    }
    loading.value = true
    const pages = await fileRead
    if (gen !== generation) return
    loading.value = false
    comments.value = merge(pages, doc)
  }

  // Coalesce bursts of editing events into one re-read per frame.
  let queued = false
  function queueRefresh() {
    if (queued || !active.value) return
    queued = true
    queueMicrotask(() => {
      queued = false
      void refresh()
    })
  }
  pdf.eventBus.on('editingstateschanged', queueRefresh)
  pdf.eventBus.on('annotationeditormodechanged', queueRefresh)
  pdf.eventBus.on('switchannotationeditorparams', queueRefresh)
  // A comment added, edited or deleted (notes, comments on editors): pdfEditManagers.
  pdf.eventBus.on('kkcommentschanged', queueRefresh)

  watch([active, pdf.pdfDocument], () => void refresh(), { immediate: true })

  return { comments, loading, refresh }
}
