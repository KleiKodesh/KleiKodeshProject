/**
 * One full-text hit as the service streams it and as the IndexedDB search cache stores it.
 *
 * NUMBERS ONLY — every field is an int or a list of ints. A high-frequency word is hundreds
 * of thousands of hits held for the life of the result set and cached on disk, so nothing
 * per hit may be a string. What a row DISPLAYS is resolved on demand, by id, for rendered
 * rows only: the book title from `booksDataStore.allBooksMap` and the TOC path by line id
 * (fullTextSearchReferences.ts), the snippet from its line's text
 * (fullTextSearchSnippets.ts), the matched terms from the search's term table via the
 * term id of each `snippetMarks` triple (useFullTextSearch's matchedTermsOf).
 *
 * Mirror of FtsHit in SeforimDbFtsModels.cs (the service's wire type).
 */
export interface FullTextSearchResult {
  lineId: number
  bookId: number
  /** Character span of the tightest window covering all query terms (smaller = closer). */
  score: number
  /** Word distance of the tightest window — number of tokens between the outermost matched
   *  words (0 = adjacent). This is the primary relevancy key. */
  wordDistance: number
  /**
   * Snippet DESCRIPTOR over the MATCHED LINE's text, located once by the search: the raw
   * window [snippetStart, snippetEnd) with `snippetMarks` highlighted — flat (start, length,
   * termId) triples (SNIPPET_MARK_STRIDE), the term id naming which term that mark matched.
   * Rebuilt on demand for rendered rows by fullTextSearchSnippets.ts.
   */
  snippetStart: number
  snippetEnd: number
  /** A plain array off the wire; a view into its chunk's packed array when read from the cache. */
  snippetMarks: number[] | Int32Array
  /** Bit flags — see SNIPPET_FLAG_LEADING_ELLIPSIS / _TRAILING_ELLIPSIS / _SHORT. */
  snippetFlags: number
}

/** "…" before the snippet window — it starts mid-text. */
export const SNIPPET_FLAG_LEADING_ELLIPSIS = 1
/** "…" after the snippet window — it ends before the text does. */
export const SNIPPET_FLAG_TRAILING_ELLIPSIS = 2
/** The window holds fewer words than the requested context: shown with its neighbour lines as plain context. */
export const SNIPPET_FLAG_SHORT = 4
/** Ints per entry of `snippetMarks`: start, length, termId. */
export const SNIPPET_MARK_STRIDE = 3

/**
 * The distinct ids (into the search's term table) of the concrete index terms that occur in
 * this hit's marks — e.g. the one fuzzy expansion of a `~` query word this line contains, not
 * the raw query, not the whole expansion: typically 1-3. Read off the marks' third ints; the
 * service does not send them separately.
 */
export function matchedTermIdsOf(hit: FullTextSearchResult): number[] {
  const marks = hit.snippetMarks ?? []
  const ids: number[] = []
  for (let i = SNIPPET_MARK_STRIDE - 1; i < marks.length; i += SNIPPET_MARK_STRIDE) {
    const id = marks[i]
    if (id !== undefined && !ids.includes(id)) ids.push(id)
  }
  return ids
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
 * catalogOrder  — the order the service streams: the library's catalog tree order, each
 *                 book in reading order. Default.
 * relevance     — by minimum word distance (0 = adjacent), then catalog order.
 * bookName      — alphabetically by book title (Hebrew collation), then catalog order.
 * authorName    — alphabetically by author name (Hebrew collation), then book name, then catalog order.
 * chronological — by era (תנ"ך → חז"ל → ראשונים → אחרונים → …), then author year within an
 *                 era where known, then catalog tree order. Era-bucket order, not exact dates.
 *
 * Sorting is applied only after the search completes, so it never interferes with
 * the incremental streaming of results.
 */
export type FullTextSearchSortOrder = 'catalogOrder' | 'relevance' | 'bookName' | 'authorName' | 'chronological'
