export interface FullTextSearchResult {
  lineId: number
  bookId: number
  bookTitle: string
  tocText: string
  score: number
  snippet: string
  /** Concrete index terms that matched — one flat list of all expanded forms across all query groups.
   *  Used by the book view to highlight the actual matched words (e.g. the fuzzy expansion ביצחק
   *  when the query was יצחק~) rather than the raw query string. */
  matchedTerms: string[]
}
