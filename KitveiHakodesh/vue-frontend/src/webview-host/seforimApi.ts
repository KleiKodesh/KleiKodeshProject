// Semantic seforim-DB access layer.
//
// Dev routes each capability through the KitveiHakodesh service (serviceCall) so
// the browser build sends no seforim SQL. Hosted (C#) still runs the SQL from
// queries.sql.ts via the __webviewQuery bridge, unchanged, until the C# migration.
//
// PERSONAL BOOKS (hosted only). Otzaria's user_books.db is a second corpus whose
// ids are shifted by USER_BOOKS_BASE at the app boundary. Dev needs nothing here —
// the service routes server-side; on the hosted path every function below routes
// BEFORE sending SQL, per the same classification the service uses (keep in sync):
//   ROUTE       an inbound id picks the DB, outbound ids shift back
//   SPLIT-MERGE an inbound id LIST may span both corpora (search results do)
//   UNION       no inbound id — query both, library block first
// Connection-type ids are translated BY NAME (see userBooksDb.ts — the same id
// means different types per DB). User-side failures in UNION/SPLIT paths degrade
// to library-only results (mirrors the service's Run() semantics) — a personal-
// books hiccup must not take the whole catalog down with it.

import { query, categoryHasOrderIndex } from './seforimDb'
import { serviceCall } from './serviceClient'
import { SQL } from './queries.sql'
import {
  isUserBooksId, toLocalId, toUserAppId, groupByCorpus, shiftRowIds,
  userBooksPresent, queryUserBooks, userTocHasLineIndex,
  userLinkHasTargetLineIndex, userHasLinkAnchor,
  connTypeMaps, userTypeIdToApp, appTypeIdsToUserLocal, appTypeIdsToLibraryLocal,
} from './userBooksDb'
import type {
  BookRow, CategoryRow, BookInfo, TocEntry, AltTocStructure,
  LineRow, ReverseLineRow, CommentaryLinkRow, WordLinkAnchor,
} from './queries.types'

/** True when the C# seforim bridge is present (hosted). Dev falls to the service. */
export const isDbHosted = (): boolean => typeof window.__webviewQuery === 'function'

/**
 * FILE-BACKED personal-book content (hosted). Otzaria keeps a personal book's text
 * in the file at book.filePath — totalLines is 0 and the line table is empty — so
 * when the DB answers empty, content comes from the file via the host. Rows carry
 * id = 0 (file lines have no line ids; per-line features are guarded off for
 * id-less rows). Dev needs none of this: the service does the same fallback
 * server-side. limit 0 fetches just totalLines (virtual-scroll init).
 */
async function userBooksFileLines(
  localBookId: number, offset: number, limit: number,
): Promise<{ rows: LineRow[]; totalLines: number }> {
  try {
    const res = (await window.__webviewAction!('userBooksFileLines', {
      bookId: localBookId, offset, limit,
    })) as { rows?: LineRow[]; totalLines?: number }
    return { rows: res.rows ?? [], totalLines: res.totalLines ?? 0 }
  } catch (e) {
    console.warn('[seforimApi] personal-book file content failed:', e)
    return { rows: [], totalLines: 0 }
  }
}

const libTypesQuery = (sql: string) => query<{ id: number; name: string }>(sql)

/** Guarded user-side fetch for UNION/SPLIT-MERGE paths: degrade to nothing, loudly. */
async function tryUserBooks<T>(what: string, fn: () => Promise<T[]>): Promise<T[]> {
  try {
    return await fn()
  } catch (e) {
    console.warn(`[seforimApi] personal-books ${what} failed — serving library only:`, e)
    return []
  }
}

// ── Catalog ─────────────────────────────────────────────────────────────────

