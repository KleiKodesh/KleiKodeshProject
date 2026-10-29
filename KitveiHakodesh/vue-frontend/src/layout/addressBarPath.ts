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
const FILE_ROUTES = new Set<string>(['/pdf-view', '/html-view', '/txt-view', '/fast-doc-view'])

export function addressBarPath(tab: Tab | undefined, root: CategoryNode, books: Map<number, BookRow>): string {
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
