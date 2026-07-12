/**
 * TOC heuristics for the book-catalog search fallback.
 *
 * When a user query matches no book titles directly, these heuristics try to
 * interpret the query as "<book words> <toc words>" — e.g. "בראשית פרק ד" splits
 * into book="בראשית" and toc="פרק ד".
 *
 * Each stage is a named function so the pipeline is easy to follow and test:
 *
 *   Stage 1 — splitQueryIntoBookAndTocParts
 *             Finds the longest book-matching prefix of the query words.
 *
 *   Stage 2 — fetchTocRowsForBooks
 *             Loads all TOC entries for the candidate books from the DB in
 *             parallel batches, stripping redundant root entries.
 *
 *   Stage 3 — searchTocRows
 *             Runs the SearchableTree scorer against the TOC words portion.
 *
 *   Stage 4 — buildTocResultItems
 *             Converts matched TOC nodes into SearchFsItem objects for the UI.
 */

import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import { SearchableTree, stripTocTitleRoots } from '../book-view/toc/tocSearchUtils'
import type { BookRow } from './bookCatalogTree'
import type { TocFsItem } from './useBookCatalogSearch'

// ─── Cap ──────────────────────────────────────────────────────────────────────

/**
 * Maximum number of candidate books to fetch TOC rows for.
 *
 * A broad book-prefix like "ראש" can match hundreds of books. Without a cap,
 * Stage 2 would issue many DB round-trips and the user would be stuck on the
 * loading spinner for several seconds. The cap applies only to the TOC
 * heuristics path — the plain book-title search (Phase 1) is uncapped.
 *
 * Books are sorted by tree order before the cap is applied, so the most
 * prominent catalog entries are always searched first.
 */
const MAX_TOC_CANDIDATE_BOOKS = 50

// ─── Types ────────────────────────────────────────────────────────────────────

export type TocRow = {
  id: number
  parentId: number | null
  bookId: number
  text: string
  lineIndex: number | null
  hasChildren: number | boolean
}

export type TocHeuristicsResult = {
  /** TOC items ready for the UI, sorted by book tree order */
  items: TocFsItem[]
  /** Whether the heuristics found a valid book+toc split at all */
  splitFound: boolean
}

// ─── Stage 1: Split query into book words and TOC words ───────────────────────

/**
 * Stage 1 — splitQueryIntoBookAndTocParts
 *
 * Tries every right-trimmed prefix of the query words to find the longest
 * prefix that matches at least one book. The remaining words become the TOC
 * portion.
 *
 * Example: ["בראשית", "פרק", "ד"]
 *   → tries ["בראשית", "פרק"] — no book match
 *   → tries ["בראשית"]        — book match ✓
 *   → returns { bookWords: ["בראשית"], tocWords: ["פרק", "ד"] }
 *
 * Returns null when no prefix matches any book (nothing to search in TOC).
 */
export function splitQueryIntoBookAndTocParts(
  words: string[],
  matchesAnyBook: (words: string[]) => boolean,
): { bookWords: string[]; tocWords: string[] } | null {
  for (let trimCount = 1; trimCount < words.length; trimCount++) {
    const bookWords = words.slice(0, words.length - trimCount)
    if (matchesAnyBook(bookWords)) {
      return { bookWords, tocWords: words.slice(words.length - trimCount) }
    }
  }
  return null
}

// ─── Stage 2: Fetch TOC rows for candidate books ──────────────────────────────

/**
 * Splits an array of IDs into roughly sqrt(n)-sized batches.
 * Keeps individual SQL IN-clauses from growing too large while minimising
 * round-trips for small sets.
 */
function splitIntoBatches(ids: number[]): number[][] {
  const batchSize = Math.max(1, Math.ceil(Math.sqrt(ids.length)))
  const batches: number[][] = []
  for (let offset = 0; offset < ids.length; offset += batchSize) {
    batches.push(ids.slice(offset, offset + batchSize))
  }
  return batches
}

/**
 * Stage 2 — fetchTocRowsForBooks
 *
 * Loads all TOC entries for the given books, strips redundant root entries
 * (entries whose text duplicates the book title), and returns the rows ordered
 * by book tree order (te.id order within each book) — the same order the
 * old sequential per-batch implementation produced.
 *
 * The batches are independent, so they are issued together and awaited with
 * Promise.all — the DB round-trips overlap instead of paying each one's
 * latency in sequence. Nothing is retained after the search completes.
 *
 * The `isCancelled` callback is checked after the fetches complete — if it
 * returns true the function returns null (a newer search has superseded this
 * one).
 *
 * Returns the full flat list of TocRow objects, or null if cancelled.
 */
