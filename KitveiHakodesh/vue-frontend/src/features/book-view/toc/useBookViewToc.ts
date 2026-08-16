import { ref, shallowRef, computed, watch } from 'vue'
import { getAllTocEntries, getAltTocStructures, getAllAltTocEntries } from '@/webview-host/seforimApi'
import { SearchableTree, stripTocTitleRoots } from './tocSearchUtils'
import type { TocEntry, AltTocStructure } from '@/webview-host/queries.types'

export interface AltTocSection {
  structure: AltTocStructure
  entries: TocEntry[]
  searchTree: SearchableTree | null // built lazily on first search use
}

// Priority order for picking the default alt-toc structure when multiple exist.
// Keys not in this list get priority 100 and fall back to the first by id.
function altTocStructurePriority(key: string): number {
  switch (key.toLowerCase()) {
    case 'daf':      return 0
    case 'parasha':  return 1
    case 'chapters': return 2
    case 'topic':    return 3
    case 'section':  return 4
    default:         return 100
  }
}

function pickPreferredAltTocStructure(structures: AltTocStructure[]): AltTocStructure | undefined {
  return structures.reduce<AltTocStructure | undefined>((best, current) =>
    best == null || altTocStructurePriority(current.key) < altTocStructurePriority(best.key)
      ? current
      : best,
    undefined,
  )
}

/**
 * The last entry at or before `lineIndex`. `sorted` must be ascending by
 * lineIndex with no nulls. Where a parent and its first child share a line the
 * child wins, which is what the tree should highlight — TreeView expands the
 * ancestors anyway.
 */
function findEntryAtLine(sorted: TocEntry[], lineIndex: number): TocEntry | null {
  let lo = 0
  let hi = sorted.length - 1
  let result: TocEntry | null = null
  while (lo <= hi) {
    const mid = (lo + hi) >>> 1
    if (sorted[mid]!.lineIndex! <= lineIndex) {
      result = sorted[mid]!
      lo = mid + 1
    } else {
      hi = mid - 1
    }
  }
  return result
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
  const altTocSections = shallowRef<AltTocSection[]>([])
  const selectedAltTocStructureId = ref<number | null>(null)
  const loading = ref(false)
  const tocLoaded = ref(false) // true once the first load completes, even if entries is empty
  const error = ref<string | null>(null)
  const tocSearchTree = shallowRef<SearchableTree>(new SearchableTree([]))

  const selectedAltTocSection = computed<AltTocSection | null>(() => {
    const sections = altTocSections.value
    if (!sections.length) return null
    return sections.find((s) => s.structure.id === selectedAltTocStructureId.value) ?? sections[0]!
  })

  function selectAltTocStructure(structure: AltTocStructure) {
    selectedAltTocStructureId.value = structure.id
  }

  async function load(id: number) {
    loading.value = true
    error.value = null
    try {
      const entries = await getAllTocEntries(id)
      const stripped = stripBookTitleRoot(entries, bookTitle?.(), id)
      tocEntries.value = stripped
      tocSearchTree.value = new SearchableTree(stripped)
    } catch (e) {
      // TOC load failed — show the book with no TOC rather than a blank page
      tocEntries.value = []
      tocSearchTree.value = new SearchableTree([])
      error.value = e instanceof Error ? e.message : 'שגיאה בטעינת תוכן עניינים'
    } finally {
      loading.value = false
      tocLoaded.value = true
    }
  }

  let altTocGeneration = 0

  // Loaded with the book, not on demand: the alt TOC labels are rendered inline
  // in the lines view, so they can't wait for the TOC panel to be opened.
  async function loadAltTocSections(id: number) {
    const generation = ++altTocGeneration
    try {
      const structures = await getAltTocStructures(id)
      const sections = await Promise.all(
        structures.map(async (s) => {
          const entries = await getAllAltTocEntries(s.id)
          return { structure: s, entries, searchTree: null }
        }),
      )
      if (generation !== altTocGeneration) return // the book changed mid-flight
      altTocSections.value = sections
      selectedAltTocStructureId.value =
        pickPreferredAltTocStructure(sections.map((s) => s.structure))?.id ?? null
    } catch {
      // alt TOC is non-critical — silently ignore errors
    }
  }

  watch(
    bookId,
    (id) => {
      if (id != null) {
        tocLoaded.value = false
        altTocSections.value = []
        selectedAltTocStructureId.value = null
        load(id)
        loadAltTocSections(id)
      }
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

  // Alt entries arrive ordered by id, which is document order for most structures
  // but not all — a handful interleave their levels. Sorting once per section
  // keeps the lookup a plain binary search.
  const altTocLineOrder = computed(() =>
    (selectedAltTocSection.value?.entries ?? [])
      .filter((e) => e.lineIndex != null)
      .sort((a, b) => a.lineIndex! - b.lineIndex!),
  )

  function getActiveAltTocEntry(lineIndex: number): TocEntry | null {
    return findEntryAtLine(altTocLineOrder.value, lineIndex)
  }

  function getTocPath(entry: TocEntry): string {
    return tocSearchTree.value.displayPaths.get(entry.id) ?? entry.text
  }

  return {
    tocEntries,
    altTocSections,
    selectedAltTocSection,
    selectAltTocStructure,
    loading,
    tocLoaded,
    error,
    tocSearchTree,
    getActiveTocEntry,
    getActiveAltTocEntry,
    getTocPath,
  }
}
