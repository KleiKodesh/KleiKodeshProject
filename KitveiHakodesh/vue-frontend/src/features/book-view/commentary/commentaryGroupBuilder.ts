/**
 * Builds CommentaryGroup arrays and fetches commentary data from the DB.
 * All data transformation and querying for the commentary panel lives here.
 * No Vue reactivity — purely async functions and pure transformations.
 */
import { getReverseLineData, getReverseBooks, getStaticFilterBooks } from '@/webview-host/seforimApi'
import type { BookRow } from '../../book-catalog/bookCatalogTree'
import type { CommentaryGroup, CommentaryBookEntry } from './useCommentary'
import {
  ensureConnectionTypeNamesLoaded,
  getConnectionTypeName,
  getConnectionTypeId,
  getPrimaryConnectionType,
  normalizeConnectionTypeName,
  getCommentaryConnectionTypeIds,
  getTargumConnectionTypeIds,
  getDbNamesForCanonicalType,
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
  rows: Array<{
    targetBookId: number
    targetLineId: number
    connectionTypeId: number
    lineIndex: number
    // Absent when the caller used the links-only range query — content is
    // backfilled afterwards via GET_LINE_CONTENTS (lines start as '').
    content?: string
  }>,
  sourceEntries: CommentaryBookEntry[],
  targumEntries: CommentaryBookEntry[],
  allBooksMap: Map<number, BookRow>,
): Promise<CommentaryGroup[]> {
  // ensureConnectionTypeNamesLoaded is guaranteed by the caller (load() in useCommentary)
  // before this function is called — no need to await it again here.
  const byBookConnection: ByBookConnectionMap = new Map()
  const lineData = new Map<number, { lineIndex: number; content: string | undefined }>()

  for (const row of rows) {
    const rawConnectionTypeName = getConnectionTypeName(row.connectionTypeId)
    const canonicalConnectionTypeName = normalizeConnectionTypeName(rawConnectionTypeName)
    // SOURCE and TARGUM are retrieved via reverse queries — skip any forward rows for
    // these types so that unreliable forward-direction links in the DB are ignored.
    if (canonicalConnectionTypeName === 'SOURCE' || canonicalConnectionTypeName === 'TARGUM') continue
    const key: ByBookConnectionKey = `${row.targetBookId}::${canonicalConnectionTypeName}`
    if (!byBookConnection.has(key))
      byBookConnection.set(key, {
        bookId: row.targetBookId,
        lineIds: new Set(),
        connectionType: canonicalConnectionTypeName,
      })
    byBookConnection.get(key)!.lineIds.add(row.targetLineId)
    lineData.set(row.targetLineId, { lineIndex: row.lineIndex, content: row.content })
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
        bookTitle: book?.title ?? String(bookId),
        connectionTypes,
        lines: [...lineIds]
          .map((id) => ({
            lineId: id,
            lineIndex: lineData.get(id)?.lineIndex ?? 0,
            content: lineData.get(id)?.content ?? '',
          }))
          .sort((a, b) => a.lineIndex - b.lineIndex),
        category: resolveCategory(book),
        treeOrder: book?.treeOrder ?? 999999,
        primaryConnectionType: connectionType,
      }
    },
  )

  return buildCommentaryGroupsFromEntries([...sourceEntries, ...targumEntries, ...forwardEntries])
}

// ── Reverse-lookup fetchers ───────────────────────────────────────────────────

/**
 * Fetches the source text entries for a set of line IDs using a reverse commentary lookup.
 * Instead of querying links where connectionType = SOURCE (unreliable), this queries links
 * where targetLineId is one of the given lines and connectionType is a COMMENTARY-type,
 * then returns the SOURCE book lines those commentary links point back to.
 */
export async function fetchSourceEntriesViaReverseQuery(
  lineIds: number[],
  allBooksMap: Map<number, BookRow>,
): Promise<CommentaryBookEntry[]> {
  const commentaryTypeIds = getCommentaryConnectionTypeIds()
  if (!commentaryTypeIds.length) return []

  const rows = await getReverseLineData(lineIds, commentaryTypeIds)

  if (!rows.length) return []

  const byBook = new Map<number, { lineIds: Set<number> }>()
  const lineData = new Map<number, { lineIndex: number; content: string }>()

  for (const row of rows) {
    if (!byBook.has(row.sourceBookId)) byBook.set(row.sourceBookId, { lineIds: new Set() })
    byBook.get(row.sourceBookId)!.lineIds.add(row.sourceLineId)
    lineData.set(row.sourceLineId, { lineIndex: row.lineIndex, content: row.content })
  }

  return [...byBook.entries()].map(([bookId, { lineIds }]) => {
    const book = allBooksMap.get(bookId)
    return {
      bookId,
      bookTitle: book?.title ?? String(bookId),
      connectionTypes: ['SOURCE'],
      lines: [...lineIds]
        .map((id) => ({
          lineId: id,
          lineIndex: lineData.get(id)?.lineIndex ?? 0,
          content: lineData.get(id)?.content ?? '',
        }))
        .sort((a, b) => a.lineIndex - b.lineIndex),
      category: resolveCategory(book),
      treeOrder: book?.treeOrder ?? 999999,
      primaryConnectionType: 'SOURCE',
    }
  })
}