export async function getAllCategories(): Promise<CategoryRow[]> {
  if (!isDbHosted()) return (await serviceCall<{ rows: CategoryRow[] }>('getAllCategories')).rows
  const lib = await query<CategoryRow>(SQL.GET_ALL_CATEGORIES(categoryHasOrderIndex))
  if (!(await userBooksPresent())) return lib
  const user = await tryUserBooks('categories', async () => {
    // Otzaria's schema has category.orderIndex; fall back to the plain variant on
    // any schema that doesn't (probe-by-failure — this runs once per catalog load).
    let rows: CategoryRow[]
    try {
      rows = await queryUserBooks<CategoryRow>(SQL.GET_ALL_CATEGORIES(true))
    } catch {
      rows = await queryUserBooks<CategoryRow>(SQL.GET_ALL_CATEGORIES(false))
    }
    return shiftRowIds(rows, ['id', 'parentId'])
  })
  return user.length ? [...lib, ...user] : lib
}

export async function getAllBooks(): Promise<BookRow[]> {
  if (!isDbHosted()) return (await serviceCall<{ rows: BookRow[] }>('getAllBooks')).rows
  const lib = await query<BookRow>(SQL.GET_ALL_BOOKS)
  if (!(await userBooksPresent())) return lib
  const user = await tryUserBooks('books', async () =>
    shiftRowIds(await queryUserBooks<BookRow>(SQL.GET_ALL_BOOKS), ['id', 'categoryId']))
  return user.length ? [...lib, ...user] : lib
}

// ── Book + lines ──────────────────────────────────────────────────────────────

export async function getBookById(id: number): Promise<BookInfo | undefined> {
  if (!isDbHosted())
    return (await serviceCall<{ book: BookInfo | null }>('getBookById', { id })).book ?? undefined
  if (isUserBooksId(id)) {
    const book = (await queryUserBooks<BookInfo>(SQL.GET_BOOK_BY_ID, [toLocalId(id)]))[0]
    // File-backed books store totalLines = 0 — without the real count the virtual
    // scroller renders an empty book and never requests a page.
    if (book && book.totalLines === 0)
      return { ...book, totalLines: (await userBooksFileLines(toLocalId(id), 0, 0)).totalLines }
    return book
  }
  return (await query<BookInfo>(SQL.GET_BOOK_BY_ID, [id]))[0]
}

export async function getLinesPaged(bookId: number, limit: number, offset: number): Promise<LineRow[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: LineRow[] }>('getLinesPaged', { bookId, limit, offset })).rows
  if (isUserBooksId(bookId)) {
    const rows = shiftRowIds(
      await queryUserBooks<LineRow>(SQL.GET_LINES_PAGED, [toLocalId(bookId), limit, offset]), ['id'])
    if (rows.length > 0) return rows
    // Empty line table ⇒ the text lives in the file — serve it from there.
    return (await userBooksFileLines(toLocalId(bookId), offset, limit)).rows
  }
  return query<LineRow>(SQL.GET_LINES_PAGED, [bookId, limit, offset])
}

// ── TOC ─────────────────────────────────────────────────────────────────────

export async function getAllTocEntries(bookId: number): Promise<TocEntry[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: TocEntry[] }>('getAllTocEntries', { bookId })).rows
  if (isUserBooksId(bookId)) {
    // Otzaria-built DBs carry lineIndex ON tocEntry (their line table is empty);
    // the JOIN variant would return NULL for every entry and the TOC couldn't navigate.
    const sql = (await userTocHasLineIndex())
      ? SQL.GET_ALL_TOC_ENTRIES_TOC_LINEINDEX
      : SQL.GET_ALL_TOC_ENTRIES
    return shiftRowIds(await queryUserBooks<TocEntry>(sql, [toLocalId(bookId)]), ['id', 'parentId', 'lineId'])
  }
  return query<TocEntry>(SQL.GET_ALL_TOC_ENTRIES, [bookId])
}

export async function getAltTocStructures(bookId: number): Promise<AltTocStructure[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: AltTocStructure[] }>('getAltTocStructures', { bookId })).rows
  if (isUserBooksId(bookId))
    return shiftRowIds(
      await queryUserBooks<AltTocStructure>(SQL.GET_ALT_TOC_STRUCTURES, [toLocalId(bookId)]), ['id'])
  return query<AltTocStructure>(SQL.GET_ALT_TOC_STRUCTURES, [bookId])
}

