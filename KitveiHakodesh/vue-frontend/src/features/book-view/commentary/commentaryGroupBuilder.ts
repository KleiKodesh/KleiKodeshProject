/**
 * Builds CommentaryGroup arrays and fetches commentary data from the DB.
 * All data transformation and querying for the commentary panel lives here.
 * No Vue reactivity — purely async functions and pure transformations.
 */
import { getReverseLines } from '@/webview-host/seforimApi'
import type { BookLineRange, BookRow, CommentaryLinkGroups } from '@/webview-host/queries.types'
import type { CommentaryGroup, CommentaryBookEntry } from './useCommentary'
import {
  ensureConnectionTypeNamesLoaded,
  getConnectionTypeName,
  getPrimaryConnectionType,
  normalizeConnectionTypeName,
  getBaseTextReverseConnectionTypeIds,
  CONNECTION_TYPE_SECTION_LABELS,
} from './commentaryConnectionTypes'
import type { CommentaryConnectionType } from './commentaryConnectionTypes'

// ── Category helpers ──────────────────────────────────────────────────────────

const OTHER_CATEGORY = 'אחר'
const AL_SUFFIX = ' על'

const CATEGORY_ORDER = [
  'תנ"ך',
  'משנה',
  'תוספתא',
  'תלמוד',
  'מדרש',
  'גאונים',
  'ראשונים',
  'אחרונים',
  OTHER_CATEGORY,
]

function truncateAtAl(label: string): string {
  const index = label.indexOf(AL_SUFFIX)
  return index !== -1 ? label.slice(0, index) : label
}

/**
 * Commentary book titles read "<commentator> על <base book>". The linking word carries no
 * information once the book is shown as a commentary, so it is dropped at the point the
 * entries are built — every consumer (headers, nav, filter tree, copy, related-books) then
 * sees the same title. Only the first mid-title occurrence goes, so a base book whose own
 * name contains the word survives.
 */
export function commentaryDisplayTitle(title: string): string {
  return title.replace(/\s+על\s+/, ' ').trim() || title
}

function resolveBookTitle(book: BookRow | undefined, bookId: number): string {
  return book?.title ? commentaryDisplayTitle(book.title) : String(bookId)
}

export function resolveCategory(book: BookRow | undefined): string {
  if (!book) return OTHER_CATEGORY
  if (book.period && book.period !== OTHER_CATEGORY) return truncateAtAl(book.period)
  return truncateAtAl(book.rootCategory ?? OTHER_CATEGORY)
}

function categoryRank(category: string): number {
  const index = CATEGORY_ORDER.indexOf(category)
  return index === -1 ? CATEGORY_ORDER.length - 1 : index
}

function sortCategoryEntries(
  entries: [string, { bookId: number }[]][],
): [string, { bookId: number }[]][] {
  return entries.sort(([categoryA], [categoryB]) => categoryRank(categoryA) - categoryRank(categoryB))
}

// ── Group building ────────────────────────────────────────────────────────────

type ByBookConnectionKey = string // `${bookId}::${connectionTypeName}`
type ByBookConnectionMap = Map<
  ByBookConnectionKey,
  { bookId: number; lineIds: Set<number>; connectionType: string }
>

