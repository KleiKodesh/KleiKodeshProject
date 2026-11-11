import { t, i18n } from '@/i18n'
import { ref, shallowRef, markRaw, computed, watch, onScopeDispose } from 'vue'
import { getAllTocEntries, getAltTocStructures, getAllAltTocEntries, getLineContents } from '@/webview-host/seforimApi'
import { sampleNumberedLeaves, printsOwnNumbering } from './altTocTextLabels'
import { SearchableTree, stripTocTitleRoots } from './tocSearchUtils'
import { registerOpenTabToc, findOpenTabToc, findOpenTabTocView, type OpenTabToc } from './openTabTocRegistry'
import type { TocEntry, AltTocStructure } from '@/webview-host/queries.types'

export interface AltTocSection {
  structure: AltTocStructure
  entries: TocEntry[]
  searchTree: SearchableTree | null // built lazily on first search use
}

/**
 * The alt-TOC pane: every alt structure of the book in one tree. With more than one
 * structure, each sits under a titled group row of its own (id = -structureId, no
 * line); with one, its entries stand as they are.
 */
export interface AltTocPane {
  /** Changes when the book's structures change — keys the pane's tree. */
  key: string
  entries: TocEntry[]
  searchTree: SearchableTree | null // built lazily on first search use
}

/** The pane title of a structure: our own name for its key, else its Hebrew title. */
function altTocStructureTitle(structure: AltTocStructure, bookTitle: string | undefined): string {
  const nameKey = `bookView.toc.altStructure.${structure.key.toLowerCase().replace(/[^a-z0-9]/g, '')}`
  if ((i18n.global as unknown as { te: (k: string) => boolean }).te(nameKey)) return t(nameKey)
  // Some libraries hold the book's own title here instead of the structure's name.
  const heTitle = structure.heTitle?.trim()
  return heTitle && heTitle !== bookTitle?.trim() ? heTitle : structure.key
}

function buildAltTocPane(sections: AltTocSection[], bookTitle: string | undefined): AltTocPane | null {
  if (!sections.length) return null
  const key = sections.map((s) => s.structure.id).join(',')
  if (sections.length === 1) return { key, entries: sections[0]!.entries, searchTree: null }
  const entries: TocEntry[] = []
  for (const { structure, entries: own } of sections) {
    const groupId = -structure.id
    entries.push({
      id: groupId, parentId: null, level: 0, hasChildren: own.length > 0,
      text: altTocStructureTitle(structure, bookTitle), lineId: null, lineIndex: null,
    })
    for (const e of own) entries.push({ ...e, parentId: e.parentId ?? groupId, level: e.level + 1 })
  }
  return { key, entries: markRaw(entries), searchTree: null }
}

/** Ids of the structures whose book prints their numbering itself — see altTocTextLabels. */
async function findStructuresPrintingNumbers(sections: AltTocSection[]): Promise<Set<number>> {
  const samples = sections.map((s) => ({ id: s.structure.id, sample: sampleNumberedLeaves(s.entries) }))
  const lineIds = [...new Set(samples.flatMap((s) => s.sample.map((e) => e.lineId!)))]
  const printing = new Set<number>()
  if (!lineIds.length) return printing
  const contents = new Map((await getLineContents(lineIds)).map((r) => [r.id, r.content]))
  for (const { id, sample } of samples) if (printsOwnNumbering(sample, contents)) printing.add(id)
  return printing
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
  const tocEntries = shallowRef<TocEntry[]>(markRaw([]))
  const altTocSections = shallowRef<AltTocSection[]>([])
  const altTocStructuresPrintingNumbers = shallowRef<ReadonlySet<number>>(new Set())
  const loading = ref(false)
  const tocLoaded = ref(false) // true once the first load completes, even if entries is empty
  const error = ref<string | null>(null)
  const tocSearchTree = shallowRef<SearchableTree>(new SearchableTree([]))

  const altTocPane = computed<AltTocPane | null>(() => buildAltTocPane(altTocSections.value, bookTitle?.()))

  // This tab's entry in the open-tab TOC lookup (not a cache: removed on dispose).
  const openTabToc: OpenTabToc = { bookId: undefined, raw: null, pending: null, view: null }
  onScopeDispose(registerOpenTabToc(openTabToc))
  let loadGeneration = 0

  async function load(id: number) {
    const generation = ++loadGeneration
    const title = bookTitle?.()
    openTabToc.bookId = id
    openTabToc.raw = null
    openTabToc.view = null
    openTabToc.pending = null
    loading.value = true
    error.value = null
    try {
      // Another open tab on this book already holds its TOC: take its arrays as is.
      const view = findOpenTabTocView(id, title, openTabToc)
      const shared = view ? null : findOpenTabToc(id, openTabToc)
      let raw: TocEntry[]
      if (view) {
        raw = findOpenTabToc(id, openTabToc) as TocEntry[]
      } else if (shared) {
        raw = await shared
      } else {
        const fetching = getAllTocEntries(id)
        openTabToc.pending = fetching
        raw = await fetching
      }
      if (generation !== loadGeneration) return // the book changed mid-flight
      const entries = view?.entries ?? markRaw(stripBookTitleRoot(raw, title, id))
      const searchTree = view?.searchTree ?? new SearchableTree(entries)
      openTabToc.raw = markRaw(raw)
      openTabToc.view = { bookTitle: title, entries, searchTree }
      tocEntries.value = entries
      tocSearchTree.value = searchTree
    } catch (e) {
      if (generation !== loadGeneration) return
      // TOC load failed — show the book with no TOC rather than a blank page
      tocEntries.value = markRaw([])
      tocSearchTree.value = new SearchableTree([])
      error.value = e instanceof Error ? e.message : t('bookView.toc.loadError')
    } finally {
      if (generation === loadGeneration) {
        openTabToc.pending = null
        loading.value = false
        tocLoaded.value = true
      }
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
      // Before the sections land, so labels of a numbering the book prints never flash.
      const printing = await findStructuresPrintingNumbers(sections).catch(() => new Set<number>())
      if (generation !== altTocGeneration) return
      altTocStructuresPrintingNumbers.value = printing
      altTocSections.value = sections
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
        altTocStructuresPrintingNumbers.value = new Set()
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
    (altTocPane.value?.entries ?? [])
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
    altTocStructuresPrintingNumbers,
    altTocPane,
    loading,
    tocLoaded,
    error,
    tocSearchTree,
    getActiveTocEntry,
    getActiveAltTocEntry,
    getTocPath,
  }
}