export async function getAllAltTocEntries(structureId: number): Promise<TocEntry[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: TocEntry[] }>('getAllAltTocEntries', { structureId })).rows
  if (isUserBooksId(structureId))
    // alt_toc_entry has no lineIndex column in ANY known schema — always the JOIN variant.
    return shiftRowIds(
      await queryUserBooks<TocEntry>(SQL.GET_ALL_ALT_TOC_ENTRIES, [toLocalId(structureId)]),
      ['id', 'parentId', 'lineId'])
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
  if (isUserBooksId(bookId)) {
    const sql = (await userTocHasLineIndex())
      ? SQL.GET_TOC_ENTRY_BY_TEXT_PREFIX_TOC_LINEINDEX
      : SQL.GET_TOC_ENTRY_BY_TEXT_PREFIX
    return shiftRowIds(
      await queryUserBooks<{ id: number; lineIndex: number | null }>(sql, [toLocalId(bookId), pattern]), ['id'])
  }
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

  const { library, userBooks } = groupByCorpus(lineIds)
  const out: CommentaryLinkRow[] = []

  if (library.length > 0) {
    let libRows: CommentaryLinkRow[] | null = null
    if (await linkHasTargetLineIndex()) {
      try {
        libRows = await query<CommentaryLinkRow>(SQL.GET_COMMENTARY_LINKS_FOR_SOURCE_LINE_RANGE(library.length), library)
      } catch {
        // DB swapped under us (user picked a different seforim DB without a reload) —
        // remember and fall through to the portable JOIN.
        _linkHasTargetLineIndex = false
      }
    }
    libRows ??= await query<CommentaryLinkRow>(SQL.GET_COMMENTARY_LINKS_FOR_SOURCE_LINE_RANGE_JOIN(library.length), library)
    out.push(...libRows)
  }

  if (userBooks.length > 0) {
    out.push(...await tryUserBooks('commentary links', async () => {
      const sql = (await userLinkHasTargetLineIndex())
        ? SQL.GET_COMMENTARY_LINKS_FOR_SOURCE_LINE_RANGE(userBooks.length)
        : SQL.GET_COMMENTARY_LINKS_FOR_SOURCE_LINE_RANGE_JOIN(userBooks.length)
      const rows = await queryUserBooks<CommentaryLinkRow>(sql, userBooks)
      if (rows.length === 0) return rows
      const maps = await connTypeMaps(libTypesQuery)
      return shiftRowIds(rows, ['targetBookId', 'targetLineId']).map((r) => ({
        ...r,
        // typeof guard: a NULL connectionTypeId must pass through as-is, not
        // arithmetic into the shifted range (null + base is a number in JS).
        connectionTypeId: typeof r.connectionTypeId === 'number' && r.connectionTypeId !== 0
          ? userTypeIdToApp(r.connectionTypeId, maps)
          : r.connectionTypeId,
      }))
    }))
  }
  return out
}

// ── Word-level link anchors (link_anchor, SeforimLibrary schema v2+) ──────────

// Whether the open LIBRARY DB has the link_anchor table (schema v2+; current
// Zayit/Otzaria v1 DBs don't). null = unknown. Once false, LIBRARY lines get []
// without touching the DB again — personal-book lines are gated by their own probe.
let _hasLinkAnchors: boolean | null = null