/**
 * Fetches the targum entries for a set of line IDs using a reverse TARGUM lookup.
 * Mirrors fetchSourceEntriesViaReverseQuery — finds lines in targum books that have a
 * TARGUM-type link pointing at the given lines and returns those lines.
 */
export async function fetchTargumEntriesViaReverseQuery(
  lineIds: number[],
  allBooksMap: Map<number, BookRow>,
): Promise<CommentaryBookEntry[]> {
  const targumTypeIds = getTargumConnectionTypeIds()
  if (!targumTypeIds.length) return []

  const rows = await getReverseLineData(lineIds, targumTypeIds)

  if (!rows.length) return []

  const byBook = new Map<number, { lineIds: Set<number> }>()
  const lineData = new Map<number, { lineIndex: number; content: string }>()

  for (const row of rows) {
    if (!byBook.has(row.sourceBookId)) byBook.set(row.sourceBookId, { lineIds: new Set() })
    byBook.get(row.sourceBookId)!.lineIds.add(row.sourceLineId)
    lineData.set(row.sourceLineId, { lineIndex: row.lineIndex, content: row.content })
  }

  return [...byBook.entries()].map(([bookId, { lineIds }]) => {
    const book = allBooksMap.get(bookId)
    return {
      bookId,
      bookTitle: book?.title ?? String(bookId),
      connectionTypes: ['TARGUM'],
      lines: [...lineIds]
        .map((id) => ({
          lineId: id,
          lineIndex: lineData.get(id)?.lineIndex ?? 0,
          content: lineData.get(id)?.content ?? '',
        }))
        .sort((a, b) => a.lineIndex - b.lineIndex),
      category: resolveCategory(book),
      treeOrder: book?.treeOrder ?? 999999,
      primaryConnectionType: 'TARGUM',
    }
  })
}

/**
 * Builds the full static filter group list for a given source book.
 * Used to populate the commentary filter tree before any line is selected.
 * Results are cached in the per-instance cache to avoid redundant DB queries.
 */
export async function buildStaticCommentaryFilterGroups(
  sourceBookId: number,
  allBooksMap: Map<number, BookRow>,
  instanceCache: Map<number, CommentaryGroup[]>,
): Promise<CommentaryGroup[]> {
  const cached = instanceCache.get(sourceBookId)
  if (cached) return cached
  await ensureConnectionTypeNamesLoaded()

  // Forward lookup: COMMENTARY and EIN_MISHPAT (SOURCE and TARGUM are unreliable in the
  // forward direction — both are discovered via reverse lookups instead).
  const forwardDbNames = [
    ...getDbNamesForCanonicalType('COMMENTARY'),
    ...getDbNamesForCanonicalType('EIN_MISHPAT'),
  ]
  const forwardConnectionTypeIds = forwardDbNames
    .map((name) => getConnectionTypeId(name))
    .filter((id): id is number => id != null)

  const commentaryTypeIds = getCommentaryConnectionTypeIds()
  const targumTypeIds = getTargumConnectionTypeIds()

  const [forwardRows, reverseSourceRows, reverseTargumRows] = await Promise.all([
    getStaticFilterBooks(sourceBookId, forwardConnectionTypeIds),
    getReverseBooks(sourceBookId, commentaryTypeIds),
    getReverseBooks(sourceBookId, targumTypeIds),
  ])

  if (!forwardRows.length && !reverseSourceRows.length && !reverseTargumRows.length) return []

  const byBook = new Map<number, Set<string>>()
  for (const row of forwardRows) {
    if (!byBook.has(row.targetBookId)) byBook.set(row.targetBookId, new Set())
    const rawName = getConnectionTypeName(row.connectionTypeId)
    const canonicalName = normalizeConnectionTypeName(rawName)
    byBook.get(row.targetBookId)!.add(canonicalName)
  }

  const commentaryEntries: CommentaryBookEntry[] = [...byBook.entries()].map(
    ([bookId, typesSet]) => {
      const book = allBooksMap.get(bookId)
      const connectionTypes = [...typesSet]
      return {
        bookId,
        bookTitle: book?.title ?? String(bookId),
        connectionTypes,
        lines: [],
        category: resolveCategory(book),
        treeOrder: book?.treeOrder ?? 999999,
        primaryConnectionType: getPrimaryConnectionType(connectionTypes),
      }
    },
  )

  const sourceEntries: CommentaryBookEntry[] = reverseSourceRows.map(
    ({ sourceBookId: bookId }) => {
      const book = allBooksMap.get(bookId)
      return {
        bookId,
        bookTitle: book?.title ?? String(bookId),
        connectionTypes: ['SOURCE'],
        lines: [],
        category: resolveCategory(book),
        treeOrder: book?.treeOrder ?? 999999,
        primaryConnectionType: 'SOURCE',
      }
    },
  )

  const targumEntries: CommentaryBookEntry[] = reverseTargumRows.map(
    ({ sourceBookId: bookId }) => {
      const book = allBooksMap.get(bookId)
      return {
        bookId,
        bookTitle: book?.title ?? String(bookId),
        connectionTypes: ['TARGUM'],
        lines: [],
        category: resolveCategory(book),
        treeOrder: book?.treeOrder ?? 999999,
        primaryConnectionType: 'TARGUM',
      }
    },
  )

  const result = buildCommentaryGroupsFromEntries([
    ...sourceEntries,
    ...targumEntries,
    ...commentaryEntries,
  ])
  instanceCache.set(sourceBookId, result)
  return result
}
