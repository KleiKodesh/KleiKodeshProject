import type { TocEntry } from '@/webview-host/queries.types'
import type { SearchableTree } from './tocSearchUtils'

/**
 * Lookup of TOCs that OPEN book-view tabs hold right now. NOT a cache: each
 * mounted useToc instance registers itself and removes itself on scope dispose,
 * so nothing here outlives the tabs that loaded it. A second tab (or a word-link
 * preview) on a book that is already open takes the open tab's arrays instead of
 * refetching; a book with no open tab is always fetched.
 *
 * `raw` is what getAllTocEntries returned (word-link sections use it as is);
 * `pending` is set while the owning tab's fetch is in flight so a simultaneous
 * mount shares it — and only while that tab is alive.
 */
export interface OpenTabToc {
  bookId: number | undefined
  raw: TocEntry[] | null
  pending: Promise<TocEntry[]> | null
  /** The tab's title-stripped entries + search tree, reusable by a tab with the same book title. */
  view: { bookTitle: string | undefined; entries: TocEntry[]; searchTree: SearchableTree } | null
}

const openTabTocs = new Set<OpenTabToc>()

export function registerOpenTabToc(owner: OpenTabToc): () => void {
  openTabTocs.add(owner)
  return () => openTabTocs.delete(owner)
}

/** An open tab's already-built view (stripped entries + search tree) for this book and title. */
export function findOpenTabTocView(bookId: number, bookTitle: string | undefined, except: OpenTabToc): OpenTabToc['view'] {
  for (const owner of openTabTocs) {
    if (owner !== except && owner.bookId === bookId && owner.view?.bookTitle === bookTitle) return owner.view
  }
  return null
}

/** The raw TOC an open tab already holds for `bookId`, or its in-flight fetch; null when no open tab has it. */
export function findOpenTabToc(bookId: number, except?: OpenTabToc): TocEntry[] | Promise<TocEntry[]> | null {
  let pending: Promise<TocEntry[]> | null = null
  for (const owner of openTabTocs) {
    if (owner === except || owner.bookId !== bookId) continue
    if (owner.raw) return owner.raw
    pending ??= owner.pending
  }
  return pending
}
