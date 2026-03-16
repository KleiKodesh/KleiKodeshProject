import { ref, watch } from 'vue'
import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'

export interface TocEntry {
  id: number
  parentId: number | null
  level: number
  lineId: number | null
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

export function useToc(bookId: () => number | undefined) {
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
      tocEntries.value = entries

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

  return { tocEntries, altTocSections, loading, error }
}