/** Word-level link anchors for a batch of source lines. [] on schema-v1 DBs — always safe to call. */
export async function getWordLinkAnchorsForLines(lineIds: number[]): Promise<WordLinkAnchor[]> {
  if (lineIds.length === 0) return []
  if (!isDbHosted()) {
    if (_hasLinkAnchors === false) return []
    const res = await serviceCall<{ supported: boolean; rows: WordLinkAnchor[] }>(
      'getWordLinkAnchorsForLines', { lineIds },
    )
    if (!res.supported) _hasLinkAnchors = false
    return res.rows
  }

  const { library, userBooks } = groupByCorpus(lineIds)
  const out: WordLinkAnchor[] = []

  if (library.length > 0 && _hasLinkAnchors !== false) {
    if (_hasLinkAnchors == null) {
      try {
        const rows = await query<{ n: number }>(SQL.HAS_LINK_ANCHOR_TABLE)
        _hasLinkAnchors = (rows[0]?.n ?? 0) > 0
      } catch {
        return [] // DB not ready — leave unknown so the next call re-probes
      }
    }
    if (_hasLinkAnchors) {
      try {
        out.push(...await query<WordLinkAnchor>(SQL.GET_WORD_LINK_ANCHORS_FOR_LINES(library.length), library))
      } catch {
        // DB swapped under us (user picked a different seforim DB without a reload) — re-probe next call.
        _hasLinkAnchors = null
      }
    }
  }

  if (userBooks.length > 0) {
    out.push(...await tryUserBooks('word anchors', async () => {
      // The anchors SQL reads l.targetLineIndex directly — require BOTH probes
      // before running it against the personal-books DB (no Otzaria DB passes today).
      if (!(await userHasLinkAnchor()) || !(await userLinkHasTargetLineIndex())) return []
      const rows = await queryUserBooks<WordLinkAnchor>(SQL.GET_WORD_LINK_ANCHORS_FOR_LINES(userBooks.length), userBooks)
      return shiftRowIds(rows, ['lineId', 'targetBookId', 'targetLineId'])
    }))
  }
  return out
}

/** Content backfill for a batch of line ids. */
export async function getLineContents(lineIds: number[]): Promise<{ id: number; content: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; content: string }[] }>('getLineContents', { lineIds })).rows
  const { library, userBooks } = groupByCorpus(lineIds)
  const out: { id: number; content: string }[] = []
  if (library.length > 0)
    out.push(...await query<{ id: number; content: string }>(SQL.GET_LINE_CONTENTS(library.length), library))
  if (userBooks.length > 0)
    out.push(...await tryUserBooks('line contents', async () =>
      shiftRowIds(await queryUserBooks<{ id: number; content: string }>(SQL.GET_LINE_CONTENTS(userBooks.length), userBooks), ['id'])))
  return out
}

/** Library types verbatim (their ids ARE the app-visible type space), plus any
 * user-DB types whose NAME the library lacks, with shifted ids. */
export async function getAllConnectionTypes(): Promise<{ id: number; name: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; name: string }[] }>('getAllConnectionTypes')).rows
  const lib = await query<{ id: number; name: string }>(SQL.GET_ALL_CONNECTION_TYPES)
  if (!(await userBooksPresent())) return lib
  const extras = await tryUserBooks('connection types', async () => {
    const maps = await connTypeMaps(libTypesQuery)
    const rows: { id: number; name: string }[] = []
    for (const [id, name] of maps.user.idToName)
      if (name && !maps.lib.nameToId.has(name)) rows.push({ id: toUserAppId(id), name })
    return rows
  })
  return extras.length ? [...lib, ...extras] : lib
}

export async function getDefaultCommentators(bookId: number): Promise<{ commentatorBookId: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { commentatorBookId: number }[] }>('getDefaultCommentators', { bookId })).rows
  if (isUserBooksId(bookId))
    return shiftRowIds(
      await queryUserBooks<{ commentatorBookId: number }>(SQL.GET_DEFAULT_COMMENTATORS, [toLocalId(bookId)]),
      ['commentatorBookId'])
  return query<{ commentatorBookId: number }>(SQL.GET_DEFAULT_COMMENTATORS, [bookId])
}

// ── Reverse lookups (source & targum) + static filter books ────────────────────

