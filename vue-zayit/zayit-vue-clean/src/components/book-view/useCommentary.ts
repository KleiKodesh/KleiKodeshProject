import { ref, watch } from 'vue'
import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'

export interface CommentaryLine { lineId: number; content: string }
export interface CommentaryGroup { bookId: number; bookTitle: string; connectionTypes: string[]; lines: CommentaryLine[] }

const CT_ORDER: Record<string, number> = { TARGUM: 0, COMMENTARY: 1, SOURCE: 2, REFERENCE: 3, OTHER: 4 }

export function useCommentary(selectedLineId: () => number | null) {
  const groups = ref<CommentaryGroup[]>([])
  const loading = ref(false)

  async function load(lineId: number) {
    loading.value = true
    groups.value = []
    try {
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
        query<{ id: number; content: string }>(`SELECT id, content FROM line WHERE id IN (${lineIds.map(() => '?').join(',')})`, lineIds),
      ])

      const bookTitleMap = new Map(bookRows.map(b => [b.id, b.title]))
      const lineContentMap = new Map(lineRows.map(l => [l.id, l.content]))

      groups.value = bookIds.map(bookId => {
        const g = byBook.get(bookId)!
        return {
          bookId,
          bookTitle: bookTitleMap.get(bookId) ?? String(bookId),
          connectionTypes: [...g.connectionTypes],
          lines: g.lineIds.map(id => ({ lineId: id, content: lineContentMap.get(id) ?? '' })),
        }
      }).sort((a, b) => {
        const diff = (CT_ORDER[a.connectionTypes[0] ?? ''] ?? 99) - (CT_ORDER[b.connectionTypes[0] ?? ''] ?? 99)
        return diff !== 0 ? diff : a.bookTitle.localeCompare(b.bookTitle, 'he')
      })
    } finally {
      loading.value = false
    }
  }

  watch(selectedLineId, id => { if (id != null) load(id); else groups.value = [] }, { immediate: true })

  return { groups, loading }
}
