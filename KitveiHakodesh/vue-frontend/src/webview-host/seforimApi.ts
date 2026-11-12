// Semantic seforim-DB access layer.
//
// Every capability names an operation and the KitveiHakodesh service runs it — in dev over
// its loopback HTTP host, in the hosted app over its named pipe through the C# relay. Same
// client (serviceClient.ts), same op names, same shapes, so a capability cannot behave
// differently depending on which app is running it. Nothing here sends SQL text.
//
// The SQL in queries.sql.ts is what this replaced. Anything still importing it is
// a caller that has not been moved over yet.

import { ref } from 'vue'
import { serviceCall } from './serviceClient'
import type {
  BookRow, CategoryRow, BookInfo, BookMetadataRow, TocEntry, AltTocStructure,
  LineRow, BookVersionRow, HebrewBooksEdition, ReverseLineRefRow, BookLineRange, CommentaryLinkRow, CommentaryLinkGroups, WordLinkAnchor, WordLinkTargetRow, LineNote,
  RelatedBook, RelatedBooksList, CommentaryFilterGroup,
} from './queries.types'

/** Runs a named seforim operation on the service. One call per capability, no branch. */
function seforimOp<T>(op: string, args: object = {}): Promise<T> {
  return serviceCall<T>(op, args)
}

// ── Catalog ─────────────────────────────────────────────────────────────────

export async function getAllCategories(): Promise<CategoryRow[]> {
  return (await seforimOp<{ rows: CategoryRow[] }>('getAllCategories')).rows
}

export async function getAllBooks(): Promise<BookRow[]> {
  return (await seforimOp<{ rows: BookRow[] }>('getAllBooks')).rows
}

// ── Book + lines ──────────────────────────────────────────────────────────────

export async function getBookById(id: number): Promise<BookInfo | undefined> {
  return (await seforimOp<{ book: BookInfo | null }>('getBookById', { id })).book ?? undefined
}

/** Credits, provenance and licence for the book view's info popover. undefined = no such book. */
export async function getBookMetadata(bookId: number): Promise<BookMetadataRow | undefined> {
  return (await seforimOp<{ metadata: BookMetadataRow | null }>('getBookMetadata', { bookId })).metadata ?? undefined
}

/**
 * Up to `limit` lines from lineIndex `fromLineIndex` on (a lineIndex range, not a row
 * offset). `versionId` reads the page through an alternate version's overlay;
 * omit it (or pass 0) for the book's merged text.
 */
export async function getLinesPaged(
  bookId: number, limit: number, fromLineIndex: number, versionId = 0,
): Promise<LineRow[]> {
  return (await seforimOp<{ rows: LineRow[] }>('getLinesPaged', { bookId, limit, offset: fromLineIndex, versionId })).rows
}

/**
 * The in-book and commentary-panel search, whole, run by the service: every line inside the
 * (book, first lineIndex, last lineIndex) ranges that holds `query` as a substring — the
 * same normalization as stripHtmlForSearch/removeDiacriticsForSearch — and how many times,
 * as parallel arrays by book then reading order. The service narrows with the full-text
 * index when it can and confirms or scans the text natively; `versionId` reads through an
 * alternate version's overlay (0 = the merged text). Numbers only; nothing is kept.
 */
export async function linesSubstringSearch(
  query: string,
  ranges: { bookId: number; firstLineIndex: number; lastLineIndex: number }[],
  versionId = 0,
): Promise<{ bookIds: number[]; lineIndexes: number[]; counts: number[] }> {
  const rangeTriples = new Array<number>(ranges.length * 3)
  for (let i = 0; i < ranges.length; i++) {
    rangeTriples[i * 3] = ranges[i]!.bookId
    rangeTriples[i * 3 + 1] = ranges[i]!.firstLineIndex
    rangeTriples[i * 3 + 2] = ranges[i]!.lastLineIndex
  }
  return serviceCall('linesSubstringSearch', { query, rangeTriples, versionId })
}

/**
 * The ids of a book's lines in [fromIndex, toIndex), by lineIndex, with no text (0
 * where a lineIndex has no row). A TOC section's commentary and a shift-click range
 * need ids only; this is what lets them skip downloading the section's text.
 */
export async function getLineIds(bookId: number, fromIndex: number, toIndex: number): Promise<number[]> {
  return (await seforimOp<{ ids: number[] }>('getLineIds', { bookId, fromIndex, toIndex })).ids
}

