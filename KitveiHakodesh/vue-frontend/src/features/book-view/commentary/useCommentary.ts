import { computed, nextTick, ref, watch } from 'vue'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { BookRow } from '../../book-catalog/bookCatalogTree'

export interface CommentaryLine {
  lineId: number
  lineIndex: number
  content: string
}

export interface CommentaryGroup {
  bookId: number
  bookTitle: string
  path: string
  connectionTypes: string[]
  lines: CommentaryLine[]
  category?: string
  sectionLabel?: string
  subSectionLabel?: string
}

export interface CommentaryTreeNode {
  type: 'section' | 'book'
  label: string
  bookId?: number
  firstLineIndex?: number
  children: CommentaryTreeNode[]
}

type CommentaryConnectionType = 'SOURCE' | 'TARGUM' | 'COMMENTARY' | 'OTHER' | 'REFERENCE'
type StaticFilterConnectionType = 'SOURCE' | 'TARGUM' | 'COMMENTARY'

interface CommentaryBookEntry {
  bookId: number
  bookTitle: string
  connectionTypes: string[]
  lines: CommentaryLine[]
  category: string
  treeOrder: number
  primaryConnectionType: string
}

const CONNECTION_TYPE_PRIORITY: CommentaryConnectionType[] = [
  'SOURCE',
  'TARGUM',
  'COMMENTARY',
  'OTHER',
  'REFERENCE',
]

const STATIC_FILTER_CONNECTION_TYPE_LIST: StaticFilterConnectionType[] = [
  'SOURCE',
  'TARGUM',
  'COMMENTARY',
]

export const STATIC_FILTER_CONNECTION_TYPES = new Set<StaticFilterConnectionType>(
  STATIC_FILTER_CONNECTION_TYPE_LIST,
)

/**
 * Maps raw connection type names from the DB to the canonical CommentaryConnectionType
 * used for grouping and display. New connection types added to the DB are mapped here
 * so older DB versions that lack them continue to work — the mapping is only applied
 * when the name is actually returned by the DB.
 *
 * COMMENTARY equivalents: SUPER_COMMENTARY, PARSHANUT, MIDRASH
 * REFERENCE (ציונים) equivalents: MESORAH_HASHAS, EIN_MISHPAT, MISHNAH_IN_TALMUD
 * All other unknown names fall back to OTHER (קשרים).
 */
const DB_CONNECTION_TYPE_TO_CANONICAL: Record<string, CommentaryConnectionType> = {
  SOURCE: 'SOURCE',
  TARGUM: 'TARGUM',
  COMMENTARY: 'COMMENTARY',
  SUPER_COMMENTARY: 'COMMENTARY',
  PARSHANUT: 'COMMENTARY',
  MIDRASH: 'COMMENTARY',
  REFERENCE: 'REFERENCE',
  MESORAH_HASHAS: 'REFERENCE',
  EIN_MISHPAT: 'REFERENCE',
  MISHNAH_IN_TALMUD: 'REFERENCE',
  OTHER: 'OTHER',
}

function normalizeConnectionTypeName(dbName: string): CommentaryConnectionType {
  return DB_CONNECTION_TYPE_TO_CANONICAL[dbName] ?? 'OTHER'
}

const CONNECTION_TYPE_SECTION_LABELS: Record<CommentaryConnectionType, string> = {
  SOURCE: '\u05DE\u05E7\u05D5\u05E8',
  TARGUM: '\u05EA\u05E8\u05D2\u05D5\u05DE\u05D9\u05DD',
  COMMENTARY: '\u05DE\u05E4\u05E8\u05E9\u05D9\u05DD',
  OTHER: '\u05E7\u05E9\u05E8\u05D9\u05DD',
  REFERENCE: '\u05E6\u05D9\u05D5\u05E0\u05D9\u05DD',
}

// Reverse mapping: Hebrew label → connection type
export const SECTION_LABEL_TO_CONNECTION_TYPE: Record<string, CommentaryConnectionType> = {
  '\u05DE\u05E7\u05D5\u05E8': 'SOURCE',
  '\u05EA\u05E8\u05D2\u05D5\u05DE\u05D9\u05DD': 'TARGUM',
  '\u05DE\u05E4\u05E8\u05E9\u05D9\u05DD': 'COMMENTARY',
  '\u05E7\u05E9\u05E8\u05D9\u05DD': 'OTHER',
  '\u05E6\u05D9\u05D5\u05E0\u05D9\u05DD': 'REFERENCE',
}