export function buildCommentaryGroupsFromEntries(entries: CommentaryBookEntry[]): CommentaryGroup[] {
  const byType = new Map<string, CommentaryBookEntry[]>()
  for (const entry of entries) {
    if (!byType.has(entry.primaryConnectionType)) byType.set(entry.primaryConnectionType, [])
    byType.get(entry.primaryConnectionType)!.push(entry)
  }

  const result: CommentaryGroup[] = []
  const byTreeOrder = (a: { treeOrder: number }, b: { treeOrder: number }) =>
    a.treeOrder - b.treeOrder

  const addFlat = (connectionType: CommentaryConnectionType) => {
    const sectionLabel = CONNECTION_TYPE_SECTION_LABELS[connectionType]
    for (const entry of (byType.get(connectionType) ?? []).sort(byTreeOrder)) {
      result.push({
        bookId: entry.bookId,
        bookTitle: entry.bookTitle,
        path: `${entry.bookTitle} · ${sectionLabel}`,
        connectionTypes: entry.connectionTypes,
        lines: entry.lines,
        category: entry.category,
        sectionLabel,
      })
    }
  }

  const addMergedByCategory = (connectionType: CommentaryConnectionType) => {
    const sectionLabel = CONNECTION_TYPE_SECTION_LABELS[connectionType]
    const items = byType.get(connectionType) ?? []
    if (!items.length) return

    const byCategory = new Map<string, CommentaryBookEntry[]>()
    for (const entry of items) {
      if (!byCategory.has(entry.category)) byCategory.set(entry.category, [])
      byCategory.get(entry.category)!.push(entry)
    }

    const sorted = sortCategoryEntries(
      [...byCategory.entries()].map(([cat, groups]) => [cat, groups.map((g) => ({ bookId: g.bookId }))]),
    )

    for (const [cat] of sorted) {
      for (const entry of byCategory.get(cat)!.sort(byTreeOrder)) {
        result.push({
          bookId: entry.bookId,
          bookTitle: entry.bookTitle,
          path: `${entry.bookTitle} · ${sectionLabel} · ${cat}`,
          connectionTypes: entry.connectionTypes,
          lines: entry.lines,
          category: cat,
          sectionLabel,
          subSectionLabel: cat,
        })
      }
    }
  }

  addFlat('SOURCE')
  addFlat('TARGUM')
  addMergedByCategory('COMMENTARY')
  addFlat('EIN_MISHPAT')
  addMergedByCategory('OTHER')
  addFlat('REFERENCE')

  return result
}

export async function buildCommentaryGroupsFromCombined(
  /**
   * The links, already DISTINCT and grouped per (book, connection type id) by Core
   * (getCommentaryLinkGroupsForSourceLineRange): ids and line indexes only. Text is
   * fetched per window later (lines start as '').
   */
  linkGroups: CommentaryLinkGroups,
  sourceEntries: CommentaryBookEntry[],
  allBooksMap: Map<number, BookRow>,
): Promise<CommentaryGroup[]> {
  // ensureConnectionTypeNamesLoaded is guaranteed by the caller (load() in useCommentary)
  // before this function is called — no need to await it again here.
  const byBookConnection: ByBookConnectionMap = new Map()
  const lineIndexById = new Map<number, number>()

  function addLinkGroup(
    bookId: number,
    canonicalConnectionTypeName: CommentaryConnectionType,
    lineIds: number[],
    lineIndexes: number[],
  ) {
    // Several raw type ids can normalize to one canonical name, and a book can be
    // linked from both ends, so a (book, name) entry may collect the lines of more
    // than one Core group.
    const key: ByBookConnectionKey = `${bookId}::${canonicalConnectionTypeName}`
    if (!byBookConnection.has(key))
      byBookConnection.set(key, { bookId, lineIds: new Set(), connectionType: canonicalConnectionTypeName })
    const entry = byBookConnection.get(key)!
    for (let i = 0; i < lineIds.length; i++) {
      const lineId = lineIds[i]!
      entry.lineIds.add(lineId)
      lineIndexById.set(lineId, lineIndexes[i] ?? 0)
    }
  }

  // A book the catalogue files as a commentary on this one is shown as one whatever the
  // link type says (Pilpula Charifta on the Rosh is type OTHER), in either direction —
  // the same pair can be stored either way round.
  const catalogCommentaryBookIds = new Set(linkGroups.catalogCommentaryBookIds)
  const canonicalTypeFor = (bookId: number, connectionTypeId: number): CommentaryConnectionType =>
    catalogCommentaryBookIds.has(bookId)
      ? 'COMMENTARY'
      : normalizeConnectionTypeName(getConnectionTypeName(connectionTypeId))

  for (const row of linkGroups.rows) {
    const canonicalConnectionTypeName = canonicalTypeFor(row.targetBookId, row.connectionTypeId)
    // SOURCE is the one type retrieved via a reverse query — its forward-direction
    // links are unreliable, so skip forward SOURCE rows. Every other type, TARGUM
    // included, is a normal forward link and is handled right here.
    if (canonicalConnectionTypeName === 'SOURCE') continue
    addLinkGroup(row.targetBookId, canonicalConnectionTypeName, row.lineIds, row.lineIndexes)
  }

  // Links stored the other way round (Sefaria's alphabetical order — see
  // CommentaryLinkGroups); apart from catalogue commentaries they land under OTHER.
  for (const row of linkGroups.incoming) {
    const canonicalConnectionTypeName = canonicalTypeFor(row.sourceBookId, row.connectionTypeId)
    if (canonicalConnectionTypeName === 'SOURCE') continue
    addLinkGroup(row.sourceBookId, canonicalConnectionTypeName, row.lineIds, row.lineIndexes)
  }

  // Collect all connection types per book so each entry's connectionTypes field
  // reflects the full set of ways this book is linked (used for display/filtering).
  const allConnectionTypesByBook = new Map<number, Set<string>>()
  for (const { bookId, connectionType } of byBookConnection.values()) {
    if (!allConnectionTypesByBook.has(bookId)) allConnectionTypesByBook.set(bookId, new Set())
    allConnectionTypesByBook.get(bookId)!.add(connectionType)
  }

  const forwardEntries: CommentaryBookEntry[] = [...byBookConnection.values()].map(
    ({ bookId, lineIds, connectionType }) => {
      const book = allBooksMap.get(bookId)
      const connectionTypes = [...(allConnectionTypesByBook.get(bookId) ?? new Set())]
      return {
        bookId,
        bookTitle: resolveBookTitle(book, bookId),
        connectionTypes,
        lines: [...lineIds]
          .map((id) => ({
            lineId: id,
            lineIndex: lineIndexById.get(id) ?? 0,
            content: '',
          }))
          .sort((a, b) => a.lineIndex - b.lineIndex),
        category: resolveCategory(book),
        treeOrder: book?.treeOrder ?? 999999,
        primaryConnectionType: connectionType,
      }
    },
  )

  return buildCommentaryGroupsFromEntries([...sourceEntries, ...forwardEntries])
}

