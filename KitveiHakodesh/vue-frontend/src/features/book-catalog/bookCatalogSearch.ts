/**
 * Book catalog search index and filtering.
 *
 * Owns the inverted index built from book titles/paths/authors and all query
 * matching logic. Tree building and category metadata live in bookCatalogTree.ts.
 *
 * Public API:
 *   buildSearchIndex(books)   — call once after assignFullPaths; async so the
 *                               catalog renders before the index is built
 *   filterBooksByWords(books, words) — main entry point for search
 */

import { normalize } from '@/utils/normalizeText'
import { normalizeBookPath, decomposeHebrewWord, stripHePrefix } from './bookCatalogSearchNormalizer'
import type { DecomposedToken } from './bookCatalogSearchNormalizer'
import { SCORE_EXACT, SCORE_PREFIX, SCORE_NONE } from './bookCatalogSearchMatcher'
import type { BookRow } from './bookCatalogTree'

// ─── Tokenization ─────────────────────────────────────────────────────────────

/** Compute the normalized token list for a book's full indexed string (path + title + authors). */
function _tokenizeBook(book: BookRow): string[] {
  const fullPath = book.parentPath ? `${book.parentPath} / ${book.title}` : book.title
  const authorPart = book.authors ? ` ${normalizeBookPath(normalize(book.authors))}` : ''
  const searchString = normalizeBookPath(normalize(fullPath)) + authorPart
  return searchString.split(/\s+/).filter((w) => w.length > 0)
}

/** Compute the normalized token set for a book's title alone (no parent path, no authors). */
function _tokenizeTitle(book: BookRow): Set<string> {
  return new Set(
    normalizeBookPath(normalize(book.title))
      .split(/\s+/)
      .filter((w) => w.length > 0),
  )
}

// ─── Inverted index ───────────────────────────────────────────────────────────
//
// Two structures, built once at catalog load time:
//
//   sortedTokens / sortedTokenBooks:
//     Sorted array of all unique tokens with a parallel Uint16Array of book indices.
//     Handles exact, prefix, and ה-prefix lookups via binary search.
//     Exact match = token === word (SCORE_EXACT).
//     Prefix match = token.startsWith(word) (SCORE_PREFIX).
//     ה-prefix: also scan for stripHePrefix(word) so הרמבן and רמבן match each other.
//
//   skeleton:
//     Consonantal skeleton → { bookIndices, decomps } for חסר/מלא variant matching.
//     נידה and נדה share skeleton "נדה" and are compatible variants (SCORE_EXACT).
//     שבועות and שביעית share skeleton "שבעת" but have incompatible vowel sets — no match.

type BookIndex = number

interface SkeletonEntry {
  bookIndices: Uint16Array
  decomps: DecomposedToken[]
}

interface InvertedIndex {
  sortedTokens: string[]
  sortedTokenBooks: Uint16Array[]
  skeleton: Map<string, SkeletonEntry>
  books: BookRow[]
  /** Number of indexed tokens per book (full path + authors). Fewer = tighter overall match. */
  tokenCounts: Uint16Array
  /** Title-only token set per book — for checking whether a query word hits the title. */
  titleTokenSets: Set<string>[]
  /** Number of title-only tokens per book — tiebreaker within the same title-match tier. */
  titleTokenCounts: Uint16Array
}

let _index: InvertedIndex | null = null

