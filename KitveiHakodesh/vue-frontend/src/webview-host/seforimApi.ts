// Semantic seforim-DB access layer.
//
// Dev routes each capability through the KitveiHakodesh service (serviceCall) so
// the browser build sends no seforim SQL. Hosted (C#) still runs the SQL from
// queries.sql.ts via the __webviewQuery bridge, unchanged, until the C# migration.
//
// Migrate one capability at a time: add a function here (dev → service op,
// hosted → query(SQL.X)) and point the composable at it instead of query(SQL.X).

import { query, categoryHasOrderIndex } from './seforimDb'
import { serviceCall } from './serviceClient'
import { SQL } from './queries.sql'
import type {
  BookRow, CategoryRow, BookInfo, TocEntry, AltTocStructure,
  LineRow, ReverseLineRow, CommentaryLinkRow, WordLinkAnchor,
} from './queries.types'

/** True when the C# seforim bridge is present (hosted). Dev falls to the service. */
export const isDbHosted = (): boolean => typeof window.__webviewQuery === 'function'

// ── Catalog ─────────────────────────────────────────────────────────────────

export async function getAllCategories(): Promise<CategoryRow[]> {
  if (!isDbHosted()) return (await serviceCall<{ rows: CategoryRow[] }>('getAllCategories')).rows
  return query<CategoryRow>(SQL.GET_ALL_CATEGORIES(categoryHasOrderIndex))
}

export async function getAllBooks(): Promise<BookRow[]> {
  if (!isDbHosted()) return (await serviceCall<{ rows: BookRow[] }>('getAllBooks')).rows
  return query<BookRow>(SQL.GET_ALL_BOOKS)
}

// ── Book + lines ──────────────────────────────────────────────────────────────

export async function getBookById(id: number): Promise<BookInfo | undefined> {
  if (!isDbHosted())
    return (await serviceCall<{ book: BookInfo | null }>('getBookById', { id })).book ?? undefined
  return (await query<BookInfo>(SQL.GET_BOOK_BY_ID, [id]))[0]
}

export async function getLinesPaged(bookId: number, limit: number, offset: number): Promise<LineRow[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: LineRow[] }>('getLinesPaged', { bookId, limit, offset })).rows
  return query<LineRow>(SQL.GET_LINES_PAGED, [bookId, limit, offset])
}

// ── TOC ─────────────────────────────────────────────────────────────────────

export async function getAllTocEntries(bookId: number): Promise<TocEntry[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: TocEntry[] }>('getAllTocEntries', { bookId })).rows
  return query<TocEntry>(SQL.GET_ALL_TOC_ENTRIES, [bookId])
}

export async function getAltTocStructures(bookId: number): Promise<AltTocStructure[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: AltTocStructure[] }>('getAltTocStructures', { bookId })).rows
  return query<AltTocStructure>(SQL.GET_ALT_TOC_STRUCTURES, [bookId])
}

export async function getAllAltTocEntries(structureId: number): Promise<TocEntry[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: TocEntry[] }>('getAllAltTocEntries', { structureId })).rows
  return query<TocEntry>(SQL.GET_ALL_ALT_TOC_ENTRIES, [structureId])
}

/** Daf-yomi navigation: first TOC entry whose text matches the LIKE `pattern`. */
export async function getTocEntryByTextPrefix(
  bookId: number,
  pattern: string,
): Promise<{ id: number; lineIndex: number | null }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; lineIndex: number | null }[] }>(
      'getTocEntryByTextPrefix', { bookId, pattern },
    )).rows
  return query<{ id: number; lineIndex: number | null }>(SQL.GET_TOC_ENTRY_BY_TEXT_PREFIX, [bookId, pattern])
}

// ── Commentary / links ────────────────────────────────────────────────────────