/** Reverse source/targum lookup — pass commentary type ids for source, targum type ids for targum. */
export async function getReverseLineData(lineIds: number[], typeIds: number[]): Promise<ReverseLineRow[]> {
  if (lineIds.length === 0 || typeIds.length === 0) return []
  if (!isDbHosted())
    return (await serviceCall<{ rows: ReverseLineRow[] }>('getReverseLineData', { lineIds, typeIds })).rows

  const { library, userBooks } = groupByCorpus(lineIds)
  const out: ReverseLineRow[] = []

  const runReverse = async (
    ids: number[], types: number[],
    run: <T>(sql: string, params: unknown[]) => Promise<T[]>,
  ): Promise<ReverseLineRow[]> => {
    const isMulti = ids.length > 1
    const sql = isMulti
      ? SQL.GET_SOURCE_DATA_BY_REVERSE_COMMENTARY_LOOKUP_RANGE(types.length, ids.length)
      : SQL.GET_SOURCE_DATA_BY_REVERSE_COMMENTARY_LOOKUP(types.length)
    const params = isMulti ? [...ids, ...types] : [ids[0]!, ...types]
    return run<ReverseLineRow>(sql, params)
  }

  if (library.length > 0) {
    const libTypes = appTypeIdsToLibraryLocal(typeIds)
    if (libTypes.length > 0) out.push(...await runReverse(library, libTypes, (s, p) => query(s, p)))
  }
  if (userBooks.length > 0) {
    out.push(...await tryUserBooks('reverse lookup', async () => {
      const maps = await connTypeMaps(libTypesQuery)
      const uTypes = appTypeIdsToUserLocal(typeIds, maps)
      if (uTypes.length === 0) return []
      const rows = await runReverse(userBooks, uTypes, (s, p) => queryUserBooks(s, p))
      return shiftRowIds(rows, ['sourceBookId', 'sourceLineId'])
    }))
  }
  return out
}

/** Distinct source/targum books linking to the given base book via the given connection types. */
export async function getReverseBooks(bookId: number, typeIds: number[]): Promise<{ sourceBookId: number }[]> {
  if (typeIds.length === 0) return []
  if (!isDbHosted())
    return (await serviceCall<{ rows: { sourceBookId: number }[] }>('getReverseBooks', { bookId, typeIds })).rows
  if (isUserBooksId(bookId)) {
    return tryUserBooks('reverse books', async () => {
      const maps = await connTypeMaps(libTypesQuery)
      const uTypes = appTypeIdsToUserLocal(typeIds, maps)
      if (uTypes.length === 0) return []
      const rows = await queryUserBooks<{ sourceBookId: number }>(
        SQL.GET_SOURCE_BOOKS_BY_REVERSE_COMMENTARY_LOOKUP(uTypes.length), [toLocalId(bookId), ...uTypes])
      return shiftRowIds(rows, ['sourceBookId'])
    })
  }
  const libTypes = appTypeIdsToLibraryLocal(typeIds)
  if (libTypes.length === 0) return []
  return query<{ sourceBookId: number }>(
    SQL.GET_SOURCE_BOOKS_BY_REVERSE_COMMENTARY_LOOKUP(libTypes.length),
    [bookId, ...libTypes],
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
  if (isUserBooksId(sourceBookId)) {
    return tryUserBooks('static filter', async () => {
      const maps = await connTypeMaps(libTypesQuery)
      const uTypes = appTypeIdsToUserLocal(typeIds, maps)
      if (uTypes.length === 0) return []
      const rows = await queryUserBooks<{ targetBookId: number; connectionTypeId: number }>(
        SQL.GET_STATIC_COMMENTARY_FILTER_BOOKS_FOR_SOURCE_BOOK(uTypes.length), [toLocalId(sourceBookId), ...uTypes])
      return shiftRowIds(rows, ['targetBookId']).map((r) => ({
        ...r,
        connectionTypeId: typeof r.connectionTypeId === 'number' && r.connectionTypeId !== 0
          ? userTypeIdToApp(r.connectionTypeId, maps)
          : r.connectionTypeId,
      }))
    })
  }
  const libTypes = appTypeIdsToLibraryLocal(typeIds)
  if (libTypes.length === 0) return []
  return query<{ targetBookId: number; connectionTypeId: number }>(
    SQL.GET_STATIC_COMMENTARY_FILTER_BOOKS_FOR_SOURCE_BOOK(libTypes.length),
    [sourceBookId, ...libTypes],
  )
}

// ── Commentary navigation ─────────────────────────────────────────────────────
// Links never span database files: a main book and a commentary book from
// different corpora cannot be linked — those calls return empty by definition.

async function sectionWithCommentary(
  sql: string, mainBookId: number, commentaryBookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number }[]> {
  if (isUserBooksId(mainBookId) !== isUserBooksId(commentaryBookId)) return []
  if (isUserBooksId(mainBookId))
    return shiftRowIds(
      await queryUserBooks<{ id: number; lineIndex: number }>(
        sql, [toLocalId(mainBookId), toLocalId(commentaryBookId), lineIndex]), ['id'])
  return query<{ id: number; lineIndex: number }>(sql, [mainBookId, commentaryBookId, lineIndex])
}

export async function getNextSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; lineIndex: number }[] }>('getSectionWithCommentary',
      { mainBookId, commentaryBookId, lineIndex, direction: 'next' })).rows
  return sectionWithCommentary(SQL.GET_NEXT_SECTION_WITH_COMMENTARY, mainBookId, commentaryBookId, lineIndex)
}

