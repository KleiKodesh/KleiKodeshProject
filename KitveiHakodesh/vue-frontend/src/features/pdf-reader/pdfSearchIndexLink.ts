import type { PDFDocumentProxy } from 'pdfjs-dist'
import { ensureSearchIndex, type PDFFindController } from 'pdfjs-dist/web/pdf_viewer.mjs'

/**
 * The open document's search index (our PDF.js fork, pdf_search_index_store.js): loaded from
 * IndexedDB (one per file, the view history's last 20 files) or started empty. It is never built
 * by reading pages: PDFFindController hands it every page a search extracts anyway (in idle time,
 * off the search's path) and it is stored in idle time, so later searches read only the pages
 * that can match. Results stay exactly PDF.js's (the index only rules pages out).
 */
// A changed file must never use the old file's index (it could rule out pages that now match):
// the stored key is the PDF's fingerprint plus the file's size. An incremental save keeps the
// fingerprint but always grows the file; a page edit writes a new file. The view history prunes
// by fingerprint, so the current key per fingerprint is remembered here for that pruning.
const KEYS_STORAGE = 'pdfReader.searchIndexKeys'
function readKeys(): Record<string, string> {
  try {
    return JSON.parse(localStorage.getItem(KEYS_STORAGE) ?? '{}') as Record<string, string>
  } catch {
    return {}
  }
}
/** The stored index keys to keep for these view-history fingerprints (usePdfViewHistory). */
export function searchIndexKeysFor(fingerprints: string[]): string[] {
  const keys = readKeys()
  return fingerprints.map((fp) => keys[fp] ?? fp)
}

export function linkPdfSearchIndex(doc: PDFDocumentProxy, findController: PDFFindController, isCurrent: () => boolean, fileSize?: number) {
  const fc = findController as unknown as { textIndex: unknown }
  fc.textIndex = null
  const fingerprint = doc.fingerprints[0] as string
  void (async () => {
    const size = fileSize ?? (await doc.getDownloadInfo()).length
    const key = `${fingerprint}@${size}`
    try {
      const keys = readKeys()
      keys[fingerprint] = key
      localStorage.setItem(KEYS_STORAGE, JSON.stringify(keys))
    } catch { /* no storage: the index is only kept for this session */ }
    if (!isCurrent()) return
    await ensureSearchIndex(doc, key, {
      isCurrent,
      onIndex: (index: unknown) => {
        if (isCurrent()) fc.textIndex = index
      },
    })
  })().catch(() => {})
  return () => {
    fc.textIndex = null
  }
}