// Whether link.targetLineIndex exists in the hosted DB (Zayit: yes, Otzaria: no —
// verified 2026-07-19 against both real DBs; both schemas evolve independently, so
// RE-VERIFY when either DB project ships a new schema version).
// Probed once per page load; the fast denormalized query is only valid when the
// column exists — the probe, not the DB flavor, is what decides.
let _linkHasTargetLineIndex: boolean | null = null
async function linkHasTargetLineIndex(): Promise<boolean> {
  if (_linkHasTargetLineIndex != null) return _linkHasTargetLineIndex
  try {
    const rows = await query<{ n: number }>(SQL.HAS_LINK_TARGET_LINE_INDEX)
    _linkHasTargetLineIndex = (rows[0]?.n ?? 0) > 0
  } catch {
    _linkHasTargetLineIndex = false // the JOIN fallback works on every schema
  }
  return _linkHasTargetLineIndex
}

/** Links-only forward commentary for a range of source lines (content backfilled separately). */
export async function getCommentaryLinksForSourceLineRange(lineIds: number[]): Promise<CommentaryLinkRow[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: CommentaryLinkRow[] }>('getCommentaryLinksForSourceLineRange', { lineIds })).rows
  if (await linkHasTargetLineIndex()) {
    try {
      return await query<CommentaryLinkRow>(SQL.GET_COMMENTARY_LINKS_FOR_SOURCE_LINE_RANGE(lineIds.length), lineIds)
    } catch {
      // DB swapped under us (user picked a different seforim DB without a reload) —
      // remember and fall through to the portable JOIN.
      _linkHasTargetLineIndex = false
    }
  }
  return query<CommentaryLinkRow>(SQL.GET_COMMENTARY_LINKS_FOR_SOURCE_LINE_RANGE_JOIN(lineIds.length), lineIds)
}

// ── Word-level link anchors (link_anchor, SeforimLibrary schema v2+) ──────────

// Whether the open DB has the link_anchor table (schema v2+; current Zayit/Otzaria v1
// DBs don't). null = unknown. Once false, callers get [] without touching the DB again —
// the DB is static per page load (same lifecycle as _linkHasTargetLineIndex above).
let _hasLinkAnchors: boolean | null = null

/** Word-level link anchors for a batch of source lines. [] on schema-v1 DBs — always safe to call. */
export async function getWordLinkAnchorsForLines(lineIds: number[]): Promise<WordLinkAnchor[]> {
  if (_hasLinkAnchors === false || lineIds.length === 0) return []
  if (!isDbHosted()) {
    const res = await serviceCall<{ supported: boolean; rows: WordLinkAnchor[] }>(
      'getWordLinkAnchorsForLines', { lineIds },
    )
    if (!res.supported) _hasLinkAnchors = false
    return res.rows
  }
  if (_hasLinkAnchors == null) {
    try {
      const rows = await query<{ n: number }>(SQL.HAS_LINK_ANCHOR_TABLE)
      _hasLinkAnchors = (rows[0]?.n ?? 0) > 0
    } catch {
      return [] // DB not ready — leave unknown so the next call re-probes
    }
    if (!_hasLinkAnchors) return []
  }
  try {
    return await query<WordLinkAnchor>(SQL.GET_WORD_LINK_ANCHORS_FOR_LINES(lineIds.length), lineIds)
  } catch {
    // DB swapped under us (user picked a different seforim DB without a reload) — re-probe next call.
    _hasLinkAnchors = null
    return []
  }
}

/** Content backfill for a batch of line ids. */
export async function getLineContents(lineIds: number[]): Promise<{ id: number; content: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; content: string }[] }>('getLineContents', { lineIds })).rows
  return query<{ id: number; content: string }>(SQL.GET_LINE_CONTENTS(lineIds.length), lineIds)
}

export async function getAllConnectionTypes(): Promise<{ id: number; name: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; name: string }[] }>('getAllConnectionTypes')).rows
  return query<{ id: number; name: string }>(SQL.GET_ALL_CONNECTION_TYPES)
}

export async function getDefaultCommentators(bookId: number): Promise<{ commentatorBookId: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { commentatorBookId: number }[] }>('getDefaultCommentators', { bookId })).rows
  return query<{ commentatorBookId: number }>(SQL.GET_DEFAULT_COMMENTATORS, [bookId])
}

// ── Reverse lookups (source & targum) + static filter books ────────────────────

