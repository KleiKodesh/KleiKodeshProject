/**
 * A full-text hit's REFERENCE — its book title and TOC path — resolved on demand, by id.
 *
 * Hits carry only numbers (see FullTextSearchResult): the title comes from the catalog the
 * books store already holds, keyed by bookId; the TOC path comes from the hit's tocEntryId
 * walked up the book's TOC, which is fetched once per book (getAllTocEntries) for the books
 * the windowed scroller actually renders and kept in a small LRU — the same shape as the on-demand
 * snippets in fullTextSearchSnippets.ts.
 *
 * The path is built exactly as the service's GetTocPathsForLines SQL used to: ancestors
 * root → leaf joined by single spaces, the root entry dropped when its text is the book's
 * own title.
 */
import { shallowRef } from 'vue'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { getAllTocEntries } from '@/webview-host/seforimApi'
import type { TocEntry } from '@/webview-host/queries.types'
import type { FullTextSearchResult } from './fullTextSearchTypes'

/** The hit's book title from the catalog; '' for a book the catalog does not list. */
export function bookTitleOf(hit: FullTextSearchResult): string {
  return useBooksDataStore().allBooksMap.get(hit.bookId)?.title ?? ''
}

// ── On-demand TOC cache ──────────────────────────────────────────────────────────────

/** Books whose TOC is held. A screenful of rows spans a handful of books. */
const TOC_BOOK_CAP = 60
/** Built paths by TOC entry id. */
const TOC_PATH_CAP = 3000

const tocByBook = new Map<number, Map<number, TocEntry>>()
const tocPaths = new Map<number, string>()
const inFlight = new Set<number>()

/** Bumped whenever a fetched TOC lands — the reactive dependency of tocPathOf. */
const referencesVersion = shallowRef(0)

function touch<K, V>(map: Map<K, V>, key: K, value: V, cap: number) {
  map.delete(key)
  map.set(key, value)
  if (map.size > cap) map.delete(map.keys().next().value as K)
}

function buildPath(entries: Map<number, TocEntry>, tocEntryId: number, bookTitle: string): string {
  const texts: string[] = []
  let entry = entries.get(tocEntryId)
  // Guard against a cyclic parent chain in a damaged TOC: never walk more than the map.
  for (let hops = 0; entry && hops <= entries.size; hops++) {
    texts.push(entry.text)
    entry = entry.parentId == null ? undefined : entries.get(entry.parentId)
  }
  texts.reverse()
  if (texts.length > 1 && texts[0] === bookTitle) texts.shift()
  else if (texts.length === 1 && texts[0] === bookTitle) return ''
  return texts.join(' ')
}

/**
 * The hit's TOC path, '' when it has none, or undefined while its book's TOC is not loaded
 * yet. Reactive: re-evaluates when a requested TOC arrives.
 */
export function tocPathOf(hit: FullTextSearchResult): string | undefined {
  void referencesVersion.value
  if (!hit.tocEntryId) return ''
  const cached = tocPaths.get(hit.tocEntryId)
  if (cached !== undefined) return cached
  const entries = tocByBook.get(hit.bookId)
  if (!entries) return undefined
  touch(tocByBook, hit.bookId, entries, TOC_BOOK_CAP)
  const path = buildPath(entries, hit.tocEntryId, bookTitleOf(hit))
  touch(tocPaths, hit.tocEntryId, path, TOC_PATH_CAP)
  return path
}

/**
 * Fetches the TOCs the given hits' references need and are missing — one request per
 * book, deduplicated against requests still in flight. The caller passes the rows the
 * windowed scroller is about to mount, on every range change.
 */
export function requestReferences(hits: readonly FullTextSearchResult[]): void {
  for (const hit of hits) {
    if (!hit.tocEntryId || tocPaths.has(hit.tocEntryId)) continue
    const bookId = hit.bookId
    if (tocByBook.has(bookId) || inFlight.has(bookId)) continue
    inFlight.add(bookId)
    getAllTocEntries(bookId)
      .then((rows) => {
        const entries = new Map<number, TocEntry>()
        for (const row of rows) entries.set(row.id, row)
        touch(tocByBook, bookId, entries, TOC_BOOK_CAP)
      })
      .catch((err) => console.error('[fullTextSearchReferences] TOC fetch failed:', err))
      .finally(() => {
        inFlight.delete(bookId)
        referencesVersion.value++
      })
  }
}