// ── Reverse-lookup fetchers ───────────────────────────────────────────────────

/**
 * Fetches the source text entries for a set of line IDs using a reverse commentary lookup.
 * Instead of querying links where connectionType = SOURCE (unreliable), this queries links
 * where targetLineId is one of the given lines and connectionType is a COMMENTARY-type,
 * then returns the SOURCE book lines those commentary links point back to.
 * Ids and line indexes only: the text comes like every other row's, on demand.
 */
export async function fetchSourceEntriesViaReverseQuery(
  lines: number[] | BookLineRange,
  allBooksMap: Map<number, BookRow>,
): Promise<CommentaryBookEntry[]> {
  const reverseTypeIds = getBaseTextReverseConnectionTypeIds()
  if (!reverseTypeIds.length) return []

  const rows = await getReverseLines(lines, reverseTypeIds)

  if (!rows.length) return []

  const byBook = new Map<number, { lineIds: Set<number> }>()
  const lineIndexById = new Map<number, number>()

  for (const row of rows) {
    if (!byBook.has(row.sourceBookId)) byBook.set(row.sourceBookId, { lineIds: new Set() })
    byBook.get(row.sourceBookId)!.lineIds.add(row.sourceLineId)
    lineIndexById.set(row.sourceLineId, row.lineIndex)
  }

  return [...byBook.entries()].map(([bookId, { lineIds }]) => {
    const book = allBooksMap.get(bookId)
    return {
      bookId,
      bookTitle: resolveBookTitle(book, bookId),
      connectionTypes: ['SOURCE'],
      lines: [...lineIds]
        .map((id) => ({
          lineId: id,
          lineIndex: lineIndexById.get(id) ?? 0,
          content: '',
        }))
        .sort((a, b) => a.lineIndex - b.lineIndex),
      category: resolveCategory(book),
      treeOrder: book?.treeOrder ?? 999999,
      primaryConnectionType: 'SOURCE',
    }
  })
}
