export interface FullTextSearchResult {
  lineId: number
  bookId: number
  bookTitle: string
  tocText: string
  /** Character span of the tightest window covering all query terms (smaller = closer). */
  score: number
  /** Word distance of the tightest window — number of tokens between the outermost matched
   *  words (0 = adjacent). This is the primary relevancy key. */
  wordDistance: number
  snippet: string
  /** Concrete index terms that matched — one flat list of all expanded forms across all query groups.
   *  Used by the book view to highlight the actual matched words (e.g. the fuzzy expansion ביצחק
   *  when the query was יצחק~) rather than the raw query string. */
  matchedTerms: string[]
}

/**
 * Reason codes for a failed search, returned by C# in the searchError event
 * or as a failReason on the FtsSearchStart reply.
 *
 * indexNotReady  — index has not been built yet or is still building
 * indexMerging   — a segment merge is in progress; retry in a moment
 * searchFailed   — unexpected error during search execution
 */
export type SearchFailReason = 'indexNotReady' | 'indexMerging' | 'searchFailed'

/**
 * How the full-text-search results are ordered.
 *
 * lineId        — original returned order (ascending line ID = document order). Default.
 * relevance     — by minimum word distance (0 = adjacent), then line ID as a tiebreaker.
 * bookName      — alphabetically by book title (Hebrew collation), then line ID within a book.
 * authorName    — alphabetically by author name (Hebrew collation), then book name, then line ID.
 * chronological — by era (תנ"ך → חז"ל → ראשונים → אחרונים → …), then author year within an
 *                 era where known, then book name. Era-bucket order, not exact dates.
 *
 * Sorting is applied only after the search completes, so it never interferes with
 * the incremental streaming of results.
 */
export type FullTextSearchSortOrder = 'lineId' | 'relevance' | 'bookName' | 'authorName' | 'chronological'
