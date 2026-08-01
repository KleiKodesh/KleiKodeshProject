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

/** The shorthand users type, with or without a trailing colon. */
const SHORTHAND = /תוספים:?\s*/

/** Rewrites the "תוספים" shorthand to the prefix the index actually stores. */
export function normalizeAddinQuery(query: string): string {
  return query.replace(/תוספים:?\s*/g, 'תוסף אוצריא: ')
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
