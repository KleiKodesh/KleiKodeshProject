/**
 * Book search query normalization.
 *
 * Applies Hebrew-specific text transformations to both the indexed book titles
 * (at catalog load time) and the user's search query (at search time), so that
 * variant spellings and common abbreviations all resolve to the same tokens.
 *
 * Add new rules here as more cases are discovered. Every rule must be applied
 * symmetrically: both when indexing titles and when processing the user query.
 */

/**
 * Maps all known variants of a title to its canonical form.
 * Each entry is [pattern, canonical] where pattern matches every accepted spelling.
 */
const TITLE_VARIANTS: [RegExp, string][] = [
  // שו"ע / שוע → שלחן ערוך
  [/שו["״]?ע/g, 'שלחן ערוך'],
  // שולחן (plene, with vav) → שלחן — standalone word normalization, not tied to שלחן ערוך
  [/שולחן/g, 'שלחן'],
]

/**
 * Normalize a book title or search query for matching.
 * Apply this to both sides — indexed titles and user input — so they meet at
 * the same canonical form.
 */
export function normalizeBookQuery(text: string): string {
  let result = text
  for (const [pattern, canonical] of TITLE_VARIANTS) result = result.replace(pattern, canonical)
  return result
}