function _buildInvertedIndex(books: BookRow[]): void {
  const tokenSets = new Map<string, Set<BookIndex>>()
  const skelSets = new Map<string, Map<BookIndex, DecomposedToken>>()
  const tokenCountsArr: number[] = []
  const titleTokenSets: Set<string>[] = []
  const titleTokenCountsArr: number[] = []

  const addToTokenSet = (key: string, bi: BookIndex) => {
    let s = tokenSets.get(key)
    if (!s) { s = new Set(); tokenSets.set(key, s) }
    s.add(bi)
  }

  for (let bi = 0; bi < books.length; bi++) {
    const tokens = _tokenizeBook(books[bi]!)
    tokenCountsArr.push(tokens.length)

    const titleSet = _tokenizeTitle(books[bi]!)
    titleTokenSets.push(titleSet)
    titleTokenCountsArr.push(titleSet.size)

    for (const token of tokens) {
      // Index original token
      addToTokenSet(token, bi)

      // Also index ה-stripped form so הרמבן and רמבן share the same sorted-array entry
      const stripped = stripHePrefix(token)
      if (stripped !== null) addToTokenSet(stripped, bi)

      // Skeleton index for חסר/מלא variant matching
      const decomp = decomposeHebrewWord(token)
      let skelMap = skelSets.get(decomp.skeleton)
      if (!skelMap) { skelMap = new Map(); skelSets.set(decomp.skeleton, skelMap) }
      if (!skelMap.has(bi)) skelMap.set(bi, decomp)
    }
  }

  const sortedTokens = Array.from(tokenSets.keys()).sort()
  const sortedTokenBooks = sortedTokens.map((t) => new Uint16Array(tokenSets.get(t)!))

  const skeleton = new Map<string, SkeletonEntry>()
  for (const [skel, skelMap] of skelSets) {
    const entries = Array.from(skelMap.entries())
    skeleton.set(skel, {
      bookIndices: new Uint16Array(entries.map(([bi]) => bi)),
      decomps: entries.map(([, d]) => d),
    })
  }

  _index = {
    sortedTokens, sortedTokenBooks, skeleton, books,
    tokenCounts: new Uint16Array(tokenCountsArr),
    titleTokenSets,
    titleTokenCounts: new Uint16Array(titleTokenCountsArr),
  }
}

/**
 * Build the search index for all books asynchronously.
 * Yields once before the synchronous build so the catalog renders first.
 * Called once from booksDataStore after assignFullPaths.
 */
export async function buildSearchIndex(books: BookRow[]): Promise<void> {
  await Promise.resolve()
  _buildInvertedIndex(books)
}

// ─── Word lookup ──────────────────────────────────────────────────────────────

/** Look up which book indices match a query word, at what tier. */
function _lookupWord(word: string, decomp: DecomposedToken): Map<BookIndex, number> {
  const result = new Map<BookIndex, number>()
  const { sortedTokens, sortedTokenBooks, skeleton } = _index!

  // Binary search helper — finds first index where sortedTokens[i] >= prefix
  const lowerBound = (prefix: string): number => {
    let lo = 0, hi = sortedTokens.length
    while (lo < hi) {
      const mid = (lo + hi) >>> 1
      if (sortedTokens[mid]! < prefix) lo = mid + 1
      else hi = mid
    }
    return lo
  }

  // Scan sorted array for a given lookup word — exact gets SCORE_EXACT, prefix gets SCORE_PREFIX
  const scanPrefix = (lookup: string) => {
    for (let i = lowerBound(lookup); i < sortedTokens.length && sortedTokens[i]!.startsWith(lookup); i++) {
      const tier = sortedTokens[i] === lookup ? SCORE_EXACT : SCORE_PREFIX
      for (const bi of sortedTokenBooks[i]!) {
        const existing = result.get(bi) ?? SCORE_NONE
        if (tier > existing) result.set(bi, tier)
      }
    }
  }

  // Lookup original word (exact + prefix)
  scanPrefix(word)

  // Also lookup ה-stripped form of the query (רמבן finds הרמבן indexed as רמבן, and vice versa)
  const strippedQuery = stripHePrefix(word)
  if (strippedQuery !== null) scanPrefix(strippedQuery)

  // Skeleton/variant matches — חסר/מלא spelling variants get SCORE_EXACT
  const skelEntry = skeleton.get(decomp.skeleton)
  if (skelEntry) {
    for (let i = 0; i < skelEntry.bookIndices.length; i++) {
      const bi = skelEntry.bookIndices[i]!
      if ((result.get(bi) ?? SCORE_NONE) >= SCORE_EXACT) continue
      const td = skelEntry.decomps[i]!
      const setA = td.vowelSet, setB = decomp.vowelSet
      let compatible: boolean
      if (setA.size <= setB.size) {
        compatible = true
        for (const k of setA) if (!setB.has(k)) { compatible = false; break }
      } else {
        compatible = true
        for (const k of setB) if (!setA.has(k)) { compatible = false; break }
      }
      if (compatible) result.set(bi, SCORE_EXACT)
    }
  }

  return result
}

