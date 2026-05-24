import { computed, ref, watch } from 'vue'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { BookRow } from '../book-catalog/bookCatalogTree'

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

const STATIC_FILTER_CONNECTION_TYPES = new Set<StaticFilterConnectionType>(
  STATIC_FILTER_CONNECTION_TYPE_LIST,
)

const CONNECTION_TYPE_SECTION_LABELS: Record<CommentaryConnectionType, string> = {
  SOURCE: '\u05DE\u05E7\u05D5\u05E8',
  TARGUM: '\u05EA\u05E8\u05D2\u05D5\u05DE\u05D9\u05DD',
  COMMENTARY: '\u05DE\u05E4\u05E8\u05E9\u05D9\u05DD',
  OTHER: '\u05E7\u05E9\u05E8\u05D9\u05DD',
  REFERENCE: '\u05E6\u05D9\u05D5\u05E0\u05D9\u05DD',
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

type ByBookMap = Map<number, { lineIds: Set<number>; connectionTypes: Set<string> }>

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
  allBooksMap: Map<number, BookRow>,
): Promise<CommentaryGroup[]> {
  await ensureConnectionTypeNamesLoaded()
  const byBook: ByBookMap = new Map()
  const lineData = new Map<number, { lineIndex: number; content: string }>()

  for (const row of rows) {
    if (!byBook.has(row.targetBookId))
      byBook.set(row.targetBookId, { lineIds: new Set(), connectionTypes: new Set() })
    const group = byBook.get(row.targetBookId)!
    group.lineIds.add(row.targetLineId)
    group.connectionTypes.add(getConnectionTypeName(row.connectionTypeId))

    lineData.set(row.targetLineId, { lineIndex: row.lineIndex, content: row.content })
  }

  const entries: CommentaryBookEntry[] = [...byBook.entries()].map(([bookId, group]) => {
    const book = allBooksMap.get(bookId)
    const connectionTypes = [...group.connectionTypes]

    return {
      bookId,
      bookTitle: book?.title ?? String(bookId),
      connectionTypes,
      lines: [...group.lineIds]
        .map((id) => ({
          lineId: id,
          lineIndex: lineData.get(id)?.lineIndex ?? 0,
          content: lineData.get(id)?.content ?? '',
        }))
        .sort((a, b) => a.lineIndex - b.lineIndex),
      category: resolveCategory(book),
      treeOrder: book?.treeOrder ?? 999999,
      primaryConnectionType: getPrimaryConnectionType(connectionTypes),
    }
  })

  return buildCommentaryGroupsFromEntries(entries)
}

async function buildStaticCommentaryFilterGroups(
  sourceBookId: number,
  allBooksMap: Map<number, BookRow>,
  instanceCache: Map<number, CommentaryGroup[]>,
): Promise<CommentaryGroup[]> {
  const cached = instanceCache.get(sourceBookId)
  if (cached) return cached
  await ensureConnectionTypeNamesLoaded()
  const connectionTypeIds = STATIC_FILTER_CONNECTION_TYPE_LIST.map((name) =>
    getConnectionTypeId(name),
  )
  if (connectionTypeIds.some((id) => id == null)) return []

  const rows = await query<{ targetBookId: number; connectionTypeId: number }>(
    SQL.GET_STATIC_COMMENTARY_FILTER_BOOKS_FOR_SOURCE_BOOK,
    [sourceBookId, ...connectionTypeIds],
  )
  if (!rows.length) return []

  const byBook = new Map<number, Set<string>>()
  for (const row of rows) {
    if (!byBook.has(row.targetBookId)) byBook.set(row.targetBookId, new Set())
    byBook.get(row.targetBookId)!.add(getConnectionTypeName(row.connectionTypeId))
  }

  const entries: CommentaryBookEntry[] = [...byBook.entries()].map(([bookId, typesSet]) => {
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

  const result = buildCommentaryGroupsFromEntries(entries)
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
  let lastLoadUsedSingleLine = false

  async function load(lineId: number) {
    loadedForLineId = lineId
    // Capture selectedLineIds synchronously before any await — by the time the
    // async steps below complete, tocEntries/lines may have loaded and changed it.
    const multiIds = selectedLineIds()
    const isMulti = multiIds != null && multiIds.length > 0
    lastLoadUsedSingleLine = !isMulti
    loading.value = true
    groups.value = []
    try {
      await booksDataStore.ensureLoaded()
      await booksDataStore.ensureCommentaryMetadataLoaded()

      const sql = isMulti
        ? SQL.GET_COMMENTARY_DATA_FOR_SOURCE_LINE_RANGE(multiIds.length)
        : SQL.GET_COMMENTARY_DATA_FOR_SOURCE_LINE
      const params = isMulti ? multiIds : [lineId]

      const rows = await query<{
        targetBookId: number
        targetLineId: number
        connectionTypeId: number
        lineIndex: number
        content: string
      }>(sql, params)
      if (!rows.length) return

      groups.value = await buildCommentaryGroupsFromCombined(rows, booksDataStore.allBooksMap)
    } finally {
      // If this was a single-line fallback load that returned nothing, and the section
      // range is already available, the selectedLineIds watcher will fire synchronously
      // after this finally block and start gen=2. Keep loading=true so the restore
      // watcher doesn't fire between gen=1 and gen=2.
      const refetchImminent = lastLoadUsedSingleLine && selectedLineIds() != null && selectedLineIds()!.length > 0
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
    (id) => {
      if (id != null) void load(id)
      else { loadedForLineId = null; lastLoadUsedSingleLine = false; groups.value = [] }
    },
    { immediate: true },
  )

  // Re-fetch when selectedLineIds becomes available after the initial load.
  // On session restore, commentaryLineId is set before tocEntries/lines are ready,
  // so the first load() call has selectedLineIds() = null and falls back to the
  // single-line query (which returns nothing for TOC entry lines). Once tocEntries
  // and lines load, selectedLineIds becomes non-null — re-fetch with the section range.
  // Only re-fetch if the last load used the single-line fallback (lastLoadUsedSingleLine),
  // meaning the section range wasn't available yet. If load() already used the section
  // range (interactive click with tocEntries loaded), skip to avoid a double-fetch.
  watch(selectedLineIds, (ids) => {
    const lineId = selectedLineId()
    if (lineId != null && lineId === loadedForLineId && lastLoadUsedSingleLine && ids != null && ids.length > 0)
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
    // No pin, still loading, or pinned book is present — nothing to inject
    if (!pinned || loading.value || groups.value.some((g) => g.bookId === pinned))
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