/** A snippet's text (mirror of FtsSnippetText in SeforimDbFtsModels.cs): the matched line's
 *  own text, and for a short window the same book's neighbour lines before / after it. */
export interface FtsSnippetText {
  lineId: number
  text: string
  before: string
  after: string
}

/**
 * Snippet texts for rows the search page shows — the hits already carry their snippet
 * descriptors, so this is a plain text read: each line's text, and for `shortLineIds` also
 * their neighbour context, in one statement. A line with no text is absent.
 */
export async function ftsSnippetTexts(lineIds: number[], shortLineIds: number[]): Promise<FtsSnippetText[]> {
  return serviceCall('ftsSnippetTexts', { lineIds, shortLineIds })
}

/**
 * Every line's character count, by lineIndex, with no text — the reader's scrollbar
 * estimates row heights from them. null when the library has no `line.charCount`
 * column (older schemas), in which case the bar falls back to measured averages.
 */
export async function getLineCharCounts(bookId: number): Promise<Uint16Array | null> {
  // Packed little-endian uint16 (2 bytes a line). Copied out: the decoder's bytes may be an
  // unaligned view into the whole reply.
  const packed = (await seforimOp<{ packed: Uint8Array | null }>('getLineCharCounts', { bookId })).packed
  if (!packed || packed.byteLength < 2) return null
  const counts = new Uint16Array(packed.byteLength >> 1)
  const view = new DataView(packed.buffer, packed.byteOffset, packed.byteLength)
  for (let i = 0; i < counts.length; i++) counts[i] = view.getUint16(2 * i, true)
  return counts
}

// ── Versions (book_version / version_line, later schemas only) ────────────────

// No "does this DB have versions" flag here any more. C# probes for the version tables ONCE
// per database and caches the answer, so a library that predates them costs a cheap cached
// check rather than a query — the page latching it as well saved nothing and could only go
// stale when the user swapped databases.

/**
 * A book's alternate versions that carry text, best edition first.
 *
 * A version is an overlay over the same line ids, not a separate book — so switching
 * to one keeps the TOC, links, highlights and scroll position valid.
 *
 * [] means "none to offer", including on DBs that predate the feature; the toolbar
 * shows no version control at all in that case.
 */
export async function getBookVersions(bookId: number): Promise<BookVersionRow[]> {
  if (bookId <= 0) return []
  try {
    return (await seforimOp<{ rows: BookVersionRow[] }>('getBookVersions', { bookId })).rows
  } catch {
    // DB not ready, or swapped under us. [] means "no versions to offer", which is also what
    // a library without the version tables answers — the toolbar shows no version control.
    return []
  }
}

// ── HebrewBooks editions ─────────────────────────────────────────────────────

/**
 * The book's scanned editions on HebrewBooks, best first.
 *
 * [] means "none to offer" - the book has none, or the library carries no editions map
 * (Zayit, or a map file that is missing). The toolbar then shows no editions control.
 */
export async function getHebrewBooksEditions(bookId: number): Promise<HebrewBooksEdition[]> {
  if (bookId <= 0) return []
  try {
    return (await seforimOp<{ editions: HebrewBooksEdition[] }>('getHebrewBooksEditions', { bookId })).editions ?? []
  } catch {
    // Service not ready, or the DB swapped under us - same answer as "no editions".
    return []
  }
}

// ── TOC ─────────────────────────────────────────────────────────────────────

/**
 * A TOC as the service sends it: columns, entry i being the i-th value of each, and every
 * distinct text once (see TocEntriesResult in the service's SeforimModels.cs). Absent
 * values: parentIds/lineIds/altStructureIds 0, lineIndexes -1.
 */
interface TocColumns {
  ids: number[]
  parentIds: number[]
  levels: number[]
  lineIds: number[]
  lineIndexes: number[]
  hasChildren: Uint8Array | number[]
  texts: string[]
  textIndex: number[]
  altStructureIds?: number[] | null
}

/** Back to entry rows. Entries with the same text share one string. */
function tocEntriesFromColumns(c: TocColumns): TocEntry[] {
  const entries = new Array<TocEntry>(c.ids.length)
  const alt = c.altStructureIds
  for (let i = 0; i < entries.length; i++) {
    const entry: TocEntry = {
      id: c.ids[i]!,
      parentId: c.parentIds[i] || null,
      level: c.levels[i]!,
      lineId: c.lineIds[i] || null,
      hasChildren: c.hasChildren[i]!,
      text: c.texts[c.textIndex[i]!]!,
      lineIndex: c.lineIndexes[i]! < 0 ? null : c.lineIndexes[i]!,
    }
    if (alt) entry.altStructureId = alt[i] || null
    entries[i] = entry
  }
  return entries
}

