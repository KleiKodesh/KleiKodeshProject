import { ref, watch } from 'vue'
import { query } from '@/host/seforimDb'
import { SQL } from '@/host/queries.sql'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { BookRow } from '@/components/books-fs/booksCategoryTree'

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

export function buildCommentaryTree(groups: CommentaryGroup[]): CommentaryTreeNode[] {
  const root: CommentaryTreeNode[] = []
  let currentSection: CommentaryTreeNode | null = null
  let currentSubSection: CommentaryTreeNode | null = null

  for (const g of groups) {
    const sectionLabel = g.sectionLabel ?? g.bookTitle
    const subLabel = g.subSectionLabel ?? null

    if (!currentSection || currentSection.label !== sectionLabel) {
      currentSection = { type: 'section', label: sectionLabel, children: [] }
      currentSubSection = null
      root.push(currentSection)
    }

    if (subLabel) {
      if (!currentSubSection || currentSubSection.label !== subLabel) {
        currentSubSection = { type: 'section', label: subLabel, children: [] }
        currentSection.children.push(currentSubSection)
      }
      currentSubSection.children.push({
        type: 'book',
        label: g.bookTitle,
        bookId: g.bookId,
        firstLineIndex: g.lines[0]?.lineIndex,
        children: [],
      })
    } else {
      currentSubSection = null
      currentSection.children.push({
        type: 'book',
        label: g.bookTitle,
        bookId: g.bookId,
        firstLineIndex: g.lines[0]?.lineIndex,
        children: [],
      })
    }
  }

  return root
}

function truncateAtAl(label: string): string {
  const idx = label.indexOf(' על')
  return idx !== -1 ? label.slice(0, idx) : label
}

function resolveCategory(book: BookRow | undefined): string {
  if (!book) return 'אחר'
  if (book.period && book.period !== 'אחר') return truncateAtAl(book.period)
  return truncateAtAl(book.rootCategory ?? 'אחר')
}

const CATEGORY_ORDER = [
  'תנ"ך',
  'משנה',
  'תוספתא',
  'תלמוד',
  'מדרש',
  'גאונים',
  'ראשונים',
  'אחרונים',
  'אחר',
]

function categoryRank(cat: string): number {
  const idx = CATEGORY_ORDER.indexOf(cat)
  return idx === -1 ? CATEGORY_ORDER.length - 1 : idx
}

function sortCategoryEntries(
  entries: [string, { bookId: number }[]][],
): [string, { bookId: number }[]][] {
  return entries.sort(([catA], [catB]) => categoryRank(catA) - categoryRank(catB))
}

// ─── Shared core ────────────────────────────────────────────────────────────

type ByBookMap = Map<number, { lineIds: Set<number>; connectionTypes: Set<string> }>