const OTHER_CATEGORY = '\u05D0\u05D7\u05E8'
const AL_SUFFIX = ' \u05E2\u05DC'

const CATEGORY_ORDER = [
  '\u05EA\u05E0"\u05DA',
  '\u05DE\u05E9\u05E0\u05D4',
  '\u05EA\u05D5\u05E1\u05E4\u05EA\u05D0',
  '\u05EA\u05DC\u05DE\u05D5\u05D3',
  '\u05DE\u05D3\u05E8\u05E9',
  '\u05D2\u05D0\u05D5\u05E0\u05D9\u05DD',
  '\u05E8\u05D0\u05E9\u05D5\u05E0\u05D9\u05DD',
  '\u05D0\u05D7\u05E8\u05D5\u05E0\u05D9\u05DD',
  OTHER_CATEGORY,
]

export function buildCommentaryTree(groups: CommentaryGroup[]): CommentaryTreeNode[] {
  const root: CommentaryTreeNode[] = []
  let currentSection: CommentaryTreeNode | null = null
  let currentSubSection: CommentaryTreeNode | null = null

  for (const group of groups) {
    const sectionLabel = group.sectionLabel ?? group.bookTitle
    const subLabel = group.subSectionLabel ?? null

    if (!currentSection || currentSection.label !== sectionLabel) {
      currentSection = { type: 'section', label: sectionLabel, children: [] }
      currentSubSection = null
      root.push(currentSection)
    }

    // When the subsection label duplicates the section label, promote the books
    // directly into the section rather than nesting a redundant child node.
    const effectiveSubLabel = subLabel && subLabel !== sectionLabel ? subLabel : null

    if (effectiveSubLabel) {
      if (!currentSubSection || currentSubSection.label !== effectiveSubLabel) {
        currentSubSection = { type: 'section', label: effectiveSubLabel, children: [] }
        currentSection.children.push(currentSubSection)
      }
      currentSubSection.children.push({
        type: 'book',
        label: group.bookTitle,
        bookId: group.bookId,
        firstLineIndex: group.lines[0]?.lineIndex,
        children: [],
      })
    } else {
      currentSubSection = null
      currentSection.children.push({
        type: 'book',
        label: group.bookTitle,
        bookId: group.bookId,
        firstLineIndex: group.lines[0]?.lineIndex,
        children: [],
      })
    }
  }

  return root
}

function truncateAtAl(label: string): string {
  const idx = label.indexOf(AL_SUFFIX)
  return idx !== -1 ? label.slice(0, idx) : label
}

function resolveCategory(book: BookRow | undefined): string {
  if (!book) return OTHER_CATEGORY
  if (book.period && book.period !== OTHER_CATEGORY) return truncateAtAl(book.period)
  return truncateAtAl(book.rootCategory ?? OTHER_CATEGORY)
}

function categoryRank(cat: string): number {
  const idx = CATEGORY_ORDER.indexOf(cat)
  return idx === -1 ? CATEGORY_ORDER.length - 1 : idx
}

function sortCategoryEntries(
  entries: [string, { bookId: number }[]][],
): [string, { bookId: number }[]][] {
  return entries.sort(([catA], [catB]) => categoryRank(catA) - categoryRank(catB))
}

let connectionTypeNamesById: Map<number, string> | null = null
let connectionTypeIdsByName: Map<string, number> | null = null

async function ensureConnectionTypeNamesLoaded() {
  if (connectionTypeNamesById && connectionTypeIdsByName) return
  const rows = await query<{ id: number; name: string }>(SQL.GET_ALL_CONNECTION_TYPES)
  connectionTypeNamesById = new Map(rows.map((row) => [row.id, row.name]))
  connectionTypeIdsByName = new Map(rows.map((row) => [row.name, row.id]))
}

function getConnectionTypeName(connectionTypeId: number): string {
  return connectionTypeNamesById?.get(connectionTypeId) ?? String(connectionTypeId)
}

function getConnectionTypeId(connectionTypeName: string): number | null {
  return connectionTypeIdsByName?.get(connectionTypeName) ?? null
}