/** Reverse source/targum lookup — pass commentary type ids for source, targum type ids for targum. */
export async function getReverseLineData(lineIds: number[], typeIds: number[]): Promise<ReverseLineRow[]> {
  if (lineIds.length === 0 || typeIds.length === 0) return []
  if (!isDbHosted())
    return (await serviceCall<{ rows: ReverseLineRow[] }>('getReverseLineData', { lineIds, typeIds })).rows
  const isMulti = lineIds.length > 1
  const sql = isMulti
    ? SQL.GET_SOURCE_DATA_BY_REVERSE_COMMENTARY_LOOKUP_RANGE(typeIds.length, lineIds.length)
    : SQL.GET_SOURCE_DATA_BY_REVERSE_COMMENTARY_LOOKUP(typeIds.length)
  const params = isMulti ? [...lineIds, ...typeIds] : [lineIds[0]!, ...typeIds]
  return query<ReverseLineRow>(sql, params)
}

/** Distinct source/targum books linking to the given base book via the given connection types. */
export async function getReverseBooks(bookId: number, typeIds: number[]): Promise<{ sourceBookId: number }[]> {
  if (typeIds.length === 0) return []
  if (!isDbHosted())
    return (await serviceCall<{ rows: { sourceBookId: number }[] }>('getReverseBooks', { bookId, typeIds })).rows
  return query<{ sourceBookId: number }>(
    SQL.GET_SOURCE_BOOKS_BY_REVERSE_COMMENTARY_LOOKUP(typeIds.length),
    [bookId, ...typeIds],
  )
}

export async function getStaticFilterBooks(
  sourceBookId: number,
  typeIds: number[],
): Promise<{ targetBookId: number; connectionTypeId: number }[]> {
  if (typeIds.length === 0) return []
  if (!isDbHosted())
    return (await serviceCall<{ rows: { targetBookId: number; connectionTypeId: number }[] }>(
      'getStaticFilterBooks', { sourceBookId, typeIds },
    )).rows
  return query<{ targetBookId: number; connectionTypeId: number }>(
    SQL.GET_STATIC_COMMENTARY_FILTER_BOOKS_FOR_SOURCE_BOOK(typeIds.length),
    [sourceBookId, ...typeIds],
  )
}

// ── Commentary navigation ─────────────────────────────────────────────────────

export async function getNextSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; lineIndex: number }[] }>('getSectionWithCommentary',
      { mainBookId, commentaryBookId, lineIndex, direction: 'next' })).rows
  return query<{ id: number; lineIndex: number }>(SQL.GET_NEXT_SECTION_WITH_COMMENTARY, [mainBookId, commentaryBookId, lineIndex])
}

export async function getPrevSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; lineIndex: number }[] }>('getSectionWithCommentary',
      { mainBookId, commentaryBookId, lineIndex, direction: 'prev' })).rows
  return query<{ id: number; lineIndex: number }>(SQL.GET_PREV_SECTION_WITH_COMMENTARY, [mainBookId, commentaryBookId, lineIndex])
}

export async function getNextTocSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, rangePairs: number[],
): Promise<{ sectionStart: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { sectionStart: number }[] }>('getTocSectionWithCommentary',
      { mainBookId, commentaryBookId, rangePairs, direction: 'next' })).rows
  return query<{ sectionStart: number }>(
    SQL.GET_NEXT_TOC_SECTION_WITH_COMMENTARY(rangePairs.length / 2), [...rangePairs, mainBookId, commentaryBookId])
}

export async function getPrevTocSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, rangePairs: number[],
): Promise<{ sectionStart: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { sectionStart: number }[] }>('getTocSectionWithCommentary',
      { mainBookId, commentaryBookId, rangePairs, direction: 'prev' })).rows
  return query<{ sectionStart: number }>(
    SQL.GET_PREV_TOC_SECTION_WITH_COMMENTARY(rangePairs.length / 2), [...rangePairs, mainBookId, commentaryBookId])
}

