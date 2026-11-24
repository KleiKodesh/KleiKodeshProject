/**
 * Chronological ordering for full-text-search results.
 *
 * The seforim DB has no per-book date column, so chronology is resolved in four rungs,
 * most specific first:
 *
 *   1. AUTHOR YEAR (primary) — authorYears.ts maps a normalized author name to an
 *      approximate Gregorian death year, web-sourced per author against HaMichlol /
 *      Wikidata / NLI+HebrewBooks authority records. A death year is used (not birth)
 *      because what we are ordering is *works*: output clusters near the end of a life,
 *      so a birth year would place a long-lived author a full generation before the
 *      contemporaries he was actually writing alongside.
 *
 *   2. WORK STEM (workStemYears.ts) — multi-volume works split one volume per tractate.
 *      Every volume has a different title, so only the category path names the work.
 *
 *   3. CANONICAL WORK (canonicalWorkYears.ts) — the anonymous classics (Torah, Mishnah,
 *      Tosefta, both Talmuds, midrashim, Targumim), dated per work by TRADITIONAL
 *      attribution. They have no author to look up, but their era is not in doubt.
 *
 *   4. ERA CATEGORY (fallback) — only the three tree categories that genuinely encode an
 *      era: ראשונים / אחרונים / מחברי זמננו. These are real structural era subcategories,
 *      not subject labels, so they can be trusted. Everything else in the category tree
 *      answers "what shelf is this on", not "when was it written" — a topical root title
 *      (הלכה, קבלה, תנ"ך…) says nothing about a book's date, and reading one as a date is
 *      what made the previous implementation misplace books by centuries (a Bible-root
 *      bucket of 23 books held exactly one biblical text; the rest were 16th-20th c.
 *      sermon collections, live-verified 2026-08-13 against pub_date and an independent
 *      composition-date source).
 *
 *   5. UNKNOWN — none of the above. Sorts LAST, deliberately: an honest "we don't know" at
 *      the end beats a fabricated position in the middle. Liturgy (a printed siddur layers
 *      a Second Temple core under a 19th-century rite edition), modern anthologies, and
 *      works whose author could not be identified all legitimately land here.
 *
 * A precise year sort is NOT possible from this DB (the only year column, pub_date, is the
 * print-edition year, not composition), so this is deliberately an author-year sort with an
 * era-bucket fallback — never a claim of exact chronology.
 */

// AUTHOR_YEARS maps a NORMALIZED author name → approximate Gregorian death year. The
// frontend only has each book's authors as a comma-joined name string (book.authors), so we
// normalize identically on both sides — strip Hebrew nikud/te'amim, quote glyphs, and
// directional marks — and match a name substring.
import { AUTHOR_YEARS } from './authorYears'
import { CANONICAL_WORK_YEARS } from './canonicalWorkYears'
import { WORK_STEM_YEARS, WORK_STEM_UNDATED } from './workStemYears'

/**
 * Representative year for each of the three era categories that actually encode an era.
 * These are the midpoints of the conventional windows (Rishonim ~1000-1500, Acharonim
 * ~1500-1900, contemporary ~1900-today), used so an undated book from an era interleaves
 * sensibly with the dated books around it rather than clumping at an era boundary.
 */
const ERA_YEAR: Record<string, number> = {
  'ראשונים': 1250,
  'אחרונים': 1700,
  'מחברי זמננו': 1960,
}

/**
 * Sentinel year for books with neither an author year nor an era category — sorts last.
 * A large FINITE value, not Infinity: the comparator subtracts these, and Infinity-Infinity
 * is NaN, which would make the sort's ordering depend on NaN falling through to the next
 * tiebreak. A finite sentinel keeps the subtraction well-defined.
 */
const YEAR_UNKNOWN = 999999

/**
 * Representative year for a book's era, from the `period` label findCategoryMeta derived.
 * Only the three era labels resolve; every other label (topical roots, "מפרשים על X"
 * commentary buckets, and the 'אחר' default booksDataStore assigns when findCategoryMeta
 * returns null) is treated as era-unknown rather than being given a fabricated position.
 */