function getPrimaryConnectionType(connectionTypes: string[]): string {
  for (const type of CONNECTION_TYPE_PRIORITY) {
    if (connectionTypes.includes(type)) return type
  }
  return connectionTypes[0] ?? 'OTHER'
}

function isStaticFilterConnectionType(type: string): type is StaticFilterConnectionType {
  return STATIC_FILTER_CONNECTION_TYPES.has(type as StaticFilterConnectionType)
}

/**
 * Returns all canonical connection type names that map to the given canonical type.
 * Used when building SQL IN clauses that must match DB rows for a logical group
 * (e.g. all DB names that are treated as COMMENTARY).
 */
function getDbNamesForCanonicalType(canonical: CommentaryConnectionType): string[] {
  return Object.entries(DB_CONNECTION_TYPE_TO_CANONICAL)
    .filter(([, mapped]) => mapped === canonical)
    .map(([dbName]) => dbName)
}

/**
 * Returns the IDs of all connection types in the DB that canonicalize to COMMENTARY.
 * These are the types used for the reverse source lookup — the source text is found
 * by reversing a commentary-type link rather than trusting the unreliable SOURCE type.
 * Returns an empty array when the connection type table hasn't been loaded yet (caller
 * must call ensureConnectionTypeNamesLoaded first).
 */
function getCommentaryConnectionTypeIds(): number[] {
  return getDbNamesForCanonicalType('COMMENTARY')
    .map((name) => getConnectionTypeId(name))
    .filter((id): id is number => id != null)
}

/**
 * Returns the IDs of all connection types in the DB that canonicalize to TARGUM.
 * Used for the reverse targum lookup — the targum source is found by reversing a
 * TARGUM-type link rather than trusting the forward TARGUM connection type.
 * Returns an empty array when the connection type table hasn't been loaded yet (caller
 * must call ensureConnectionTypeNamesLoaded first).
 */
function getTargumConnectionTypeIds(): number[] {
  return getDbNamesForCanonicalType('TARGUM')
    .map((name) => getConnectionTypeId(name))
    .filter((id): id is number => id != null)
}

type ByBookConnectionKey = string // `${bookId}::${connectionTypeName}`
type ByBookConnectionMap = Map<ByBookConnectionKey, { bookId: number; lineIds: Set<number>; connectionType: string }>

