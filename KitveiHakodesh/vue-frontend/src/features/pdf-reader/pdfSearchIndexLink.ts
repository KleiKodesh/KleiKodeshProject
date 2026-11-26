import type { PDFDocumentProxy } from 'pdfjs-dist'
import { ensureSearchIndex, type PDFFindController } from 'pdfjs-dist/web/pdf_viewer.mjs'

/**
 * The open document's search index (our PDF.js fork, pdf_search_index_store.js): loaded from
 * IndexedDB (one per file, the view history's last 20 files) or started empty. It is never built
 * by reading pages: PDFFindController hands it every page a search extracts anyway (in idle time,
 * off the search's path) and it is stored in idle time, so later searches read only the pages
 * that can match. Results stay exactly PDF.js's (the index only rules pages out).
 */
export function linkPdfSearchIndex(doc: PDFDocumentProxy, findController: PDFFindController, isCurrent: () => boolean) {
  const fc = findController as unknown as { textIndex: unknown }
  fc.textIndex = null
  void ensureSearchIndex(doc, doc.fingerprints[0] as string, {
    isCurrent,
    onIndex: (index: unknown) => {
      if (isCurrent()) fc.textIndex = index
    },
  }).catch(() => {})
  return () => {
    fc.textIndex = null
  }
}