async function buildCommentaryGroups(
  byBook: ByBookMap,
  allBooksMap: Map<number, BookRow>,
): Promise<CommentaryGroup[]> {
  const bookIds = [...byBook.keys()]
  const lineIds = [...new Set([...byBook.values()].flatMap((g) => [...g.lineIds]))]

  const [bookRows, lineRows] = await Promise.all([
    query<{ id: number; title: string }>(SQL.GET_BOOKS_BY_IDS(bookIds.length), bookIds),
    query<{ id: number; lineIndex: number; content: string }>(
      SQL.GET_LINES_BY_IDS(lineIds.length),
      lineIds,
    ),
  ])

  const bookTitleMap = new Map(bookRows.map((b) => [b.id, b.title]))
  const lineMap = new Map(lineRows.map((l) => [l.id, l]))

  const raw = bookIds.map((bookId) => {
    const g = byBook.get(bookId)!
    const book = allBooksMap.get(bookId)
    const ct = [...g.connectionTypes][0] ?? 'OTHER'
    return {
      bookId,
      bookTitle: bookTitleMap.get(bookId) ?? String(bookId),
      connectionTypes: [...g.connectionTypes],
      lines: [...g.lineIds]
        .map((id) => ({
          lineId: id,
          lineIndex: lineMap.get(id)?.lineIndex ?? 0,
          content: lineMap.get(id)?.content ?? '',
        }))
        .sort((a, b) => a.lineIndex - b.lineIndex),
      category: resolveCategory(book),
      ct,
      treeOrder: book?.treeOrder ?? 999999,
    }
  })

  const byType = new Map<string, typeof raw>()
  for (const g of raw) {
    if (!byType.has(g.ct)) byType.set(g.ct, [])
    byType.get(g.ct)!.push(g)
  }

  const result: CommentaryGroup[] = []
  const byTreeOrder = (a: { treeOrder: number }, b: { treeOrder: number }) =>
    a.treeOrder - b.treeOrder

  const addFlat = (ct: string, sectionLabel: string) => {
    for (const g of (byType.get(ct) ?? []).sort(byTreeOrder))
      result.push({
        bookId: g.bookId,
        bookTitle: g.bookTitle,
        path: `${g.bookTitle} · ${sectionLabel}`,
        connectionTypes: g.connectionTypes,
        lines: g.lines,
        category: g.category,
        sectionLabel,
      })
  }

  const addMergedByCategory = (ct: string, sectionLabel: string) => {
    const items = byType.get(ct) ?? []
    if (!items.length) return
    const byCat = new Map<string, typeof items>()
    for (const g of items) {
      if (!byCat.has(g.category)) byCat.set(g.category, [])
      byCat.get(g.category)!.push(g)
    }
    const sorted = sortCategoryEntries(
      [...byCat.entries()].map(([cat, gs]) => [cat, gs.map((g) => ({ bookId: g.bookId }))]),
    )
    for (const [cat] of sorted) {
      for (const g of byCat.get(cat)!.sort(byTreeOrder))
        result.push({
          bookId: g.bookId,
          bookTitle: g.bookTitle,
          path: `${g.bookTitle} · ${sectionLabel} · ${cat}`,
          connectionTypes: g.connectionTypes,
          lines: g.lines,
          category: cat,
          sectionLabel,
          subSectionLabel: cat,
        })
    }
  }

  addFlat('SOURCE', 'מקור')
  addFlat('TARGUM', 'תרגומים')
  addMergedByCategory('COMMENTARY', 'מפרשים')
  addMergedByCategory('OTHER', 'קשרים')
  addFlat('REFERENCE', 'ציונים')

  return result
}

// ─── Composable ─────────────────────────────────────────────────────────────

export function useCommentary(
  selectedLineId: () => number | null,
  selectedLineIds: () => number[] | null = () => null,
) {
  const groups = ref<CommentaryGroup[]>([])
  const loading = ref(false)
  const booksDataStore = useBooksDataStore()

  async function load(lineId: number) {
    loading.value = true
    groups.value = []
    try {
      await booksDataStore.ensureLoaded()

      const multiIds = selectedLineIds()
      const isMulti = multiIds && multiIds.length > 0
      const sql = isMulti
        ? SQL.GET_LINKS_FOR_SOURCE_LINE_RANGE(multiIds.length)
        : SQL.GET_LINKS_FOR_SOURCE_LINE
      const params = isMulti ? multiIds : [lineId]

      const links = await query<{
        targetBookId: number
        targetLineId: number
        connectionType: string
      }>(sql, params)
      if (!links.length) return

      const byBook: ByBookMap = new Map()
      for (const l of links) {
        if (!byBook.has(l.targetBookId))
          byBook.set(l.targetBookId, { lineIds: new Set(), connectionTypes: new Set() })
        const g = byBook.get(l.targetBookId)!
        g.lineIds.add(l.targetLineId)
        g.connectionTypes.add(l.connectionType)
      }

      groups.value = await buildCommentaryGroups(byBook, booksDataStore.allBooksMap)
    } finally {
      loading.value = false
    }
  }

  watch(
    selectedLineId,
    (id) => {
      if (id != null) load(id)
      else groups.value = []
    },
    { immediate: true },
  )

  return { groups, loading }
}