// ─── Scoring ──────────────────────────────────────────────────────────────────

/**
 * Score all books against the given words and return them sorted best-first.
 *
 * Ranking tiers (applied in order):
 *   1. Total score (sum of per-word match tiers) — higher is better
 *   2. Title match count — how many query words hit the title tokens directly.
 *      More title hits = closer match. A book whose title contains all query words
 *      ranks above a commentary that only matches because its path contains them.
 *   3. Title token count — within the same title-match tier, fewer title tokens
 *      means the query fills a larger fraction of the title (tighter fit).
 *   4. Catalog tree order — final tiebreak by catalog position
 */
function _scoreBooks(words: string[]): BookRow[] {
  const { tokenCounts, titleTokenSets, titleTokenCounts } = _index!
  const wordMaps = words.map((w) => _lookupWord(w, decomposeHebrewWord(w)))

  // Catalog-best tier per word
  const catalogBest = wordMaps.map((m) => {
    let best = SCORE_NONE
    for (const tier of m.values()) if (tier > best) best = tier
    return best
  })

  if (catalogBest.some((b) => b === SCORE_NONE)) return []

  // Intersect word maps, smallest first
  const sorted = wordMaps
    .map((m, i) => ({ m, best: catalogBest[i]! }))
    .sort((a, b) => a.m.size - b.m.size)

  const scored: { book: BookRow; total: number; titleMatchCount: number; titleTokenCount: number; tokenCount: number }[] = []
  for (const [bi, tier0] of sorted[0]!.m) {
    if (tier0 < sorted[0]!.best) continue
    let total = tier0
    let qualified = true
    for (let wi = 1; wi < sorted.length; wi++) {
      const tier = sorted[wi]!.m.get(bi) ?? SCORE_NONE
      if (tier < sorted[wi]!.best) { qualified = false; break }
      total += tier
    }
    if (!qualified) continue

    // Count how many query words appear in the title tokens (exact or prefix match).
    const titleToks = titleTokenSets[bi]!
    let titleMatchCount = 0
    for (const w of words) {
      for (const tok of titleToks) {
        if (tok === w || tok.startsWith(w)) { titleMatchCount++; break }
      }
    }

    scored.push({
      book: _index!.books[bi]!,
      total,
      titleMatchCount,
      titleTokenCount: titleTokenCounts[bi]!,
      tokenCount: tokenCounts[bi]!,
    })
  }

  scored.sort((a, b) => {
    // 1. Higher total score first
    if (b.total !== a.total) return b.total - a.total
    // 2. More query words hitting the title = closer match
    if (b.titleMatchCount !== a.titleMatchCount) return b.titleMatchCount - a.titleMatchCount
    // 3. Fewer title tokens = query fills more of the title
    if (a.titleTokenCount !== b.titleTokenCount) return a.titleTokenCount - b.titleTokenCount
    // 4. Catalog tree order
    return (a.book.treeOrder ?? 0) - (b.book.treeOrder ?? 0)
  })

  // When no word reached EXACT, promote books whose title starts with the full
  // raw query to the front.
  const hasExact = catalogBest.some((b) => b >= SCORE_EXACT)
  if (!hasExact) {
    const rawQuery = words.join(' ')
    scored.sort((a, b) => {
      const aStarts = a.book.title.startsWith(rawQuery) ? 0 : 1
      const bStarts = b.book.title.startsWith(rawQuery) ? 0 : 1
      return aStarts - bStarts
    })
  }

  return scored.map(({ book }) => book)
}

// ─── Public entry point ───────────────────────────────────────────────────────

/**
 * Filter and rank books against a normalized query word list.
 * Uses the inverted index for O(results) lookup instead of O(all books) scan.
 */
export function filterBooksByWords(allBooks: BookRow[], words: string[]): BookRow[] {
  if (!words.length) return []

  // Build index synchronously on first call if async build hasn't completed yet
  if (!_index) _buildInvertedIndex(allBooks)

  return _scoreBooks(words)
}