export async function getPrevSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; lineIndex: number }[] }>('getSectionWithCommentary',
      { mainBookId, commentaryBookId, lineIndex, direction: 'prev' })).rows
  return sectionWithCommentary(SQL.GET_PREV_SECTION_WITH_COMMENTARY, mainBookId, commentaryBookId, lineIndex)
}

async function tocSectionWithCommentary(
  sql: string, mainBookId: number, commentaryBookId: number, rangePairs: number[],
): Promise<{ sectionStart: number }[]> {
  if (isUserBooksId(mainBookId) !== isUserBooksId(commentaryBookId)) return []
  // Range bounds are lineIndex POSITIONS, not ids — no translation.
  if (isUserBooksId(mainBookId))
    return queryUserBooks<{ sectionStart: number }>(
      sql, [...rangePairs, toLocalId(mainBookId), toLocalId(commentaryBookId)])
  return query<{ sectionStart: number }>(sql, [...rangePairs, mainBookId, commentaryBookId])
}

export async function getNextTocSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, rangePairs: number[],
): Promise<{ sectionStart: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { sectionStart: number }[] }>('getTocSectionWithCommentary',
      { mainBookId, commentaryBookId, rangePairs, direction: 'next' })).rows
  return tocSectionWithCommentary(
    SQL.GET_NEXT_TOC_SECTION_WITH_COMMENTARY(rangePairs.length / 2), mainBookId, commentaryBookId, rangePairs)
}

export async function getPrevTocSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, rangePairs: number[],
): Promise<{ sectionStart: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { sectionStart: number }[] }>('getTocSectionWithCommentary',
      { mainBookId, commentaryBookId, rangePairs, direction: 'prev' })).rows
  return tocSectionWithCommentary(
    SQL.GET_PREV_TOC_SECTION_WITH_COMMENTARY(rangePairs.length / 2), mainBookId, commentaryBookId, rangePairs)
}

export async function getLinkTargetForSourceLineAndBook(
  sourceLineId: number, targetBookId: number,
): Promise<{ targetLineId: number; lineIndex: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { targetLineId: number; lineIndex: number }[] }>('getLinkTargetForSourceLineAndBook',
      { sourceLineId, targetBookId })).rows
  if (isUserBooksId(sourceLineId) !== isUserBooksId(targetBookId)) return []
  if (isUserBooksId(sourceLineId))
    return shiftRowIds(
      await queryUserBooks<{ targetLineId: number; lineIndex: number }>(
        SQL.GET_LINK_TARGET_FOR_SOURCE_LINE_AND_BOOK, [toLocalId(sourceLineId), toLocalId(targetBookId)]),
      ['targetLineId'])
  return query<{ targetLineId: number; lineIndex: number }>(SQL.GET_LINK_TARGET_FOR_SOURCE_LINE_AND_BOOK, [sourceLineId, targetBookId])
}

// ── TOC paths & line→book/index helpers ───────────────────────────────────────