export async function getAllTocEntries(bookId: number): Promise<TocEntry[]> {
  return tocEntriesFromColumns(await seforimOp<TocColumns>('getAllTocEntries', { bookId }))
}

export async function getAltTocStructures(bookId: number): Promise<AltTocStructure[]> {
  return (await seforimOp<{ rows: AltTocStructure[] }>('getAltTocStructures', { bookId })).rows
}

export async function getAllAltTocEntries(structureId: number): Promise<TocEntry[]> {
  return tocEntriesFromColumns(await seforimOp<TocColumns>('getAllAltTocEntries', { structureId }))
}

/** Daf-yomi navigation: first TOC entry whose text matches the LIKE `pattern`. */
export async function getTocEntryByTextPrefix(
  bookId: number,
  pattern: string,
): Promise<{ id: number; lineIndex: number | null }[]> {
  return (await seforimOp<{ rows: { id: number; lineIndex: number | null }[] }>(
      'getTocEntryByTextPrefix', { bookId, pattern },
    )).rows
}

// ── Commentary / links ────────────────────────────────────────────────────────

// Which spelling of the commentary-link query to run — whether `link` carries a
// targetLineIndex column, which Zayit has and Otzaria does not — is decided inside C# now,
// from the same schema probe the rest of the queries use. The page used to ask separately
// and pick between two SQL strings.

/** Links-only forward commentary for a range of source lines (content backfilled separately). */
export async function getCommentaryLinksForSourceLineRange(lineIds: number[]): Promise<CommentaryLinkRow[]> {
  return (await seforimOp<{ rows: CommentaryLinkRow[] }>('getCommentaryLinksForSourceLineRange', { lineIds })).rows
}

// ── Word-level link anchors (link_anchor, SeforimLibrary schema v2+) ──────────

// Whether the open DB has the link_anchor table (schema v2+; current Zayit/Otzaria v1
// DBs don't). null = unknown. Once false, callers get [] without touching the DB again.
// C# reports this as `supported` from its own schema probe, so the page no longer runs a
// table-existence query of its own to find out.
let _hasLinkAnchors: boolean | null = null

/**
 * The same support state, reactive, for UI that should not exist at all on schema-v1
 * DBs (the toolbar's marker-visibility dropdown). Settles from the probes the lazy
 * anchor loads already run — reading it never queries anything.
 */
export const wordLinkAnchorsSupported = ref<boolean | null>(null)

function setWordLinkSupport(value: boolean | null) {
  _hasLinkAnchors = value
  wordLinkAnchorsSupported.value = value
}

/** Word-level link anchors for a batch of source lines. [] on schema-v1 DBs — always safe to call. */
export async function getWordLinkAnchorsForLines(lineIds: number[]): Promise<WordLinkAnchor[]> {
  if (_hasLinkAnchors === false || lineIds.length === 0) return []
  try {
    // C# answers `supported` from its own schema probe, so the page no longer runs a
    // HAS_LINK_ANCHOR_TABLE query of its own to find out whether this database has the
    // link_anchor table at all.
    const res = await seforimOp<{ supported: boolean; rows: WordLinkAnchor[] }>(
      'getWordLinkAnchorsForLines', { lineIds },
    )
    setWordLinkSupport(res.supported)
    return res.rows
  } catch {
    // DB not ready, or swapped under us (the user picked a different seforim DB without a
    // reload). Leave support UNKNOWN so the next call re-probes; the reactive flag keeps its
    // last known value so support-gated UI does not blink out on a transient error.
    _hasLinkAnchors = null
    return []
  }
}

/**
 * Distinct word-link POINT-anchor targets (commentary book id + anchor label) for one
 * source book, ascending by book id. Feeds the per-book fallback-treatment ranking and
 * the toolbar's marker dropdown. [] means "legitimately none" (including schema-v1 DBs);
 * null means "transient failure — DB not ready or swapped" and callers must NOT cache it.
 */
