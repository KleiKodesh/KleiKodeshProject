/**
 * Builds the clipboard payload for a selection spanning full-text-search results.
 *
 * WHY THIS IS NOT JUST THE DOM SELECTION
 * The results list is windowed (only the rows around the viewport are mounted), so a
 * selection dragged over a hundred results only has a screenful of them in the DOM —
 * the rest were never mounted or were unmounted away. Serializing the live Range would silently drop them, so the rows
 * between the selection's two ends are rebuilt from the result model instead. The
 * two END rows come from the model too: a result is one search hit, and half a
 * citation is not a useful thing to paste.
 *
 * WHAT A COPIED RESULT LOOKS LIKE
 * Each result becomes its reference followed by its snippet, so a range of results
 * pastes as a readable list of citations rather than a wall of unattributed text:
 *
 *   <book title> › <toc path>
 *   <snippet text>
 *
 * The reference is the same "title › path" the row shows on screen. Results with no
 * TOC path just carry the title.
 */
import { escapeHtml, htmlToText } from '@/utils/htmlText'
import { cleanHebrewText } from '@/utils/hebrewTextCleaning'
import type { FullTextSearchResult } from './fullTextSearchTypes'

/** The separator between a book title and its TOC path, as the rows display it. */
const REFERENCE_SEPARATOR = ' › '

/**
 * A result's citation: its book title, then its TOC path when it has one. Both are
 * resolved by the caller from the hit's ids (fullTextSearchReferences) — a hit itself
 * carries no text.
 *
 * Exported because the row's own tooltip shows the same thing (see resultTitle in
 * FullTextSearchResultsList) — one definition so a copied citation and a displayed
 * one can never drift apart.
 */
export function resultReference(bookTitle: string, tocPath: string): string {
  return tocPath ? `${bookTitle}${REFERENCE_SEPARATOR}${tocPath}` : bookTitle
}

/**
 * One result as an HTML block: a reference line, then the snippet's own text.
 *
 * The snippet comes as rendered HTML carrying the search-match <mark> elements.
 * Those are stripped to plain text rather than carried over: a highlight is a
 * reading aid for this search, not part of the quoted source, and pasting it into
 * Word imports the mark styling with it.
 */
function resultToHtml(
  result: FullTextSearchResult,
  renderSnippet: (result: FullTextSearchResult) => string,
  renderReference: (result: FullTextSearchResult) => string,
): string {
  // Tags out first, then entities decoded: htmlToText expects tag-free input (see its
  // contract), and the snippet still carries the search-match <mark> elements.
  //
  // Stripped to NOTHING, not to a space — and that is deliberately unlike the
  // book-view copy paths, which replace a tag with a space. Their input is multi-line
  // block HTML where a tag marks a place a space genuinely belongs (without it the
  // last word of one line runs into the first word of the next). A snippet is a
  // single continuous line whose only tags are <mark> pairs sitting mid-phrase, so
  // every inserted space would be spurious: the scanner treats the maqaf — the Hebrew
  // word-joining hyphen — as a token boundary that still renders, so a match starting
  // after one puts <mark> flush against a visible character, and a space there splits
  // a word that belongs together.
  const snippetText = htmlToText(renderSnippet(result).replace(/<[^>]*>/g, '')).trim()
  return (
    `<div dir="rtl"><b>${escapeHtml(renderReference(result))}</b></div>` +
    (snippetText ? `<div dir="rtl">${escapeHtml(snippetText)}</div>` : '')
  )
}

/**
 * The selected results as HTML, or null when the selection covers no result rows.
 *
 * `firstRowIndex`/`lastRowIndex` come from the pinned-row tracker, which is what
 * makes this windowing-independent: those two rows are held in the DOM for the
 * life of the selection, so their indexes stay valid however far the list scrolls.
 */
export function buildSelectedResultsHtml(
  results: FullTextSearchResult[],
  rowIndexRange: { firstRowIndex: number; lastRowIndex: number } | null,
  /** The result's rendered snippet HTML ('' when its text has not been fetched — the
   *  snippet is built on demand, see fullTextSearchSnippets.ts). */
  renderSnippet: (result: FullTextSearchResult) => string,
  /** The result's citation ("title › path"), resolved from its ids by the caller. */
  renderReference: (result: FullTextSearchResult) => string,
  cleanText: boolean,
): string | null {
  if (!rowIndexRange) return null
  const { firstRowIndex, lastRowIndex } = rowIndexRange
  // A selection inside ONE result needs nothing from the model: the DOM already holds
  // exactly what the user highlighted, so let the native copy take it. Rebuilding here
  // would replace a double-clicked word, or a selected book title, with the result's
  // whole snippet — which is both surprising and often far more text than is on screen
  // (the snippet is line-clamped in the row). Matches the book-view copy path, which
  // refuses the single-row case for the same reason.
  if (firstRowIndex >= lastRowIndex) return null
  const blocks: string[] = []
  for (let rowIndex = firstRowIndex; rowIndex <= lastRowIndex; rowIndex++) {
    const result = results[rowIndex]
    if (!result) continue
    blocks.push(resultToHtml(result, renderSnippet, renderReference))
  }
  if (!blocks.length) return null
  const html = blocks.join('')
  // The same "clean text" toggle the book view and the global menu honour, applied
  // once over the whole payload rather than per result.
  return cleanText ? cleanHebrewText(html) : html
}