export async function getTocPathsForLines(lineIds: number[]): Promise<{ lineId: number; bookId: number; tocPath: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { lineId: number; bookId: number; tocPath: string }[] }>('getTocPathsForLines', { lineIds })).rows
  const { library, userBooks } = groupByCorpus(lineIds)
  const out: { lineId: number; bookId: number; tocPath: string }[] = []
  if (library.length > 0)
    out.push(...await query<{ lineId: number; bookId: number; tocPath: string }>(
      SQL.GET_TOC_PATHS_FOR_LINES(library.length), library))
  if (userBooks.length > 0)
    out.push(...await tryUserBooks('TOC paths', async () =>
      shiftRowIds(await queryUserBooks<{ lineId: number; bookId: number; tocPath: string }>(
        SQL.GET_TOC_PATHS_FOR_LINES(userBooks.length), userBooks), ['lineId', 'bookId'])))
  return out
}

/** triples = flat [groupKey, firstLineId, lastLineId, …]. groupKey is a caller token —
 * NOT an id, never translated. A range's endpoints must sit in one corpus; a mixed
 * range is a caller bug and is dropped rather than answered from the wrong DB. */
export async function getEnclosingTocPathForLineRanges(triples: number[]): Promise<{ groupKey: number; bookId: number; tocPath: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { groupKey: number; bookId: number; tocPath: string }[] }>('getEnclosingTocPathForLineRanges', { triples })).rows

  const library: number[] = []
  const userBooks: number[] = []
  for (let i = 0; i + 2 < triples.length; i += 3) {
    const g = triples[i]!, f = triples[i + 1]!, l = triples[i + 2]!
    if (isUserBooksId(f) !== isUserBooksId(l)) {
      console.warn(`[seforimApi] enclosing-TOC range for group ${g} spans corpora — dropped`)
      continue
    }
    const target = isUserBooksId(f) ? userBooks : library
    target.push(g, toLocalId(f), toLocalId(l))
  }

  const out: { groupKey: number; bookId: number; tocPath: string }[] = []
  if (library.length > 0)
    out.push(...await query<{ groupKey: number; bookId: number; tocPath: string }>(
      SQL.GET_ENCLOSING_TOC_PATH_FOR_LINE_RANGES(library.length / 3), library))
  if (userBooks.length > 0)
    out.push(...await tryUserBooks('enclosing TOC paths', async () =>
      shiftRowIds(await queryUserBooks<{ groupKey: number; bookId: number; tocPath: string }>(
        SQL.GET_ENCLOSING_TOC_PATH_FOR_LINE_RANGES(userBooks.length / 3), userBooks), ['bookId'])))
  return out
}

export async function getBookIdsForLines(lineIds: number[]): Promise<{ lineId: number; bookId: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { lineId: number; bookId: number }[] }>('getBookIdsForLines', { lineIds })).rows
  const { library, userBooks } = groupByCorpus(lineIds)
  const out: { lineId: number; bookId: number }[] = []
  if (library.length > 0)
    out.push(...await query<{ lineId: number; bookId: number }>(SQL.GET_BOOK_IDS_FOR_LINES(library.length), library))
  if (userBooks.length > 0)
    out.push(...await tryUserBooks('book ids for lines', async () =>
      shiftRowIds(await queryUserBooks<{ lineId: number; bookId: number }>(
        SQL.GET_BOOK_IDS_FOR_LINES(userBooks.length), userBooks), ['lineId', 'bookId'])))
  return out
}

export async function getLineIndexFromLineId(lineId: number): Promise<{ lineIndex: number; bookId: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { lineIndex: number; bookId: number }[] }>('getLineIndexFromLineId', { lineId })).rows
  if (isUserBooksId(lineId))
    return shiftRowIds(
      await queryUserBooks<{ lineIndex: number; bookId: number }>(SQL.GET_LINE_INDEX_FROM_LINE_ID, [toLocalId(lineId)]),
      ['bookId'])
  return query<{ lineIndex: number; bookId: number }>(SQL.GET_LINE_INDEX_FROM_LINE_ID, [lineId])
}

