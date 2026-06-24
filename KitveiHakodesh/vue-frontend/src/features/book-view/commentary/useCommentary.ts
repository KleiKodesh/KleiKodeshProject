import { computed, nextTick, ref, watch } from 'vue'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import { useBooksDataStore } from '@/stores/booksDataStore'
import {
  ensureConnectionTypeNamesLoaded,
  getPrimaryConnectionType,
  isStaticFilterConnectionType,
} from './commentaryConnectionTypes'
import {
  buildCommentaryGroupsFromCombined,
  buildStaticCommentaryFilterGroups,
  fetchSourceEntriesViaReverseQuery,
  fetchTargumEntriesViaReverseQuery,
} from './commentaryGroupBuilder'

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

export interface CommentaryBookEntry {
  bookId: number
  bookTitle: string
  connectionTypes: string[]
  lines: CommentaryLine[]
  category: string
  treeOrder: number
  primaryConnectionType: string
}

export {
  STATIC_FILTER_CONNECTION_TYPES,
  SECTION_LABEL_TO_CONNECTION_TYPE,
} from './commentaryConnectionTypes'

/** Tree node used by buildCommentaryTree — sections hold child nodes, books are leaves. */
export interface CommentaryTreeNode {
  label: string
  bookId?: number
  firstLineIndex?: number
  children: CommentaryTreeNode[]
}

export function buildCommentaryTree(groups: CommentaryGroup[]): CommentaryTreeNode[] {
  const root: CommentaryTreeNode[] = []
  let currentSection: CommentaryTreeNode | null = null
  let currentSubSection: CommentaryTreeNode | null = null

  for (const group of groups) {
    const sectionLabel = group.sectionLabel ?? group.bookTitle
    const subLabel = group.subSectionLabel ?? null

    if (!currentSection || currentSection.label !== sectionLabel) {
      currentSection = { label: sectionLabel, children: [] }
      currentSubSection = null
      root.push(currentSection)
    }

    const effectiveSubLabel = subLabel && subLabel !== sectionLabel ? subLabel : null

    if (effectiveSubLabel) {
      if (!currentSubSection || currentSubSection.label !== effectiveSubLabel) {
        currentSubSection = { label: effectiveSubLabel, children: [] }
        currentSection.children.push(currentSubSection)
      }
      currentSubSection.children.push({
        label: group.bookTitle,
        bookId: group.bookId,
        firstLineIndex: group.lines[0]?.lineIndex,
        children: [],
      })
    } else {
      currentSubSection = null
      currentSection.children.push({
        label: group.bookTitle,
        bookId: group.bookId,
        firstLineIndex: group.lines[0]?.lineIndex,
        children: [],
      })
    }
  }

  return root
}

const NO_TEXT_PLACEHOLDER_CONTENT = 'אין טקסט לשורה זו'