export async function getWordLinkTargetsForBook(bookId: number): Promise<WordLinkTargetRow[] | null> {
  if (_hasLinkAnchors === false || bookId <= 0) return []
  try {
    const res = await seforimOp<{ supported: boolean; rows: WordLinkTargetRow[] }>(
      'getWordLinkAnchorTargetsForBook', { bookId },
    )
    setWordLinkSupport(res.supported)
    return res.rows
  } catch {
    // Transient — see getWordLinkAnchorsForLines. null (not []) because callers must not
    // cache "none" when the real answer is "could not ask".
    _hasLinkAnchors = null
    return null
  }
}

// line_note (Otzaria schema 7+) probe: null = unknown, false = this DB has no inline notes.
let _hasLineNotes: boolean | null = null

/** Inline notes (footnotes) for a batch of lines, one query. [] on DBs without line_note. */
export async function getLineNotesForLines(lineIds: number[]): Promise<LineNote[]> {
  if (_hasLineNotes === false || lineIds.length === 0) return []
  try {
    const res = await seforimOp<{ supported: boolean; rows: LineNote[] }>('getLineNotesForLines', { lineIds })
    _hasLineNotes = res.supported
    return res.rows
  } catch {
    // Transient (DB not ready or swapped) - re-probe next time; same contract as word links.
    _hasLineNotes = null
    throw new Error('getLineNotesForLines failed')
  }
}

/**
 * Word-link anchors and inline notes for two sets of lines in ONE call - the lines view asks
 * both for every window it mounts. Each half keeps its own contract: anchors are [] on any
 * failure, notes reject (so their lines are retried).
 */
export async function getLineDecorationsForLines(
  anchorLineIds: number[], noteLineIds: number[],
): Promise<{ anchors: WordLinkAnchor[]; notes: LineNote[] | Error }> {
  const askAnchors = _hasLinkAnchors !== false ? anchorLineIds : []
  const askNotes = _hasLineNotes !== false ? noteLineIds : []
  if (!askAnchors.length && !askNotes.length) return { anchors: [], notes: [] }
  try {
    const res = await seforimOp<{
      anchors: { supported: boolean; rows: WordLinkAnchor[] }
      notes: { supported: boolean; rows: LineNote[] }
    }>('getLineDecorationsForLines', { anchorLineIds: askAnchors, noteLineIds: askNotes })
    if (askAnchors.length) setWordLinkSupport(res.anchors.supported)
    if (askNotes.length) _hasLineNotes = res.notes.supported
    return { anchors: res.anchors.rows, notes: res.notes.rows }
  } catch {
    _hasLinkAnchors = null
    _hasLineNotes = null
    return { anchors: [], notes: new Error('getLineDecorationsForLines failed') }
  }
}

/** A word-link target's hover preview, resolved by the service: see WordLinkPreviewResult. */
export interface WordLinkPreview {
  bookId: number
  lineIndex: number
  /** '' = nothing to show. */
  content: string
  tocPath: string
  section: { tocText: string; focusIndex: number; lines: LineRow[] } | null
}

export async function getWordLinkPreview(lineId: number): Promise<WordLinkPreview> {
  return seforimOp<WordLinkPreview>('getWordLinkPreview', { lineId })
}

/** Content backfill for a batch of line ids. */
export async function getLineContents(lineIds: number[]): Promise<{ id: number; content: string }[]> {
  return (await seforimOp<{ rows: { id: number; content: string }[] }>('getLineContents', { lineIds })).rows
}

export async function getAllConnectionTypes(): Promise<{ id: number; name: string }[]> {
  return (await seforimOp<{ rows: { id: number; name: string }[] }>('getAllConnectionTypes')).rows
}

export async function getDefaultCommentators(bookId: number): Promise<{ commentatorBookId: number }[]> {
  return (await seforimOp<{ rows: { commentatorBookId: number }[] }>('getDefaultCommentators', { bookId })).rows
}

// ── Reverse lookups (source & targum) + static filter books ────────────────────

/** Reverse source/targum lookup — pass commentary type ids for source, targum type ids for targum. */
/**
 * The lines a reverse lookup finds, once each and without their text (the commentary
 * panel fetches the text of the rows it shows). `lines` is a list of ids or a book's
 * index range; a section as a range spares sending (and binding) thousands of ids.
 */