function buildCommentaryGroupsFromEntries(entries: CommentaryBookEntry[]): CommentaryGroup[] {
  const byType = new Map<string, CommentaryBookEntry[]>()
  for (const entry of entries) {
    if (!byType.has(entry.primaryConnectionType)) byType.set(entry.primaryConnectionType, [])
    byType.get(entry.primaryConnectionType)!.push(entry)
  }

  const result: CommentaryGroup[] = []
  const byTreeOrder = (a: { treeOrder: number }, b: { treeOrder: number }) =>
    a.treeOrder - b.treeOrder

  const addFlat = (ct: CommentaryConnectionType) => {
    const sectionLabel = CONNECTION_TYPE_SECTION_LABELS[ct]
    for (const entry of (byType.get(ct) ?? []).sort(byTreeOrder)) {
      result.push({
        bookId: entry.bookId,
        bookTitle: entry.bookTitle,
        path: `${entry.bookTitle} \u00B7 ${sectionLabel}`,
        connectionTypes: entry.connectionTypes,
        lines: entry.lines,
        category: entry.category,
        sectionLabel,
      })
    }
  }

  const addMergedByCategory = (ct: CommentaryConnectionType) => {
    const sectionLabel = CONNECTION_TYPE_SECTION_LABELS[ct]
    const items = byType.get(ct) ?? []
    if (!items.length) return

    const byCat = new Map<string, CommentaryBookEntry[]>()
    for (const entry of items) {
      if (!byCat.has(entry.category)) byCat.set(entry.category, [])
      byCat.get(entry.category)!.push(entry)
    }

    const sorted = sortCategoryEntries(
      [...byCat.entries()].map(([cat, groups]) => [cat, groups.map((g) => ({ bookId: g.bookId }))]),
    )

    for (const [cat] of sorted) {
      for (const entry of byCat.get(cat)!.sort(byTreeOrder)) {
        result.push({
          bookId: entry.bookId,
          bookTitle: entry.bookTitle,
          path: `${entry.bookTitle} \u00B7 ${sectionLabel} \u00B7 ${cat}`,
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
  addMergedByCategory('OTHER')
  addFlat('REFERENCE')

  return result
}

async function buildCommentaryGroupsFromCombined(
  rows: Array<{
    targetBookId: number
    targetLineId: number
    connectionTypeId: number
    lineIndex: number
    content: string
  }>,
  sourceEntries: CommentaryBookEntry[],
  targumEntries: CommentaryBookEntry[],
  allBooksMap: Map<number, BookRow>,
): Promise<CommentaryGroup[]> {
  await ensureConnectionTypeNamesLoaded()
  const byBookConnection: ByBookConnectionMap = new Map()
  const lineData = new Map<number, { lineIndex: number; content: string }>()

  for (const row of rows) {
    const rawConnectionTypeName = getConnectionTypeName(row.connectionTypeId)
    const canonicalConnectionTypeName = normalizeConnectionTypeName(rawConnectionTypeName)
    // SOURCE and TARGUM are retrieved via reverse queries — skip any forward rows for
    // these types so that unreliable forward-direction links in the DB are ignored.
    if (canonicalConnectionTypeName === 'SOURCE' || canonicalConnectionTypeName === 'TARGUM') continue
    const key: ByBookConnectionKey = `${row.targetBookId}::${canonicalConnectionTypeName}`
    if (!byBookConnection.has(key))
      byBookConnection.set(key, { bookId: row.targetBookId, lineIds: new Set(), connectionType: canonicalConnectionTypeName })
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

  const forwardEntries: CommentaryBookEntry[] = [...byBookConnection.values()].map(({ bookId, lineIds, connectionType }) => {
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
  })

  return buildCommentaryGroupsFromEntries([...sourceEntries, ...targumEntries, ...forwardEntries])
}

/**
 * Fetches the source text entries for a set of line IDs using a reverse commentary lookup.
 * Instead of querying links where connectionType = SOURCE (unreliable), this queries links
 * where targetLineId is one of the given lines and connectionType is a COMMENTARY-type,
 * then returns the SOURCE book lines those commentary links point back to.
 *
 * Returns CommentaryBookEntry objects with primaryConnectionType = 'SOURCE', ready to be
 * merged with the forward commentary entries before building the display groups.
 */
async function fetchSourceEntriesViaReverseQuery(
  lineIds: number[],
  allBooksMap: Map<number, BookRow>,
): Promise<CommentaryBookEntry[]> {
  const commentaryTypeIds = getCommentaryConnectionTypeIds()
  if (!commentaryTypeIds.length) return []

  const isMulti = lineIds.length > 1
  const sql = isMulti
    ? SQL.GET_SOURCE_DATA_BY_REVERSE_COMMENTARY_LOOKUP_RANGE(commentaryTypeIds.length, lineIds.length)
    : SQL.GET_SOURCE_DATA_BY_REVERSE_COMMENTARY_LOOKUP(commentaryTypeIds.length)
  const params = isMulti
    ? [...lineIds, ...commentaryTypeIds]
    : [lineIds[0]!, ...commentaryTypeIds]

  const rows = await query<{
    sourceBookId: number
    sourceLineId: number
    lineIndex: number
    content: string
  }>(sql, params)

  if (!rows.length) return []

  // Group lines by source book — each book becomes one SOURCE entry.
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
 * Mirrors fetchSourceEntriesViaReverseQuery — instead of using the forward TARGUM
 * connection type (which may be unreliable), this finds lines in targum books that
 * have a TARGUM-type link pointing at the given lines and returns those lines.
 *
 * Returns CommentaryBookEntry objects with primaryConnectionType = 'TARGUM', ready to be
 * merged with the source and forward commentary entries before building the display groups.
 */
async function fetchTargumEntriesViaReverseQuery(
  lineIds: number[],
  allBooksMap: Map<number, BookRow>,
): Promise<CommentaryBookEntry[]> {
  const targumTypeIds = getTargumConnectionTypeIds()
  if (!targumTypeIds.length) return []

  const isMulti = lineIds.length > 1
  const sql = isMulti
    ? SQL.GET_TARGUM_DATA_BY_REVERSE_TARGUM_LOOKUP_RANGE(targumTypeIds.length, lineIds.length)
    : SQL.GET_TARGUM_DATA_BY_REVERSE_TARGUM_LOOKUP(targumTypeIds.length)
  const params = isMulti
    ? [...lineIds, ...targumTypeIds]
    : [lineIds[0]!, ...targumTypeIds]

  const rows = await query<{
    sourceBookId: number
    sourceLineId: number
    lineIndex: number
    content: string
  }>(sql, params)

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

async function buildStaticCommentaryFilterGroups(
  sourceBookId: number,
  allBooksMap: Map<number, BookRow>,
  instanceCache: Map<number, CommentaryGroup[]>,
): Promise<CommentaryGroup[]> {
  const cached = instanceCache.get(sourceBookId)
  if (cached) return cached
  await ensureConnectionTypeNamesLoaded()

  // Forward lookup: COMMENTARY only (SOURCE and TARGUM are unreliable in the forward
  // direction — both are discovered via reverse lookups instead).
  const forwardCanonicalTypes: StaticFilterConnectionType[] = ['COMMENTARY']
  const forwardDbNames = forwardCanonicalTypes.flatMap((canonical) =>
    getDbNamesForCanonicalType(canonical),
  )
  const forwardConnectionTypeIds = forwardDbNames
    .map((name) => getConnectionTypeId(name))
    .filter((id): id is number => id != null)

  // Reverse lookups: find source books and targum books that link to this base book.
  const commentaryTypeIds = getCommentaryConnectionTypeIds()
  const targumTypeIds = getTargumConnectionTypeIds()

  // Run all three queries in parallel — none depends on another's result.
  const [forwardRows, reverseSourceRows, reverseTargumRows] = await Promise.all([
    forwardConnectionTypeIds.length
      ? query<{ targetBookId: number; connectionTypeId: number }>(
          SQL.GET_STATIC_COMMENTARY_FILTER_BOOKS_FOR_SOURCE_BOOK(forwardConnectionTypeIds.length),
          [sourceBookId, ...forwardConnectionTypeIds],
        )
      : Promise.resolve([]),
    commentaryTypeIds.length
      ? query<{ sourceBookId: number }>(
          SQL.GET_SOURCE_BOOKS_BY_REVERSE_COMMENTARY_LOOKUP(commentaryTypeIds.length),
          [sourceBookId, ...commentaryTypeIds],
        )
      : Promise.resolve([]),
    targumTypeIds.length
      ? query<{ sourceBookId: number }>(
          SQL.GET_TARGUM_BOOKS_BY_REVERSE_TARGUM_LOOKUP(targumTypeIds.length),
          [sourceBookId, ...targumTypeIds],
        )
      : Promise.resolve([]),
  ])

  if (!forwardRows.length && !reverseSourceRows.length && !reverseTargumRows.length) return []

  // Build entries from the forward rows (COMMENTARY only).
  const byBook = new Map<number, Set<string>>()
  for (const row of forwardRows) {
    if (!byBook.has(row.targetBookId)) byBook.set(row.targetBookId, new Set())
    const rawName = getConnectionTypeName(row.connectionTypeId)
    const canonicalName = normalizeConnectionTypeName(rawName)
    byBook.get(row.targetBookId)!.add(canonicalName)
  }

  const commentaryEntries: CommentaryBookEntry[] = [...byBook.entries()].map(([bookId, typesSet]) => {
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
  })

  // SOURCE entries from the reverse commentary lookup.
  const sourceEntries: CommentaryBookEntry[] = reverseSourceRows.map(({ sourceBookId: bookId }) => {
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
  })

  // TARGUM entries from the reverse targum lookup.
  const targumEntries: CommentaryBookEntry[] = reverseTargumRows.map(({ sourceBookId: bookId }) => {
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
  })

  const result = buildCommentaryGroupsFromEntries([...sourceEntries, ...targumEntries, ...commentaryEntries])
  instanceCache.set(sourceBookId, result)
  return result
}

const NO_TEXT_PLACEHOLDER_CONTENT = 'אין טקסט לשורה זו'

export function useCommentary(
  selectedLineId: () => number | null,
  selectedLineIds: () => number[] | null = () => null,
  sourceBookId: () => number | undefined = () => undefined,
  filterPanelVisible: () => boolean = () => false,
  pinnedBookId: () => number | null = () => null,
) {
  const groups = ref<CommentaryGroup[]>([])
  const staticFilterGroups = ref<CommentaryGroup[]>([])
  const staticFilterGroupsLoaded = ref(false)
  const loading = ref(false)
  const booksDataStore = useBooksDataStore()
  let staticFilterLoadToken = 0
  // Per-instance cache — scoped to this tab's book, cleared when the composable is destroyed
  const staticFilterCache = new Map<number, CommentaryGroup[]>()

  const filterGroups = computed(() => {
    if (!staticFilterGroupsLoaded.value) return groups.value
    return [
      ...staticFilterGroups.value,
      ...groups.value.filter((group) => {
        const primaryType = getPrimaryConnectionType(group.connectionTypes)
        return !isStaticFilterConnectionType(primaryType)
      }),
    ]
  })

  let loadedForLineId: number | null = null
  let loadUsedSectionRange = false

  async function load(lineId: number) {
    loadedForLineId = lineId
    const multiIds = selectedLineIds()
    const isMulti = multiIds != null && multiIds.length > 0
    loadUsedSectionRange = isMulti
    groups.value = []
    loading.value = true
    try {
      await booksDataStore.ensureLoaded()
      await booksDataStore.ensureCommentaryMetadataLoaded()
      // Must be loaded before the parallel queries below so getCommentaryConnectionTypeIds()
      // in fetchSourceEntriesViaReverseQuery has the ID map available.
      await ensureConnectionTypeNamesLoaded()

      const sql = isMulti
        ? SQL.GET_COMMENTARY_DATA_FOR_SOURCE_LINE_RANGE(multiIds.length)
        : SQL.GET_COMMENTARY_DATA_FOR_SOURCE_LINE
      const params = isMulti ? multiIds : [lineId]

      // Run the forward commentary query and the reverse lookups in parallel.
      // The reverse lookups find source and targum text by reversing the respective
      // link types instead of relying on unreliable forward-direction SOURCE/TARGUM links.
      const lineIdsForReverse = isMulti ? multiIds : [lineId]
      const [rows, sourceEntries, targumEntries] = await Promise.all([
        query<{
          targetBookId: number
          targetLineId: number
          connectionTypeId: number
          lineIndex: number
          content: string
        }>(sql, params),
        fetchSourceEntriesViaReverseQuery(lineIdsForReverse, booksDataStore.allBooksMap),
        fetchTargumEntriesViaReverseQuery(lineIdsForReverse, booksDataStore.allBooksMap),
      ])
      if (!rows.length && !sourceEntries.length && !targumEntries.length) return

      groups.value = await buildCommentaryGroupsFromCombined(rows, sourceEntries, targumEntries, booksDataStore.allBooksMap)

      const pinned = pinnedBookId()
      if (pinned != null && !groups.value.some((g) => g.bookId === pinned)) {
        const staticOrder = staticFilterGroups.value
        const pinnedRank = staticOrder.findIndex((g) => g.bookId === pinned)
        const staticGroup = pinnedRank !== -1 ? staticOrder[pinnedRank] : undefined
        const book = booksDataStore.allBooksMap.get(pinned)
        const bookTitle = book?.title ?? String(pinned)
        const placeholder: CommentaryGroup = {
          bookId: pinned,
          bookTitle,
          path: staticGroup?.path ?? bookTitle,
          connectionTypes: staticGroup?.connectionTypes ?? [],
          lines: [{ lineId: -1, lineIndex: -1, content: NO_TEXT_PLACEHOLDER_CONTENT }],
          category: staticGroup?.category ?? '',
          sectionLabel: staticGroup?.sectionLabel,
          subSectionLabel: staticGroup?.subSectionLabel,
        }
        if (pinnedRank === -1 || !staticOrder.length) {
          groups.value = [placeholder, ...groups.value]
        } else {
          const insertBefore = groups.value.findIndex((g) => {
            const rank = staticOrder.findIndex((s) => s.bookId === g.bookId)
            return rank !== -1 && rank > pinnedRank
          })
          const updated = [...groups.value]
          if (insertBefore === -1) updated.push(placeholder)
          else updated.splice(insertBefore, 0, placeholder)
          groups.value = updated
        }
      }
    } finally {
      const refetchImminent = !loadUsedSectionRange && selectedLineIds() != null && selectedLineIds()!.length > 0
      if (!refetchImminent) loading.value = false
    }
  }

  async function loadStaticFilterGroups(bookId: number, token: number) {
    await booksDataStore.ensureLoaded()
    await booksDataStore.ensureCommentaryMetadataLoaded()

    const nextGroups = await buildStaticCommentaryFilterGroups(bookId, booksDataStore.allBooksMap, staticFilterCache)
    if (token !== staticFilterLoadToken) return

    staticFilterGroups.value = nextGroups
    staticFilterGroupsLoaded.value = true
  }

  watch(
    selectedLineId,
    async (id) => {
      if (id == null) {
        loadedForLineId = null
        loadUsedSectionRange = false
        groups.value = []
        return
      }
      if (selectedLineIds() == null) await nextTick()
      if (selectedLineId() !== id) return
      void load(id)
    },
    { immediate: true },
  )

  // Re-fetch when selectedLineIds becomes available after the initial load.
  // This handles the rare case where selectedLineIds was still null after the
  // nextTick yield above (e.g. lines or TOC took more than one tick to arrive).
  watch(selectedLineIds, (ids) => {
    const lineId = selectedLineId()
    // Only re-fetch if: a line is selected, we already ran a load for it using
    // the single-line fallback, and the section range is now available.
    if (lineId != null && lineId === loadedForLineId && !loadUsedSectionRange && ids != null && ids.length > 0)
      void load(lineId)
  })

  // Lazy — called by useBookView when the related-books dropdown or commentary filter
  // panel first opens. Safe to call multiple times; the staticFilterCache prevents
  // redundant DB queries for the same book.
  async function ensureStaticFilterGroupsLoaded() {
    const id = sourceBookId()
    if (id == null || staticFilterGroupsLoaded.value) return
    staticFilterLoadToken += 1
    await loadStaticFilterGroups(id, staticFilterLoadToken)
  }

  // Reset state when the book changes so stale groups are never shown.
  watch(
    sourceBookId,
    () => {
      staticFilterLoadToken += 1
      staticFilterGroups.value = []
      staticFilterGroupsLoaded.value = false
    },
    { immediate: true },
  )

  // When the pinned book has no commentary for the current line, insert a placeholder
  // group so the user can see the book they were reading rather than a confusing jump.
  const groupsForDisplay = computed<CommentaryGroup[]>(() => {
    const pinned = pinnedBookId()
    // No pin, no line selected yet, still loading, or pinned book is present — nothing to inject
    if (!pinned || selectedLineId() == null || loading.value || groups.value.some((g) => g.bookId === pinned))
      return groups.value

    // Pinned book is absent from this line's commentary — inject a placeholder at the
    // correct position using staticFilterGroups as the canonical order reference.
    const book = booksDataStore.allBooksMap.get(pinned)
    const bookTitle = book?.title ?? String(pinned)

    // Find the pinned book's rank in the static order
    const staticOrder = staticFilterGroups.value
    const pinnedRank = staticOrder.findIndex((g) => g.bookId === pinned)
    // Use the static group's path/sectionLabel/subSectionLabel so the placeholder
    // shows the full label (e.g. "רש"י · מפרשים · ראשונים") matching real groups
    const staticGroup = pinnedRank !== -1 ? staticOrder[pinnedRank] : undefined

    const placeholder: CommentaryGroup = {
      bookId: pinned,
      bookTitle,
      path: staticGroup?.path ?? bookTitle,
      connectionTypes: staticGroup?.connectionTypes ?? [],
      lines: [{ lineId: -1, lineIndex: -1, content: NO_TEXT_PLACEHOLDER_CONTENT }],
      category: staticGroup?.category ?? '',
      sectionLabel: staticGroup?.sectionLabel,
      subSectionLabel: staticGroup?.subSectionLabel,
    }

    if (pinnedRank === -1 || !staticOrder.length) {
      // Not in static order — fall back to inserting at the front
      return [placeholder, ...groups.value]
    }

    // Find the first real group whose static rank comes after the pinned book
    const insertBefore = groups.value.findIndex((g) => {
      const rank = staticOrder.findIndex((s) => s.bookId === g.bookId)
      return rank !== -1 && rank > pinnedRank
    })

    if (insertBefore === -1) {
      // All real groups come before the pinned book — append at the end
      return [...groups.value, placeholder]
    }

    const result = [...groups.value]
    result.splice(insertBefore, 0, placeholder)
    return result
  })

  return { groups, groupsForDisplay, filterGroups, staticFilterGroups, loading, staticFilterGroupsLoaded, ensureStaticFilterGroupsLoaded }
}
