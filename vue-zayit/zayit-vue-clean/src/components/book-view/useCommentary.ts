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

function resolveCategory(book: BookRow | undefined): string {
  if (!book) return 'אחר'
  if (book.period && book.period !== 'אחר') return book.period
  return book.rootCategory ?? 'אחר'
}
const CATEGORY_ORDER = [
  'תנ"ך', 'משנה', 'תוספתא', 'תלמוד',
  'מדרש',
  'גאונים', 'ראשונים', 'אחרונים',
  'אחר',
]

function categoryRank(cat: string): number {
  const idx = CATEGORY_ORDER.indexOf(cat)
  return idx === -1 ? CATEGORY_ORDER.length - 1 : idx
}

function sortCategoryEntries(entries: [string, { bookId: number }[]][]): [string, { bookId: number }[]][] {
  return entries.sort(([catA], [catB]) => categoryRank(catA) - categoryRank(catB))
}

export function useCommentary(selectedLineId: () => number | null, selectedLineIds: () => number[] | null = () => null) {
  const groups = ref<CommentaryGroup[]>([])
  const loading = ref(false)
  const booksDataStore = useBooksDataStore()

  async function load(lineId: number) {
    const lineIds = selectedLineIds()
    if (lineIds && lineIds.length > 0) {
      await loadMultiple(lineIds)
    } else {
      await loadSingle(lineId)
    }
  }

  async function loadSingle(lineId: number) {
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

      // Group COMMENTARY only by resolved category
      const addMergedByCategory = () => {
        const items = [...(byType.get('COMMENTARY') ?? [])]
        if (!items.length) return
        const byCat = new Map<string, typeof items>()
        for (const g of items) {
          if (!byCat.has(g.category)) byCat.set(g.category, [])
          byCat.get(g.category)!.push(g)
        }
        const sorted = sortCategoryEntries(
          [...byCat.entries()].map(([cat, gs]) => [cat, gs.map(g => ({ bookId: g.bookId }))])
        )
        for (const [cat] of sorted) {
          const catItems = byCat.get(cat)!.sort((a, b) => a.bookTitle.localeCompare(b.bookTitle, 'he'))
          for (const g of catItems) {
            result.push({ bookId: g.bookId, bookTitle: g.bookTitle, connectionTypes: g.connectionTypes, lines: g.lines, category: cat, sectionLabel: cat })
          }
        }
      }

      addFlat('SOURCE', 'מקור')
      addFlat('TARGUM', 'תרגומים')
      addMergedByCategory()
      addFlat('OTHER', 'מראי מקומות')
      addFlat('REFERENCE', 'ציונים')

      groups.value = result
    } finally {
      loading.value = false
    }
  }

  async function loadMultiple(sourceLineIds: number[]) {
    loading.value = true
    groups.value = []
    try {
      await booksDataStore.ensureLoaded()
      const links = await query<{ targetBookId: number; targetLineId: number; connectionType: string }>(
        SQL.GET_LINKS_FOR_SOURCE_LINE_RANGE(sourceLineIds.length), sourceLineIds
      )
      if (!links.length) return

      const byBook = new Map<number, { lineIds: number[]; connectionTypes: Set<string> }>()
      for (const l of links) {
        if (!byBook.has(l.targetBookId)) byBook.set(l.targetBookId, { lineIds: [], connectionTypes: new Set() })
        const g = byBook.get(l.targetBookId)!
        if (!g.lineIds.includes(l.targetLineId)) g.lineIds.push(l.targetLineId)
        g.connectionTypes.add(l.connectionType)
      }

      const bookIds = [...byBook.keys()]
      const targetLineIds = [...new Set(links.map(l => l.targetLineId))]
      const [bookRows, lineRows] = await Promise.all([
        query<{ id: number; title: string }>(`SELECT id, title FROM book WHERE id IN (${bookIds.map(() => '?').join(',')})`, bookIds),
        query<{ id: number; lineIndex: number; content: string }>(`SELECT id, lineIndex, content FROM line WHERE id IN (${targetLineIds.map(() => '?').join(',')})`, targetLineIds),
      ])

      const bookTitleMap = new Map(bookRows.map(b => [b.id, b.title]))
      const lineMap = new Map(lineRows.map(l => [l.id, l]))

      const raw = bookIds.map(bookId => {
        const g = byBook.get(bookId)!
        const book = booksDataStore.allBooks.find(b => b.id === bookId)
        const ct = [...g.connectionTypes][0] ?? 'OTHER'
        const category = resolveCategory(book)
        return {
          bookId,
          bookTitle: bookTitleMap.get(bookId) ?? String(bookId),
          connectionTypes: [...g.connectionTypes],
          lines: g.lineIds.map(id => ({ lineId: id, lineIndex: lineMap.get(id)?.lineIndex ?? 0, content: lineMap.get(id)?.content ?? '' })).sort((a, b) => a.lineIndex - b.lineIndex),
          category,
          ct,
        }
      })

      const byType = new Map<string, typeof raw>()
      for (const g of raw) {
        if (!byType.has(g.ct)) byType.set(g.ct, [])
        byType.get(g.ct)!.push(g)
      }

      const result: CommentaryGroup[] = []
      const addFlat = (ct: string, label: string) => {
        const items = byType.get(ct) ?? []
        for (const g of items.sort((a, b) => a.bookTitle.localeCompare(b.bookTitle, 'he')))
          result.push({ bookId: g.bookId, bookTitle: g.bookTitle, connectionTypes: g.connectionTypes, lines: g.lines, category: g.category, sectionLabel: label })
      }
      const addMergedByCategory = () => {
        const items = [...(byType.get('COMMENTARY') ?? [])]
        if (!items.length) return
        const byCat = new Map<string, typeof items>()
        for (const g of items) { if (!byCat.has(g.category)) byCat.set(g.category, []); byCat.get(g.category)!.push(g) }
        const sorted = sortCategoryEntries([...byCat.entries()].map(([cat, gs]) => [cat, gs.map(g => ({ bookId: g.bookId }))]))
        for (const [cat] of sorted) {
          for (const g of byCat.get(cat)!.sort((a, b) => a.bookTitle.localeCompare(b.bookTitle, 'he')))
            result.push({ bookId: g.bookId, bookTitle: g.bookTitle, connectionTypes: g.connectionTypes, lines: g.lines, category: cat, sectionLabel: cat })
        }
      }
      addFlat('SOURCE', 'מקור')
      addFlat('TARGUM', 'תרגומים')
      addMergedByCategory()
      addFlat('OTHER', 'מראי מקומות')
      addFlat('REFERENCE', 'ציונים')
      groups.value = result
    } finally {
      loading.value = false
    }
  }

  watch(selectedLineId, id => { if (id != null) load(id); else groups.value = [] }, { immediate: true })

  return { groups, loading }
}
