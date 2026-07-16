/**
 * Chronological ordering for full-text-search results.
 *
 * The seforim DB has no per-book date column, so chronology is derived in two layers:
 *
 *   1. ERA rank (this file, `eraRank`) — from the book's `period` label, which
 *      booksDataStore computes from the category tree (see bookCatalogTree.detectPeriod /
 *      findCategoryMeta). The category tree organizes books into era sub-categories
 *      (ראשונים / אחרונים / מחברי זמננו) under each topic, so the era label is a real
 *      structural signal, not a guess. This gives 100% coverage and is the primary key.
 *
 *   2. AUTHOR YEAR (Stage 2, `authorYear`) — an optional within-era refinement: a curated
 *      author→approx-death-year map (sourced from המכלול / chronological charts). When a
 *      hit's book has a known author year, it orders books *within* the same era. Missing
 *      authors simply fall back to the era rank + book-name tiebreak.
 *
 * A precise year sort is NOT possible from this DB (the only year column, pub_date, is the
 * print-edition year, not composition), so this is deliberately an era-bucket sort refined
 * by author year where we have it — never a claim of exact chronology.
 */

/**
 * Chronological rank for each `period` label produced by findCategoryMeta/detectPeriod.
 * Lower = earlier. Values are spaced so intermediate buckets can be inserted later without
 * renumbering. Topic-only labels (הלכה, קבלה, שו״ת, מוסר…) have no intrinsic era, so they
 * get a late-medieval-ish middle rank — they still cluster together and sort after the
 * clearly-early strata and before clearly-modern ones.
 */
const ERA_RANK: Record<string, number> = {
  'תנ"ך': 10,
  'תנ״ך': 10,
  'משנה': 20,
  'תוספתא': 25,
  'תלמוד': 30,
  'מדרש': 40,
  'ספרות חז"ל': 40,
  'גאונים': 50,
  'ראשונים': 60,
  'אחרונים': 80,
  'חסידות': 85,
  'מחברי זמננו': 95,
}

// Fallback for period labels not in ERA_RANK (topic-only roots like הלכה/קבלה/שו״ת, or
// commentary buckets like "מפרשים על X" / "על התלמוד"). Placed between ראשונים and
// אחרונים so topical collections land in the broad medieval/early-modern middle.
const ERA_RANK_FALLBACK = 70

/** Sentinel rank for hits whose book/period is unknown — sort to the very end. */
const ERA_RANK_UNKNOWN = 9999

/** Rank a period label chronologically. `null`/unknown → sorts last. */
export function eraRank(period: string | null | undefined): number {
  if (!period) return ERA_RANK_UNKNOWN
  const exact = ERA_RANK[period]
  if (exact !== undefined) return exact
  // "מפרשים על …" and "על ה…" commentary buckets — treat as the medieval/early-modern middle.
  return ERA_RANK_FALLBACK
}

// ── Author death-year refinement (within-era key) ──────────────────────────────
//
// authorYears.json maps a NORMALIZED author name → approximate Gregorian death year,
// curated from המכלול / Torat Emet / chronological charts (373 of the DB's 384 authors;
// the ~11 omitted are living/unidentifiable and simply fall back to era-only ordering).
// The frontend only has each book's authors as a comma-joined name string (book.authors),
// so we normalize identically on both sides — strip Hebrew nikud/te'amim, quote glyphs,
// and directional marks — and match a name substring.
import authorYearsData from './authorYears.json'

const AUTHOR_YEARS: Record<string, number> = authorYearsData

/** Strip Hebrew diacritics, quote glyphs, and directional marks; collapse space; lowercase.
 *  Must match the normalization used when authorYears.json was generated. */
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