export async function getReverseLines(lines: number[] | BookLineRange, typeIds: number[]): Promise<ReverseLineRefRow[]> {
  if (typeIds.length === 0) return []
  if (Array.isArray(lines)) {
    if (lines.length === 0) return []
    return (await seforimOp<{ rows: ReverseLineRefRow[] }>('getReverseLines', { lineIds: lines, typeIds })).rows
  }
  return (await seforimOp<{ rows: ReverseLineRefRow[] }>('getReverseLines', { ...lines, typeIds })).rows
}

/** The commentary filter tree's groups for a book, in display order (Core builds them). */
export async function getCommentaryFilterGroups(bookId: number): Promise<CommentaryFilterGroup[]> {
  return (await seforimOp<{ groups: CommentaryFilterGroup[] }>('getCommentaryFilterGroups', { bookId })).groups
}

// ── Commentary navigation ─────────────────────────────────────────────────────

export async function getNextSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number }[]> {
  return (await seforimOp<{ rows: { id: number; lineIndex: number }[] }>('getSectionWithCommentary',
      { mainBookId, commentaryBookId, lineIndex, direction: 'next' })).rows
}

export async function getPrevSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number }[]> {
  return (await seforimOp<{ rows: { id: number; lineIndex: number }[] }>('getSectionWithCommentary',
      { mainBookId, commentaryBookId, lineIndex, direction: 'prev' })).rows
}

export async function getNextTocSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, rangePairs: number[],
): Promise<{ sectionStart: number }[]> {
  return (await seforimOp<{ rows: { sectionStart: number }[] }>('getTocSectionWithCommentary',
      { mainBookId, commentaryBookId, rangePairs, direction: 'next' })).rows
}

export async function getPrevTocSectionWithCommentary(
  mainBookId: number, commentaryBookId: number, rangePairs: number[],
): Promise<{ sectionStart: number }[]> {
  return (await seforimOp<{ rows: { sectionStart: number }[] }>('getTocSectionWithCommentary',
      { mainBookId, commentaryBookId, rangePairs, direction: 'prev' })).rows
}

// ── Related books (book view toolbar) ─────────────────────────────────────────

/** The related-books dropdown's list for a book, sections in display order. */
export async function getRelatedBooks(bookId: number): Promise<RelatedBooksList> {
  return seforimOp<RelatedBooksList>('getRelatedBooks', { bookId })
}

/**
 * The line a related book opens at for the reader's current top line, or null for the
 * book's start. `target` is the row exactly as getRelatedBooks returned it.
 */
export async function resolveRelatedTarget(
  fromBookId: number, topLineIndex: number, target: RelatedBook,
): Promise<number | null> {
  return (await seforimOp<{ lineIndex: number | null }>('resolveRelatedTarget',
      { fromBookId, topLineIndex, target })).lineIndex ?? null
}

// ── TOC paths & line→book/index helpers ───────────────────────────────────────

/**
 * TOC path per line id. The wire carries each line's TOC entry id and each distinct
 * entry's path once (lines of a section share an entry); this expands that back into
 * the per-line map every caller wants. Lines with no TOC entry are absent.
 */
export async function getTocPathsForLines(lineIds: number[]): Promise<Map<number, string>> {
  const { lines, paths } = await seforimOp<{
    lines: { lineId: number; bookId: number; tocEntryId: number }[]
    paths: { tocEntryId: number; path: string }[]
  }>('getTocPathsForLines', { lineIds })
  const pathByEntry = new Map(paths.map((p) => [p.tocEntryId, p.path]))
  const pathByLine = new Map<number, string>()
  for (const line of lines) {
    const path = pathByEntry.get(line.tocEntryId)
    if (path !== undefined && !pathByLine.has(line.lineId)) pathByLine.set(line.lineId, path)
  }
  return pathByLine
}

/** triples = flat [groupKey, firstLineId, lastLineId, …]. */
export async function getEnclosingTocPathForLineRanges(triples: number[]): Promise<{ groupKey: number; bookId: number; tocPath: string }[]> {
  return (await seforimOp<{ rows: { groupKey: number; bookId: number; tocPath: string }[] }>('getEnclosingTocPathForLineRanges', { triples })).rows
}

export async function getBookIdsForLines(lineIds: number[]): Promise<{ lineId: number; bookId: number }[]> {
  return (await seforimOp<{ rows: { lineId: number; bookId: number }[] }>('getBookIdsForLines', { lineIds })).rows
}