export function useCommentary(
  selectedLineId: () => number | null,
  selectedLineIds: () => number[] | null = () => null,
  sourceBookId: () => number | undefined = () => undefined,
  filterPanelVisible: () => boolean = () => false,
  pinnedBookId: () => number | null = () => null,
) {
  const groups = ref<CommentaryGroup[]>([])
  const staticFilterGroups = ref<CommentaryGroup[]>([])
  const staticFilterGroupsLoaded = ref(false)
  const loading = ref(false)
  const booksDataStore = useBooksDataStore()
  let staticFilterLoadToken = 0
  // Per-instance cache — scoped to this tab's book, cleared when the composable is destroyed
  const staticFilterCache = new Map<number, CommentaryGroup[]>()

  const filterGroups = computed(() => {
    if (!staticFilterGroupsLoaded.value) return groups.value
    return [
      ...staticFilterGroups.value,
      ...groups.value.filter((group) => {
        const primaryType = getPrimaryConnectionType(group.connectionTypes)
        return !isStaticFilterConnectionType(primaryType)
      }),
    ]
  })

  let loadedForLineId: number | null = null
  let loadUsedSectionRange = false

  async function load(lineId: number) {
    loadedForLineId = lineId
    const multiIds = selectedLineIds()
    const isMulti = multiIds != null && multiIds.length > 0
    loadUsedSectionRange = isMulti
    groups.value = []
    loading.value = true
    try {
      await booksDataStore.ensureLoaded()
      await booksDataStore.ensureCommentaryMetadataLoaded()
      await ensureConnectionTypeNamesLoaded()

      const sql = isMulti
        ? SQL.GET_COMMENTARY_DATA_FOR_SOURCE_LINE_RANGE(multiIds.length)
        : SQL.GET_COMMENTARY_DATA_FOR_SOURCE_LINE
      const params = isMulti ? multiIds : [lineId]

      const lineIdsForReverse = isMulti ? multiIds : [lineId]
      const [rows, sourceEntries, targumEntries] = await Promise.all([
        query<{
          targetBookId: number
          targetLineId: number
          connectionTypeId: number
          lineIndex: number
          content: string
        }>(sql, params),
        fetchSourceEntriesViaReverseQuery(lineIdsForReverse, booksDataStore.allBooksMap),
        fetchTargumEntriesViaReverseQuery(lineIdsForReverse, booksDataStore.allBooksMap),
      ])

      if (!rows.length && !sourceEntries.length && !targumEntries.length) return

      groups.value = await buildCommentaryGroupsFromCombined(
        rows,
        sourceEntries,
        targumEntries,
        booksDataStore.allBooksMap,
      )

      const pinned = pinnedBookId()
      if (pinned != null && !groups.value.some((g) => g.bookId === pinned)) {
        const staticOrder = staticFilterGroups.value
        const pinnedRank = staticOrder.findIndex((g) => g.bookId === pinned)
        const staticGroup = pinnedRank !== -1 ? staticOrder[pinnedRank] : undefined
        const book = booksDataStore.allBooksMap.get(pinned)
        const bookTitle = book?.title ?? String(pinned)
        const placeholder: CommentaryGroup = {
          bookId: pinned,
          bookTitle,
          path: staticGroup?.path ?? bookTitle,
          connectionTypes: staticGroup?.connectionTypes ?? [],
          lines: [{ lineId: -1, lineIndex: -1, content: NO_TEXT_PLACEHOLDER_CONTENT }],
          category: staticGroup?.category ?? '',
          sectionLabel: staticGroup?.sectionLabel,
          subSectionLabel: staticGroup?.subSectionLabel,
        }
        if (pinnedRank === -1 || !staticOrder.length) {
          groups.value = [placeholder, ...groups.value]
        } else {
          const insertBefore = groups.value.findIndex((g) => {
            const rank = staticOrder.findIndex((s) => s.bookId === g.bookId)
            return rank !== -1 && rank > pinnedRank
          })
          const updated = [...groups.value]
          if (insertBefore === -1) updated.push(placeholder)
          else updated.splice(insertBefore, 0, placeholder)
          groups.value = updated
        }
      }
    } finally {
      const refetchImminent =
        !loadUsedSectionRange &&
        selectedLineIds() != null &&
        selectedLineIds()!.length > 0
      if (!refetchImminent) loading.value = false
    }
  }

  async function loadStaticFilterGroups(bookId: number, token: number) {
    await booksDataStore.ensureLoaded()
    await booksDataStore.ensureCommentaryMetadataLoaded()

    const nextGroups = await buildStaticCommentaryFilterGroups(
      bookId,
      booksDataStore.allBooksMap,
      staticFilterCache,
    )
    if (token !== staticFilterLoadToken) return

    staticFilterGroups.value = nextGroups
    staticFilterGroupsLoaded.value = true
  }

  watch(
    selectedLineId,
    async (id) => {
      if (id == null) {
        loadedForLineId = null
        loadUsedSectionRange = false
        groups.value = []
        return
      }
      if (selectedLineIds() == null) await nextTick()
      if (selectedLineId() !== id) return
      void load(id)
    },
    { immediate: true },
  )

  // Re-fetch when selectedLineIds becomes available after the initial load.
  // This handles the rare case where selectedLineIds was still null after the
  // nextTick yield above (e.g. lines or TOC took more than one tick to arrive).
  watch(selectedLineIds, (ids) => {
    const lineId = selectedLineId()
    if (
      lineId != null &&
      lineId === loadedForLineId &&
      !loadUsedSectionRange &&
      ids != null &&
      ids.length > 0
    )
      void load(lineId)
  })

  // Lazy — called by useBookView when the related-books dropdown or commentary filter
  // panel first opens. Safe to call multiple times; the staticFilterCache prevents
  // redundant DB queries for the same book.
  async function ensureStaticFilterGroupsLoaded() {
    const id = sourceBookId()
    if (id == null || staticFilterGroupsLoaded.value) return
    staticFilterLoadToken += 1
    await loadStaticFilterGroups(id, staticFilterLoadToken)
  }

  // Reset state when the book changes so stale groups are never shown.
  watch(
    sourceBookId,
    () => {
      staticFilterLoadToken += 1
      staticFilterGroups.value = []
      staticFilterGroupsLoaded.value = false
    },
    { immediate: true },
  )

  // When the pinned book has no commentary for the current line, insert a placeholder
  // group so the user can see the book they were reading rather than a confusing jump.
  const groupsForDisplay = computed<CommentaryGroup[]>(() => {
    const pinned = pinnedBookId()
    if (
      !pinned ||
      selectedLineId() == null ||
      loading.value ||
      groups.value.some((g) => g.bookId === pinned)
    )
      return groups.value

    // Pinned book is absent from this line's commentary — inject a placeholder at the
    // correct position using staticFilterGroups as the canonical order reference.
    const book = booksDataStore.allBooksMap.get(pinned)
    const bookTitle = book?.title ?? String(pinned)

    const staticOrder = staticFilterGroups.value
    const pinnedRank = staticOrder.findIndex((g) => g.bookId === pinned)
    const staticGroup = pinnedRank !== -1 ? staticOrder[pinnedRank] : undefined

    const placeholder: CommentaryGroup = {
      bookId: pinned,
      bookTitle,
      path: staticGroup?.path ?? bookTitle,
      connectionTypes: staticGroup?.connectionTypes ?? [],
      lines: [{ lineId: -1, lineIndex: -1, content: NO_TEXT_PLACEHOLDER_CONTENT }],
      category: staticGroup?.category ?? '',
      sectionLabel: staticGroup?.sectionLabel,
      subSectionLabel: staticGroup?.subSectionLabel,
    }

    if (pinnedRank === -1 || !staticOrder.length) {
      return [placeholder, ...groups.value]
    }

    const insertBefore = groups.value.findIndex((g) => {
      const rank = staticOrder.findIndex((s) => s.bookId === g.bookId)
      return rank !== -1 && rank > pinnedRank
    })

    if (insertBefore === -1) {
      return [...groups.value, placeholder]
    }

    const result = [...groups.value]
    result.splice(insertBefore, 0, placeholder)
    return result
  })

  return {
    groups,
    groupsForDisplay,
    filterGroups,
    staticFilterGroups,
    loading,
    staticFilterGroupsLoaded,
    ensureStaticFilterGroupsLoaded,
  }
}
