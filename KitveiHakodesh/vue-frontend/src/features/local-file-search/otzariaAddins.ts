/**
 * Otzaria addin handling for file search — shared by every consumer of
 * `fileSystemSearch` (the file-search page, the home search bar, the address bar).
 *
 * The DocumentLocator index bakes an addin entry point's display name into its own
 * field as "תוסף אוצריא: {name}". Three things follow from that, and they have to
 * agree everywhere or the same query behaves differently depending on which input
 * the user typed it into:
 *
 *  • Query — users type the shorthand "תוספים", which matches nothing in the index.
 *    It is rewritten to the baked prefix, so it hits the same *word* wildcards as
 *    any other term.
 *  • Title — an addin result is presented by its addin name (prefix stripped), not
 *    by its file name, which is a meaningless index.html.
 *  • Tab flag — the opened tab carries isOtzariaAddin so HtmlViewPage activates the
 *    addin bridge (and the recents entry inherits the puzzle-piece icon).
 */

/**
 * The shorthand users type, with or without a trailing colon.
 *
 * Word-initial only: an unanchored match would fire inside ordinary words, and
 * these inputs are where people type book titles. "בעלי התוספים" must stay a
 * book query — matching it would both mis-rank the sections and rewrite the
 * term into the nonsense "בעלי התוסף אוצריא: ".
 */
const SHORTHAND = /(^|\s)תוספים:?\s*/

/**
 * Rewrites the "תוספים" shorthand to the prefix the index actually stores.
 * Built from SHORTHAND so the rewrite rule can never drift from the ranking
 * rule below; $1 preserves the boundary the pattern consumed.
 */
export function normalizeAddinQuery(query: string): string {
  return query.replace(new RegExp(SHORTHAND.source, 'g'), '$1תוסף אוצריא: ')
}

/**
 * True when the query is asking for addins. The home/address-bar search uses this
 * to rank file results above the book catalog, the same way the "מחשב"/"קובץ"
 * prefixes do — except this term is a real search term, so it is never stripped.
 */
export function queryTargetsAddins(query: string): boolean {
  return SHORTHAND.test(query)
}

/** Title to show for an addin result: the baked index prefix stripped off. */
export function addinDisplayTitle(addinName: string): string {
  return addinName.replace(/^תוסף אוצריא:\s*/u, '').trim()
}
