import { ref, watch } from 'vue'
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import { SearchableTree, stripTocTitleRoots } from '@/utils/tocSearchUtils'

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
  searchTree: SearchableTree | null // built lazily on first search use
}

function stripBookTitleRoot(
  entries: TocEntry[],
  bookTitle: string | undefined,
  bookId: number | undefined,
): TocEntry[] {
  if (!bookTitle) return entries
  return stripTocTitleRoots(entries, bookTitle, { singleRootOnly: true, bookId })
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
      const stripped = stripBookTitleRoot(entries, bookTitle?.(), id)
      tocEntries.value = stripped
      tocSearchTree.value = new SearchableTree(stripped)
      altTocSections.value = await Promise.all(
        structures.map(async (s) => {
          const entries = await query<TocEntry>(SQL.GET_ALL_ALT_TOC_ENTRIES, [s.id])
          return { structure: s, entries, searchTree: null }
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
    const entries = tocEntries.value
    if (!entries.length) return null
    // Binary search for the last entry with lineIndex <= the given lineIndex.
    // Entries without a lineIndex are skipped; the array is ordered by lineIndex.
    let lo = 0
    let hi = entries.length - 1
    let result: TocEntry | null = null
    while (lo <= hi) {
      const mid = (lo + hi) >>> 1
      const e = entries[mid]!
      if (e.lineIndex == null) {
        // scan outward to find a comparable entry
        let found = false
        for (let i = mid - 1; i >= lo; i--) {
          if (entries[i]!.lineIndex != null) {
            hi = i
            found = true
            break
          }
        }
        if (!found) lo = mid + 1
        continue
      }
      if (e.lineIndex <= lineIndex) {
        result = e
        lo = mid + 1
      } else {
        hi = mid - 1
      }
    }
    return result
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
