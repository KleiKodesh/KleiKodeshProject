/**
 * Book query matching and scoring.
 *
 * Scores a set of normalized query words against a book's pre-built token list.
 * Used by filterBooksByWords in bookCatalogTree.ts.
 *
 * Match tiers (per query word, best token wins):
 *   EXACT    (3) — token equals word, or is a חסר/מלא spelling variant
 *   PREFIX   (2) — token starts with word
 *   NONE     (0) — no match → book is disqualified
 *
 * The catalog-best rule: for each query word, the highest tier achieved by any
 * book in the catalog becomes the required tier for that word. Books that only
 * reach a lower tier for any word are dropped entirely. This means if exact
 * matches exist for a word, prefix-only books are discarded.
 */

import { decomposeHebrewWord, areDecomposedVariants } from './bookCatalogSearchNormalizer'
import type { DecomposedToken } from './bookCatalogSearchNormalizer'

export const SCORE_EXACT = 3
export const SCORE_PREFIX = 2
export const SCORE_NONE = 0

/**
 * A pre-decomposed query word, built once per search before the book loop.
 * Avoids re-decomposing the same query word for every book token.
 */
export interface PreparedQueryWord {
  raw: string
  decomposed: DecomposedToken
}

/** Decompose a query word once before the search loop. */
export function prepareQueryWord(word: string): PreparedQueryWord {
  return { raw: word, decomposed: decomposeHebrewWord(word) }
}

/**
 * Score a single prepared query word against a book's pre-decomposed token list.
 * Returns the best tier achieved across all tokens.
 *
 * Both the query word and the tokens must already be normalized.
 * Tokens must be pre-decomposed via decomposeHebrewWord at index-build time.
 */
export function scoreWordAgainstTokens(
  query: PreparedQueryWord,
  tokens: string[],
  decomposedTokens: DecomposedToken[],
): number {
  let best = SCORE_NONE
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i]!
    if (token === query.raw || areDecomposedVariants(decomposedTokens[i]!, query.decomposed)) {
      return SCORE_EXACT
    }
    if (best < SCORE_PREFIX && token.startsWith(query.raw)) best = SCORE_PREFIX
  }
  return best
}
