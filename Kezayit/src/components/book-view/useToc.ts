import { ref, watch } from 'vue'
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import { SearchableTree } from '@/utils/tocSearchUtils'

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
  searchTree: SearchableTree
}

function stripBookTitleRoot(entries: TocEntry[], bookTitle: string | undefined): TocEntry[] {
  if (!bookTitle || !entries.length) return entries
  const roots = entries.filter((e) => e.parentId === null)
  if (roots.length !== 1 || roots[0]!.text !== bookTitle) return entries
  const rootId = roots[0]!.id
  return entries
    .filter((e) => e.id !== rootId)
    .map((e) => ({ ...e, parentId: e.parentId === rootId ? null : e.parentId, level: e.level - 1 }))
}

export function useToc(bookId: () => number | undefined, bookTitle?: () => string | undefined) {
  const tocEntries = ref<TocEntry[]>([])
  const altTocSections = ref<AltTocSection[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const tocSearchTree = ref<SearchableTree>(new SearchableTree([]))

  async function load(id: number) {
    loading.value = true
    error.value = null
    try {
      const [entries, structures] = await Promise.all([
        query<TocEntry>(SQL.GET_ALL_TOC_ENTRIES, [id]),
        query<AltTocStructure>(SQL.GET_ALT_TOC_STRUCTURES, [id]),
      ])
      const stripped = stripBookTitleRoot(entries, bookTitle?.())
      tocEntries.value = stripped
      tocSearchTree.value = new SearchableTree(stripped)
      altTocSections.value = await Promise.all(
        structures.map(async (s) => {
          const entries = await query<TocEntry>(SQL.GET_ALL_ALT_TOC_ENTRIES, [s.id])
          return { structure: s, entries, searchTree: new SearchableTree(entries) }
        }),
      )
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'שגיאה בטעינת תוכן עניינים'
    } finally {
      loading.value = false
    }
  }

  watch(
    bookId,
    (id) => {
      if (id != null) load(id)
    },
    { immediate: true },
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
    return tocSearchTree.value.displayPaths.get(entry.id) ?? entry.text
  }

  return {
    tocEntries,
    altTocSections,
    loading,
    error,
    tocSearchTree,
    getActiveTocEntry,
    getTocPath,
  }
}
