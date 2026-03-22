import { ref, watch } from 'vue'
import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { BookRow } from '@/components/books-fs/booksFsTree'

export interface CommentaryLine { lineId: number; lineIndex: number; content: string }
export interface CommentaryGroup {
  bookId: number
  bookTitle: string
  connectionTypes: string[]
  lines: CommentaryLine[]
  category?: string      // resolved category label (e.g. ראשונים, תנ"ך)
  sectionLabel?: string  // display label for header (e.g. מפרשים - ראשונים)
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

  for (const g of groups) {
    const label = g.sectionLabel ?? g.bookTitle
    if (!currentSection || currentSection.label !== label) {
      currentSection = { type: 'section', label, children: [] }
      root.push(currentSection)
    }
    currentSection.children.push({
      type: 'book',
      label: g.bookTitle,
      bookId: g.bookId,
      firstLineIndex: g.lines[0]?.lineIndex,
      children: [],
    })
  }

  return root
}

// Connection type display order: SOURCE first, then TARGUM, then COMMENTARY (grouped), then REFERENCE, then OTHER (grouped)
const CT_ORDER: Record<string, number> = { SOURCE: 0, TARGUM: 1, COMMENTARY: 2, REFERENCE: 3, OTHER: 4 }

const SECONDARY_ORDER = ['תנ"ך', 'משנה', 'תוספתא', 'תלמוד']
const PERIOD_ORDER = ['תלמוד', 'גאונים', 'ראשונים', 'אחרונים']

function resolveCategory(book: BookRow | undefined): string {
  if (!book) return 'אחר'
  if (book.period && book.period !== 'אחר') return book.period
  const root = book.rootCategory ?? 'אחר'
  const useSecondary = root === 'תנ"ך' || root === 'משנה' || root === 'תלמוד'
  return (useSecondary && book.secondaryCategory) ? book.secondaryCategory : root
}

function sortCategoryEntries(
  entries: [string, { bookId: number }[]][],
  allBooks: BookRow[]
): [string, { bookId: number }[]][] {
  return entries.sort(([catA, groupsA], [catB, groupsB]) => {
    const bookA = allBooks.find(b => b.id === groupsA[0]?.bookId)
    const bookB = allBooks.find(b => b.id === groupsB[0]?.bookId)

    const isSecondaryA = bookA?.secondaryCategory === catA
    const isSecondaryB = bookB?.secondaryCategory === catB

    const secIdxA = isSecondaryA ? SECONDARY_ORDER.indexOf(catA) : -1
    const secIdxB = isSecondaryB ? SECONDARY_ORDER.indexOf(catB) : -1
    const perIdxA = PERIOD_ORDER.indexOf(catA)
    const perIdxB = PERIOD_ORDER.indexOf(catB)

    if (secIdxA !== -1 && perIdxB !== -1 && catB !== 'תלמוד') return -1
    if (perIdxA !== -1 && catA !== 'תלמוד' && secIdxB !== -1) return 1
    if (secIdxA !== -1 && secIdxB !== -1) return secIdxA - secIdxB
    if (perIdxA !== -1 && perIdxB !== -1) return perIdxA - perIdxB
    if (secIdxA !== -1) return -1
    if (secIdxB !== -1) return 1
    if (perIdxA !== -1) return -1
    if (perIdxB !== -1) return 1

    const orderA = isSecondaryA ? (bookA?.secondaryCategoryOrder ?? 999) : (bookA?.rootCategoryOrder ?? 999)
    const orderB = isSecondaryB ? (bookB?.secondaryCategoryOrder ?? 999) : (bookB?.rootCategoryOrder ?? 999)
    return orderA - orderB
  })
}

