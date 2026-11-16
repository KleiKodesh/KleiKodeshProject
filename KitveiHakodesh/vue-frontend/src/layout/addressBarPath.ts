import type { Tab } from '@/stores/tabStore'
import type { BookRow } from '@/webview-host/queries.types'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import { findCategoryChain } from '@/features/book-catalog/bookCatalogTree'

/**
 * The address bar's Explorer-style full path. Catalog books live in a SQLite db,
 * so their "path" is the catalog chain (category titles + book title) joined by
 * '\'; local files show their real disk path.
 */
const SEPARATOR = '\\'
const FILE_ROUTES = new Set<string>(['/pdf-view', '/html-view', '/fast-doc-view'])

export function addressBarPath(tab: Tab | undefined, root: CategoryNode, books: Map<number, BookRow>): string {
  const location = locationPath(tab, root, books)
  // The reader's current TOC section (book view, PDF, FastDocView publish it on the tab).
  const toc = tab?.tocPath?.trim()
  return location && toc ? `${location}${SEPARATOR}${toc}` : location
}

export function locationPath(tab: Tab | undefined, root: CategoryNode, books: Map<number, BookRow>): string {
  if (!tab) return ''
  // Navigating away leaves localFilePath on the tab, so only file routes may show it.
  if (FILE_ROUTES.has(tab.route)) return tab.localFilePath ?? ''
  if (tab.route !== '/book-view' || tab.bookId == null) return ''
  const book = books.get(tab.bookId)
  if (!book) return tab.title
  const chain = findCategoryChain(root, book.categoryId) ?? []
  return [...chain.map((c) => c.title), book.title].join(SEPARATOR)
}

/** A drive path (C:\…) or UNC share (\\server\…). */
export function isDiskPath(text: string): boolean {
  return /^[a-zA-Z]:[\\/]|^\\\\/.test(text)
}

/**
 * Normalises pasted text into a disk path: strips quotes ("Copy as path") and
 * converts file:/// URLs. Returns the text trimmed when it is neither.
 */
export function normalizePastedPath(text: string): string {
  let s = text.trim().replace(/^"(.*)"$/, '$1').trim()
  if (/^file:\/\//i.test(s)) {
    try {
      const u = new URL(s)
      let p = decodeURIComponent(u.pathname).replace(/^\/(?=[a-zA-Z]:)/, '')
      if (u.host) p = `\\\\${u.host}${p}`
      s = p.replace(/\//g, '\\')
    } catch { /* keep as typed */ }
  }
  return s
}

/** Disk path ending in a file name with an extension — anything else is treated as a folder. */
export function looksLikeFile(path: string): boolean {
  const last = path.replace(/[\\/]+$/, '').split(/[\\/]/).pop() ?? ''
  return /\.[^.\s]{1,6}$/.test(last)
}

export type CatalogTarget = { kind: 'book'; book: BookRow } | { kind: 'category'; node: CategoryNode }

/**
 * Walks the catalog by '\' or '/' separated titles. The last segment may be a
 * book in the reached category. Null when any segment doesn't match.
 */
export function resolveCatalogPath(text: string, root: CategoryNode): CatalogTarget | null {
  const segments = text.split(/[\\/]/).map((s) => s.trim()).filter(Boolean)
  if (segments.length === 0) return null
  let node = root
  for (let i = 0; i < segments.length; i++) {
    const segment = segments[i]!
    const child = node.children.find((c) => c.title === segment)
    if (child) { node = child; continue }
    const book = i === segments.length - 1 ? node.books.find((b) => b.title === segment) : undefined
    return book ? { kind: 'book', book } : null
  }
  return { kind: 'category', node }
}

/**
 * A copied address-bar address of a catalog book: category titles, the book title,
 * then the current TOC section, all joined by '\'. Null when the text does not walk
 * the catalog down to a book. `tocPath` is '' when the address stops at the book.
 */
export function parseBookAddress(text: string, root: CategoryNode): { book: BookRow; tocPath: string } | null {
  if (!text.includes(SEPARATOR)) return null
  const segments = text.split(SEPARATOR).map((s) => s.trim())
  let node = root
  for (let i = 0; i < segments.length; i++) {
    const segment = segments[i]!
    const child = node.children.find((c) => c.title === segment)
    if (child) { node = child; continue }
    const book = node.books.find((b) => b.title === segment)
    if (!book) return null
    return { book, tocPath: segments.slice(i + 1).filter(Boolean).join(SEPARATOR) }
  }
  return null
}

/**
 * A copied address of a local file: the disk path, optionally followed by '\' and
 * the TOC section. Returns just the file path (cut after the first segment with an
 * extension), or null when the text is not a disk path to a file.
 */
export function fileFromAddress(text: string): string | null {
  if (!isDiskPath(text)) return null
  const segments = text.split(SEPARATOR)
  const fileIndex = segments.findIndex((s, i) => i > 0 && /\.[^.\s]{1,6}$/.test(s.trim()))
  return fileIndex < 0 ? null : segments.slice(0, fileIndex + 1).join(SEPARATOR)
}