export async function fetchTocRowsForBooks(
  candidateBooks: BookRow[],
  isCancelled: () => boolean,
): Promise<TocRow[] | null> {
  const bookTitles = new Map(candidateBooks.map((book) => [book.id, book.title]))
  const bookIds = candidateBooks
    .slice()
    .sort((a, b) => (a.treeOrder ?? 0) - (b.treeOrder ?? 0))
    .map((book) => book.id)

  const batchResults = await Promise.all(
    splitIntoBatches(bookIds).map((batch) =>
      query<TocRow>(SQL.GET_TOC_TITLES_FOR_BOOKS(batch.length), batch),
    ),
  )
  if (isCancelled()) return null

  // Group rows per book, then strip redundant roots per book.
  const rowsByBook = new Map<number, TocRow[]>()
  for (const rows of batchResults) {
    for (const row of rows) {
      const group = rowsByBook.get(row.bookId)
      if (group) group.push(row)
      else rowsByBook.set(row.bookId, [row])
    }
  }

  // Assemble in book tree order — identical ordering to the old sequential
  // implementation (batch order followed tree order, stable-sorted per batch).
  const allRows: TocRow[] = []
  for (const id of bookIds) {
    const group = rowsByBook.get(id)
    if (!group) continue
    const stripped = stripTocTitleRoots(group, bookTitles.get(id) ?? '', { bookId: id })
    for (const row of stripped) allRows.push(row)
  }

  return allRows
}

// ─── Stage 3: Search TOC rows with the TOC portion of the query ───────────────

/**
 * Stage 3 — searchTocRows
 *
 * Builds a SearchableTree from the flat TOC rows and runs the scorer against
 * the TOC words. Returns matched nodes sorted by score (tightest match first).
 *
 * The tree is built fresh per search — it is not cached because the candidate
 * book set changes with every query.
 */
export function searchTocRows(
  allRows: TocRow[],
  tocWords: string[],
): { matchedNodes: ReturnType<SearchableTree['search']>; tree: SearchableTree } {
  const tree = new SearchableTree(allRows)
  const tocQuery = tocWords.join(' ')
  const matchedNodes = tree.search(allRows, tocQuery)
  return { matchedNodes, tree }
}

// ─── Stage 4: Build UI result items from matched TOC nodes ────────────────────

/**
 * Stage 4 — buildTocResultItems
 *
 * Converts matched TOC nodes into TocFsItem objects that the search results
 * component can render. Nodes whose bookId is not in the candidate book map
 * are silently dropped (should not happen in practice).
 */
export function buildTocResultItems(
  matchedNodes: ReturnType<SearchableTree['search']>,
  bookMap: Map<number, BookRow>,
  tree: SearchableTree,
): TocFsItem[] {
  const items: TocFsItem[] = []

  for (const node of matchedNodes) {
    const tocRow = node as TocRow
    const book = bookMap.get(tocRow.bookId)
    if (!book) continue

    items.push({
      uid: `toc-${tocRow.bookId}-${node.id}`,
      kind: 'toc',
      book,
      tocEntryId: node.id,
      tocLineIndex: tocRow.lineIndex,
      tocTitle: node.text,
      tocPath: tree.displayPaths.get(node.id) ?? node.text,
    })
  }

  return items
}

// ─── Full pipeline ─────────────────────────────────────────────────────────────

/**
 * runTocHeuristics — runs all four stages in sequence.
 *
 * Call this when a book-title search yields no results. Pass the full query
 * words, a function that tests whether a word list matches any book, and the
 * full list of all books (used to resolve candidate books after the split).
 *
 * The `isCancelled` callback is polled between async stages — return true from
 * it to abort early when a newer search has started.
 *
 * Returns { items, splitFound }:
 *   - splitFound: false means the query could not be split at all (no results possible)
 *   - items: the TOC result items to show, may be empty even when splitFound is true
 */
export async function runTocHeuristics(
  queryWords: string[],
  filterBooks: (words: string[]) => BookRow[],
  isCancelled: () => boolean,
): Promise<TocHeuristicsResult> {
  // Stage 1: find the book/toc word boundary
  const split = splitQueryIntoBookAndTocParts(queryWords, (words) => filterBooks(words).length > 0)
  if (!split) return { items: [], splitFound: false }

  const { bookWords, tocWords } = split
  if (!tocWords.length) return { items: [], splitFound: true }

  // Cap candidate books so a broad prefix doesn't trigger hundreds of DB fetches.
  // filterBooks already returns books sorted by tree order, so slicing keeps the
  // most prominent catalog entries and drops the tail.
  const candidateBooks = filterBooks(bookWords).slice(0, MAX_TOC_CANDIDATE_BOOKS)
  if (!candidateBooks.length) return { items: [], splitFound: true }

  const bookMap = new Map(candidateBooks.map((book) => [book.id, book]))

  // Stage 2: fetch TOC rows from the database
  const allRows = await fetchTocRowsForBooks(candidateBooks, isCancelled)
  if (allRows === null) return { items: [], splitFound: true } // cancelled

  // Stage 3: score and rank TOC entries
  const { matchedNodes, tree } = searchTocRows(allRows, tocWords)
  if (isCancelled()) return { items: [], splitFound: true }

  // Stage 4: convert to UI items
  const items = buildTocResultItems(matchedNodes, bookMap, tree)

  return { items, splitFound: true }
}