export async function getLinkTargetForSourceLineAndBook(
  sourceLineId: number, targetBookId: number,
): Promise<{ targetLineId: number; lineIndex: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { targetLineId: number; lineIndex: number }[] }>('getLinkTargetForSourceLineAndBook',
      { sourceLineId, targetBookId })).rows
  return query<{ targetLineId: number; lineIndex: number }>(SQL.GET_LINK_TARGET_FOR_SOURCE_LINE_AND_BOOK, [sourceLineId, targetBookId])
}

// ── TOC paths & line→book/index helpers ───────────────────────────────────────

export async function getTocPathsForLines(lineIds: number[]): Promise<{ lineId: number; bookId: number; tocPath: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { lineId: number; bookId: number; tocPath: string }[] }>('getTocPathsForLines', { lineIds })).rows
  return query<{ lineId: number; bookId: number; tocPath: string }>(SQL.GET_TOC_PATHS_FOR_LINES(lineIds.length), lineIds)
}

/** triples = flat [groupKey, firstLineId, lastLineId, …]. */
export async function getEnclosingTocPathForLineRanges(triples: number[]): Promise<{ groupKey: number; bookId: number; tocPath: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { groupKey: number; bookId: number; tocPath: string }[] }>('getEnclosingTocPathForLineRanges', { triples })).rows
  return query<{ groupKey: number; bookId: number; tocPath: string }>(
    SQL.GET_ENCLOSING_TOC_PATH_FOR_LINE_RANGES(triples.length / 3), triples)
}

export async function getBookIdsForLines(lineIds: number[]): Promise<{ lineId: number; bookId: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { lineId: number; bookId: number }[] }>('getBookIdsForLines', { lineIds })).rows
  return query<{ lineId: number; bookId: number }>(SQL.GET_BOOK_IDS_FOR_LINES(lineIds.length), lineIds)
}

export async function getLineIndexFromLineId(lineId: number): Promise<{ lineIndex: number; bookId: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { lineIndex: number; bookId: number }[] }>('getLineIndexFromLineId', { lineId })).rows
  return query<{ lineIndex: number; bookId: number }>(SQL.GET_LINE_INDEX_FROM_LINE_ID, [lineId])
}

// ── Dictionary sources in the seforim DB (מצודת ציון, מלבי״ם, מנחם, ערוך) ──────

export async function getBookIdsByTitlePattern(pattern: string): Promise<{ id: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number }[] }>('getBookIdsByTitlePattern', { pattern })).rows
  return query<{ id: number }>(SQL.GET_BOOK_IDS_BY_TITLE_PATTERN, [pattern])
}

export async function getBookIdByExactTitle(title: string): Promise<{ id: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number }[] }>('getBookIdByExactTitle', { title })).rows
  return query<{ id: number }>(SQL.GET_BOOK_ID_BY_EXACT_TITLE, [title])
}

export async function getLinesWithContentPatternForBooks(
  bookIds: number[], pattern: string,
): Promise<{ content: string; title: string; bookId: number; lineId: number; lineIndex: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { content: string; title: string; bookId: number; lineId: number; lineIndex: number }[] }>(
      'getLinesWithContentPatternForBooks', { bookIds, pattern })).rows
  return query<{ content: string; title: string; bookId: number; lineId: number; lineIndex: number }>(
    SQL.GET_LINES_WITH_CONTENT_PATTERN_FOR_BOOKS(bookIds.length), [...bookIds, pattern])
}

export async function getLinesWithEitherContentPattern(
  bookId: number, p1: string, p2: string,
): Promise<{ id: number; lineIndex: number; content: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; lineIndex: number; content: string }[] }>(
      'getLinesWithEitherContentPattern', { bookId, p1, p2 })).rows
  return query<{ id: number; lineIndex: number; content: string }>(SQL.GET_LINES_WITH_EITHER_CONTENT_PATTERN, [bookId, p1, p2])
}

export async function getLineByBookAndLineIndex(
  bookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number; content: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; lineIndex: number; content: string }[] }>(
      'getLineByBookAndLineIndex', { bookId, lineIndex })).rows
  return query<{ id: number; lineIndex: number; content: string }>(SQL.GET_LINE_BY_BOOK_AND_LINE_INDEX, [bookId, lineIndex])
}