// ── Dictionary sources in the seforim DB (מצודת ציון, מלבי״ם, מנחם, ערוך) ──────
// Title lookups enumerate rather than route (ids flow OUT) — union both corpora,
// library first so existing consumers that take the first match keep their answer.

async function unionBookIds(sql: string, params: unknown[]): Promise<{ id: number }[]> {
  const lib = await query<{ id: number }>(sql, params)
  if (!(await userBooksPresent())) return lib
  const user = await tryUserBooks('title lookup', async () =>
    shiftRowIds(await queryUserBooks<{ id: number }>(sql, params), ['id']))
  return user.length ? [...lib, ...user] : lib
}

export async function getBookIdsByTitlePattern(pattern: string): Promise<{ id: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number }[] }>('getBookIdsByTitlePattern', { pattern })).rows
  return unionBookIds(SQL.GET_BOOK_IDS_BY_TITLE_PATTERN, [pattern])
}

export async function getBookIdByExactTitle(title: string): Promise<{ id: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number }[] }>('getBookIdByExactTitle', { title })).rows
  return unionBookIds(SQL.GET_BOOK_ID_BY_EXACT_TITLE, [title])
}

export async function getLinesWithContentPatternForBooks(
  bookIds: number[], pattern: string,
): Promise<{ content: string; title: string; bookId: number; lineId: number; lineIndex: number }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { content: string; title: string; bookId: number; lineId: number; lineIndex: number }[] }>(
      'getLinesWithContentPatternForBooks', { bookIds, pattern })).rows
  type Row = { content: string; title: string; bookId: number; lineId: number; lineIndex: number }
  const { library, userBooks } = groupByCorpus(bookIds)
  const out: Row[] = []
  if (library.length > 0)
    out.push(...await query<Row>(SQL.GET_LINES_WITH_CONTENT_PATTERN_FOR_BOOKS(library.length), [...library, pattern]))
  if (userBooks.length > 0 && out.length < 50)
    out.push(...await tryUserBooks('content pattern', async () =>
      shiftRowIds(await queryUserBooks<Row>(
        SQL.GET_LINES_WITH_CONTENT_PATTERN_FOR_BOOKS(userBooks.length), [...userBooks, pattern]), ['bookId', 'lineId'])))
  // The SQL carries LIMIT 50 per query; with two corpora that could yield up to 100 —
  // re-apply the cap on the merged list (library block first, so a full library
  // result keeps today's top-50 exactly).
  return out.length > 50 ? out.slice(0, 50) : out
}

export async function getLinesWithEitherContentPattern(
  bookId: number, p1: string, p2: string,
): Promise<{ id: number; lineIndex: number; content: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; lineIndex: number; content: string }[] }>(
      'getLinesWithEitherContentPattern', { bookId, p1, p2 })).rows
  if (isUserBooksId(bookId))
    return shiftRowIds(
      await queryUserBooks<{ id: number; lineIndex: number; content: string }>(
        SQL.GET_LINES_WITH_EITHER_CONTENT_PATTERN, [toLocalId(bookId), p1, p2]), ['id'])
  return query<{ id: number; lineIndex: number; content: string }>(SQL.GET_LINES_WITH_EITHER_CONTENT_PATTERN, [bookId, p1, p2])
}

export async function getLineByBookAndLineIndex(
  bookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number; content: string }[]> {
  if (!isDbHosted())
    return (await serviceCall<{ rows: { id: number; lineIndex: number; content: string }[] }>(
      'getLineByBookAndLineIndex', { bookId, lineIndex })).rows
  if (isUserBooksId(bookId)) {
    const rows = shiftRowIds(
      await queryUserBooks<{ id: number; lineIndex: number; content: string }>(
        SQL.GET_LINE_BY_BOOK_AND_LINE_INDEX, [toLocalId(bookId), lineIndex]), ['id'])
    if (rows.length > 0 || lineIndex < 0) return rows
    return (await userBooksFileLines(toLocalId(bookId), lineIndex, 1)).rows
  }
  return query<{ id: number; lineIndex: number; content: string }>(SQL.GET_LINE_BY_BOOK_AND_LINE_INDEX, [bookId, lineIndex])
}