export function useCommentary(selectedLineId: () => number | null) {
  const groups = ref<CommentaryGroup[]>([])
  const loading = ref(false)
  const booksDataStore = useBooksDataStore()

  async function load(lineId: number) {
    loading.value = true
    groups.value = []
    try {
      // Ensure book metadata is available before resolving categories
      await booksDataStore.ensureLoaded()
      const links = await query<{ targetBookId: number; targetLineId: number; connectionType: string }>(
        SQL.GET_LINKS_FOR_SOURCE_LINE, [lineId]
      )
      if (!links.length) return

      const byBook = new Map<number, { lineIds: number[]; connectionTypes: Set<string> }>()
      for (const l of links) {
        if (!byBook.has(l.targetBookId)) byBook.set(l.targetBookId, { lineIds: [], connectionTypes: new Set() })
        const g = byBook.get(l.targetBookId)!
        g.lineIds.push(l.targetLineId)
        g.connectionTypes.add(l.connectionType)
      }

      const bookIds = [...byBook.keys()]
      const lineIds = links.map(l => l.targetLineId)
      const [bookRows, lineRows] = await Promise.all([
        query<{ id: number; title: string }>(`SELECT id, title FROM book WHERE id IN (${bookIds.map(() => '?').join(',')})`, bookIds),
        query<{ id: number; lineIndex: number; content: string }>(`SELECT id, lineIndex, content FROM line WHERE id IN (${lineIds.map(() => '?').join(',')})`, lineIds),
      ])

      const bookTitleMap = new Map(bookRows.map(b => [b.id, b.title]))
      const lineMap = new Map(lineRows.map(l => [l.id, l]))

      // Build raw groups with category resolved
      const raw = bookIds.map(bookId => {
        const g = byBook.get(bookId)!
        const book = booksDataStore.allBooks.find(b => b.id === bookId)
        const ct = [...g.connectionTypes][0] ?? 'OTHER'
        const category = resolveCategory(book)
        return {
          bookId,
          bookTitle: bookTitleMap.get(bookId) ?? String(bookId),
          connectionTypes: [...g.connectionTypes],
          lines: g.lineIds.map(id => ({ lineId: id, lineIndex: lineMap.get(id)?.lineIndex ?? 0, content: lineMap.get(id)?.content ?? '' })),
          category,
          ct,
        }
      })

      // Separate by connection type
      const byType = new Map<string, typeof raw>()
      for (const g of raw) {
        if (!byType.has(g.ct)) byType.set(g.ct, [])
        byType.get(g.ct)!.push(g)
      }

      const result: CommentaryGroup[] = []

      // Helper: add flat groups sorted by title
      const addFlat = (ct: string, label: string) => {
        const items = byType.get(ct) ?? []
        for (const g of items.sort((a, b) => a.bookTitle.localeCompare(b.bookTitle, 'he'))) {
          result.push({ bookId: g.bookId, bookTitle: g.bookTitle, connectionTypes: g.connectionTypes, lines: g.lines, category: g.category, sectionLabel: label })
        }
      }

      // Helper: add category-grouped groups (COMMENTARY / OTHER)
      const addGrouped = (ct: string, labelPrefix: string) => {
        const items = byType.get(ct) ?? []
        if (!items.length) return
        // group by category
        const byCat = new Map<string, typeof raw>()
        for (const g of items) {
          if (!byCat.has(g.category)) byCat.set(g.category, [])
          byCat.get(g.category)!.push(g)
        }
        const sorted = sortCategoryEntries(
          [...byCat.entries()].map(([cat, gs]) => [cat, gs.map(g => ({ bookId: g.bookId }))]),
          booksDataStore.allBooks
        )
        for (const [cat] of sorted) {
          const catItems = byCat.get(cat)!.sort((a, b) => a.bookTitle.localeCompare(b.bookTitle, 'he'))
          for (const g of catItems) {
            result.push({ bookId: g.bookId, bookTitle: g.bookTitle, connectionTypes: g.connectionTypes, lines: g.lines, category: cat, sectionLabel: `${labelPrefix} - ${cat}` })
          }
        }
      }

      addFlat('SOURCE', 'מקור')
      addFlat('TARGUM', 'תרגומים')
      addGrouped('COMMENTARY', 'מפרשים')
      addFlat('REFERENCE', 'קשרים')
      addGrouped('OTHER', 'שונות')

      groups.value = result
    } finally {
      loading.value = false
    }
  }

  watch(selectedLineId, id => { if (id != null) load(id); else groups.value = [] }, { immediate: true })

  return { groups, loading }
}
