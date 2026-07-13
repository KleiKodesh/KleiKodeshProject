/**
 * Structural TOC keywords — words that begin TOC entry texts across the corpus
 * and that users type to address a location inside a book ("שלחן ערוך סימן ה",
 * "משנה תורה הלכות שבת").
 *
 * Used as an ADDITIVE trigger for the TOC heuristics: when a query contains one
 * of these words (with at least one book-matching word before it), the TOC
 * search runs even though the book-title search found results, and its items
 * are appended below the book results. The existing zero-results trigger and
 * all scoring logic are unchanged.
 *
 * Generated from the seforim DB (2026-07-13) by ranking the leading word of
 * every TOC entry text by breadth (distinct books) and frequency (entries),
 * then curating:
 *   - kept: structural locators (פרק 4576 books/90k entries, דף 1415/128k,
 *     סעיף 128/177k, פסוק 481/186k, סימן 447/94k, הלכה 2383/81k …)
 *   - dropped: commentary/title words that also lead TOC entries but would
 *     misfire on book titles (חידושי, תוספות, הגהות, פירוש, ספר, בית, תורה,
 *     דרך, נתיב, ילקוט, ליקוטים, שערי, פסקי, רבינו …) and gematria values
 *     (יא, כב, לג …).
 *
 * Regenerate the analysis with a query like:
 *   SELECT tt.text, COUNT(DISTINCT te.bookId) books, COUNT(*) entries
 *   FROM tocEntry te JOIN tocText tt ON tt.id = te.textId
 *   GROUP BY te.textId — then aggregate by leading word.
 */

import { normalize } from '@/utils/normalizeText'
import { normalizeBookPath } from './bookCatalogSearchNormalizer'

export const TOC_KEYWORDS = [
  // chapter/section units
  'פרק',
  'פסוק',
  'דף',
  'עמוד',
  'הלכה',
  'הלכות',
  'משנה',
  'סימן',
  'סעיף',
  'שער',
  'חלק',
  'פסקה',
  'פרשה',
  'פרשת',
  'מזמור',
  'רמז',
  'מצוה',
  'כלל',
  'אות',
  // responsa / essay units
  'תשובה',
  'שאלה',
  'אגרת',
  'מאמר',
  'דרוש',
  // front/back matter
  'הקדמה',
  'פתיחה',
  'קונטרס',
] as const

/**
 * Keyword set in the same normalized form as search query words
 * (normalize + normalizeBookPath), so lookup is a straight Set.has on a
 * normalized query word.
 */
const normalizedKeywordSet = new Set(
  TOC_KEYWORDS.flatMap((keyword) =>
    normalizeBookPath(normalize(keyword))
      .split(/\s+/)
      .filter((word) => word.length > 0),
  ),
)

/** Whether a normalized query word is a structural TOC keyword. */
export function isTocKeyword(normalizedWord: string): boolean {
  return normalizedKeywordSet.has(normalizedWord)
}