function eraYear(period: string | null | undefined): number {
  if (!period) return YEAR_UNKNOWN
  const exact = ERA_YEAR[period]
  return exact !== undefined ? exact : YEAR_UNKNOWN
}

/** Strip Hebrew diacritics, quote glyphs, and directional marks; collapse space; lowercase.
 *  Must match the normalization used when authorYears.ts in this folder was generated. */
function normAuthor(s: string): string {
  return s
    .replace(/[֑-ׇ]/g, '') // Hebrew nikud + te'amim
    .replace(/["'״׳‎‏]/g, '') // quotes, gershayim/geresh, LRM/RLM
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase()
}

/**
 * Approximate death year for a book's author(s), or null when unknown. `authors` is the
 * comma-joined name string from book.authors. When a book lists several authors we take
 * the EARLIEST known year (the base author of a work generally predates its annotators),
 * which keeps a commented base text sorting by the base author's era, not a later glossator.
 */
export function authorYear(authors: string | null | undefined): number | null {
  if (!authors) return null
  let best: number | null = null
  for (const raw of authors.split(',')) {
    const key = normAuthor(raw)
    if (!key) continue
    const year = AUTHOR_YEARS[key]
    if (year != null && (best === null || year < best)) best = year
  }
  return best
}

/**
 * Combined chronological sort key for a book: a single `year` axis plus a `precision` tier
 * that breaks ties between an exact author year and an era estimate landing on the same
 * value.
 *
 *   - author year known  → that year, precision 0. A measured year outranks an inferred
 *     bucket, and because everything shares one axis a dated book interleaves correctly
 *     across shelves instead of being trapped in a category bucket.
 *   - author year absent → the era category's representative year, precision 1, so an exact
 *     1700 leads an "Acharonim, circa 1700" estimate rather than tying arbitrarily.
 *   - neither            → Infinity, sorting LAST.
 *
 * `rank` is retained as an alias of `year` for the existing comparator, which reads
 * `rank` then `year`; the two-field shape keeps that call site working unchanged.
 *
 * Accepts undefined so callers can pass an allBooksMap miss straight through.
 */
export function chronologicalKey(
  book:
    | { period?: string | null; authors?: string | null; title?: string | null; parentPath?: string | null }
    | undefined,
): { rank: number; year: number; precision: number } {
  if (!book) return { rank: YEAR_UNKNOWN, year: YEAR_UNKNOWN, precision: 2 }

  const exact = authorYear(book.authors)
  if (exact !== null) return { rank: exact, year: exact, precision: 0 }

  // Multi-volume works split one volume per tractate: every volume carries a different
  // title, so only the category path identifies the work.
  //
  // This MUST be checked before the canonical-title table. Those volumes are titled with a
  // bare tractate name — a commentary volume titled "Berakhot" would otherwise match the
  // Bavli tractate "Berakhot" and be dated to 500 CE instead of its author's era.
  if (book.parentPath) {
    for (const [stem, year] of WORK_STEM_YEARS) {
      if (book.parentPath.includes(stem)) return { rank: year, year, precision: 0 }
    }
    // Known works we deliberately leave undated — stop here rather than let their
    // tractate-named volumes fall through to the canonical table below.
    for (const stem of WORK_STEM_UNDATED) {
      if (book.parentPath.includes(stem)) {
        return { rank: YEAR_UNKNOWN, year: YEAR_UNKNOWN, precision: 2 }
      }
    }
  }

  // Canonical anonymous works — no author to look up, but a well-established traditional
  // date. Checked before the era category because it is per-work and far more precise.
  if (book.title) {
    const canonical = CANONICAL_WORK_YEARS[book.title.trim()]
    if (canonical !== undefined) return { rank: canonical, year: canonical, precision: 0 }
  }

  const era = eraYear(book.period)
  return { rank: era, year: era, precision: era === YEAR_UNKNOWN ? 2 : 1 }
}