export async function getLineIndexFromLineId(lineId: number): Promise<{ lineIndex: number; bookId: number }[]> {
  return (await seforimOp<{ rows: { lineIndex: number; bookId: number }[] }>('getLineIndexFromLineId', { lineId })).rows
}

// ── Dictionary sources in the seforim DB (מצודת ציון, מלבי״ם, מנחם, ערוך) ──────

export async function getBookIdsByTitlePattern(pattern: string): Promise<{ id: number }[]> {
  return (await seforimOp<{ rows: { id: number }[] }>('getBookIdsByTitlePattern', { pattern })).rows
}

export async function getBookIdByExactTitle(title: string): Promise<{ id: number }[]> {
  return (await seforimOp<{ rows: { id: number }[] }>('getBookIdByExactTitle', { title })).rows
}

export async function getLineByBookAndLineIndex(
  bookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number; content: string }[]> {
  return (await seforimOp<{ rows: { id: number; lineIndex: number; content: string }[] }>(
      'getLineByBookAndLineIndex', { bookId, lineIndex })).rows
}

// ── Error-report provenance ──────────────────────────────────────────────────

/**
 * Which upstream corpus this book was built from, e.g. 'Sefaria', 'DictaToOtzaria'.
 *
 * undefined when the book or its source row is missing. Callers send the NAME and
 * never the id: `source.id` differs between the seforim databases in use here (id 3
 * is pninimToOtzaria in one and DictaToOtzaria in the other), so an id would identify
 * a different corpus depending on which library the reader happens to have open.
 */
export async function getBookSourceName(bookId: number): Promise<string | undefined> {
  if (bookId <= 0) return undefined
  return (await seforimOp<{ rows: { sourceName: string }[] }>('getBookSourceName', { bookId })).rows[0]?.sourceName
}

// No "does this DB carry schema_meta" flag here any more. The old one could only ever be set
// to true, because a missing table made the SQL throw; now C# answers an absent table with an
// empty list, so a page-side flag would latch FALSE and never re-ask — wrong the moment the
// user swaps to a library that does carry it. C# probes once per database and caches it.

/**
 * The library build identifier — `schema_meta.db_version`. undefined when the database
 * does not carry the table or the row.
 *
 * STAMPING ONLY. Schema detection goes by table structure and must never consult this;
 * see the WHY NOT READ schema_meta note in SeforimDbSchema.cs. The two databases in use
 * here report '1' and '31', which is precisely why a report needs to say which it was.
 */
export async function getLibraryDbVersion(): Promise<string | undefined> {
  try {
    return (await seforimOp<{ rows: { value: string }[] }>('getLibraryDbVersion')).rows[0]?.value
  } catch {
    // Not ready. undefined means "could not identify the build", which is also what a library
    // without schema_meta answers — nothing downstream distinguishes them.
    return undefined
  }
}

/**
 * One line's canonical reference and stored text, for checking that what the reader
 * saw still matches the library before a proposed correction is allowed to travel.
 */
export async function getLineRefAndContent(
  bookId: number, lineIndex: number,
): Promise<{ id: number; lineIndex: number; heRef: string | null; content: string } | undefined> {
  if (bookId <= 0 || lineIndex < 0) return undefined
  type Row = { id: number; lineIndex: number; heRef: string | null; content: string }
  return (await seforimOp<{ rows: Row[] }>('getLineRefAndContent', { bookId, lineIndex })).rows[0]
}

/**
 * Commentary links for a range of lines, de-duplicated and grouped per (book, connection
 * type) in Core: forward links, links pointing AT the lines, and the catalogue's commentary
 * verdict for `bookId` (see CommentaryLinkGroups). Numbers only: a whole-book range is a
 * few hundred KB where the per-row shape above was ~4 MB.
 */
export async function getCommentaryLinkGroupsForSourceLineRange(
  lineIds: number[],
  /** The book the lines belong to; 0 skips the catalogue's commentary verdict. */
  bookId = 0,
): Promise<CommentaryLinkGroups> {
  return seforimOp<CommentaryLinkGroups>('getCommentaryLinkGroupsForSourceLineRange', { lineIds, bookId })
}

/** getCommentaryLinkGroupsForSourceLineRange for a book's lines with lineIndex in [fromIndex, toIndex). */
export async function getCommentaryLinkGroupsForBookLineRange(range: BookLineRange): Promise<CommentaryLinkGroups> {
  return seforimOp<CommentaryLinkGroups>('getCommentaryLinkGroupsForBookLineRange', range)
}
