import { ref, watch } from 'vue'
import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'

export interface TocEntry {
  id: number
  parentId: number | null
  level: number
  lineId: number | null
  lineIndex: number | null
  hasChildren: number
  text: string
}

export interface AltTocStructure {
  id: number
  key: string
  title: string | null
  heTitle: string | null
}

export interface AltTocSection {
  structure: AltTocStructure
  entries: TocEntry[]
}

function stripBookTitleRoot(entries: TocEntry[], bookTitle: string | undefined): TocEntry[] {
  if (!bookTitle || entries.length === 0) return entries
  const roots = entries.filter(e => e.parentId === null)
  if (roots.length !== 1 || roots[0]!.text !== bookTitle) return entries
  const rootId = roots[0]!.id
  return entries
    .filter(e => e.id !== rootId)
    .map(e => e.parentId === rootId ? { ...e, parentId: null, level: e.level - 1 } : { ...e, level: e.level - 1 })
}

export function useToc(bookId: () => number | undefined, bookTitle?: () => string | undefined) {
  const tocEntries = ref<TocEntry[]>([])
  const altTocSections = ref<AltTocSection[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function load(id: number) {
    loading.value = true
    error.value = null
    try {
      const [entries, structures] = await Promise.all([
        query<TocEntry>(SQL.GET_ALL_TOC_ENTRIES, [id]),
        query<AltTocStructure>(SQL.GET_ALT_TOC_STRUCTURES, [id]),
      ])
      tocEntries.value = stripBookTitleRoot(entries, bookTitle?.())

      const sections = await Promise.all(
        structures.map(async (s) => ({
          structure: s,
          entries: await query<TocEntry>(SQL.GET_ALL_ALT_TOC_ENTRIES, [s.id]),
        }))
      )
      altTocSections.value = sections
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'שגיאה בטעינת תוכן עניינים'
    } finally {
      loading.value = false
    }
  }

  watch(
    bookId,
    (id) => { if (id != null) load(id) },
    { immediate: true }
  )

  function getActiveTocEntry(lineIndex: number): TocEntry | null {
    let active: TocEntry | null = null
    for (const e of tocEntries.value) {
      if (e.lineIndex == null) continue
      if (e.lineIndex <= lineIndex) active = e
      else break
    }
    return active
  }

  function getTocPath(entry: TocEntry): string {
    const map = new Map(tocEntries.value.map(e => [e.id, e]))
    const parts: string[] = []
    let current: TocEntry | undefined = entry
    while (current) {
      parts.unshift(current.text)
      current = current.parentId != null ? map.get(current.parentId) : undefined
    }
    return parts.join(' / ')
  }

  return { tocEntries, altTocSections, loading, error, getActiveTocEntry, getTocPath }
}
