import { computed, nextTick, ref, watch } from 'vue'
import { getCommentaryLinksForSourceLineRange, getLineContents } from '@/webview-host/seforimApi'
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
  // Set when load() fails (DB/bridge error, e.g. a seforim DB missing the links
  // tables) — the panel shows an error message instead of a silent empty state.
  const loadError = ref(false)
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
  // Signature of the exact line-ID set the current groups were loaded for. Lets the
  // fallback watcher below detect when the effective query changes (e.g. ctrl-click
  // extends the manual selection while commentaryLineId — the anchor — stays put).
  let loadedIdsSignature: string | null = null

  function idsSignature(lineId: number, ids: number[] | null): string {
    return ids != null && ids.length > 0 ? ids.join(',') : String(lineId)
  }

  // Content backfill batch sizes. The first batch covers roughly what fits on
  // screen so text paints as fast as possible after the structure; later batches
  // are large so a full chumash-chapter section (~8k lines) completes in a
  // handful of round trips.
  const CONTENT_FIRST_BATCH = 100
  const CONTENT_BACKFILL_BATCH = 1500

  // lineIds whose content fetch is done or in flight for the CURRENT selection —
  // shared bookkeeping between the display-order backfill and the viewport-driven
  // priority fetch so no line is ever requested twice. Reset on every load().
  const contentRequested = new Set<number>()

  /** Lines from the links-only query that still need their text, in display order. */
  function collectPendingLines(builtGroups: CommentaryGroup[]): CommentaryLine[] {
    const pending: CommentaryLine[] = []
    const seen = new Set<number>()
    for (const group of builtGroups) {
      for (const line of group.lines) {
        if (line.lineId > 0 && line.content === '' && !seen.has(line.lineId)) {
          seen.add(line.lineId)
          pending.push(line)
        }
      }
    }
    return pending
  }

  /** Fetches content for one batch of lines and writes it into every matching line object. */
  async function fetchContentsInto(builtGroups: CommentaryGroup[], batch: CommentaryLine[]) {
    for (const line of batch) contentRequested.add(line.lineId)
    const rows = await getLineContents(batch.map((line) => line.lineId))
    const contentById = new Map(rows.map((row) => [row.id, row.content ?? '']))
    for (const group of builtGroups) {
      for (const line of group.lines) {
        const content = contentById.get(line.lineId)
        if (content !== undefined && line.content === '') line.content = content
      }
    }
  }

  /**
   * Viewport-driven priority fetch: called by CommentaryView whenever virtual items
   * render lines whose content is still pending (scroll restore, fast scroll ahead
   * of the backfill, jump-to-group). Fetches exactly those lines immediately so the
   * viewport never waits for the display-order backfill to reach it.
   */
  function requestContentPriority(lineIds: number[]) {
    const currentGroups = groups.value
    if (!currentGroups.length) return
    const wanted = new Set(lineIds.filter((id) => id > 0 && !contentRequested.has(id)))
    if (!wanted.size) return
    const batch: CommentaryLine[] = []
    for (const group of currentGroups) {
      for (const line of group.lines) {
        if (wanted.has(line.lineId) && line.content === '') {
          wanted.delete(line.lineId)
          batch.push(line)
        }
      }
    }
    if (batch.length) void fetchContentsInto(currentGroups, batch).catch(() => {})
  }

  /**
   * Fills line.content for groups built from the links-only range query.
   * Batches run sequentially in display order so the top of the panel fills first
   * and the bridge is never flooded. Mutates the (reactive) line objects in place —
   * safe if the selection changes mid-flight because stale groups are not displayed.
   */
  async function backfillLineContents(builtGroups: CommentaryGroup[], forLineId: number) {
    const pending = collectPendingLines(builtGroups)
    for (let i = 0; i < pending.length; i += CONTENT_BACKFILL_BATCH) {
      // Stop early if the user has moved on to a different selection.
      if (loadedForLineId !== forLineId) return
      // Skip lines a viewport-priority fetch already covered in the meantime.
      const batch = pending
        .slice(i, i + CONTENT_BACKFILL_BATCH)
        .filter((line) => !contentRequested.has(line.lineId))
      if (!batch.length) continue
      try {
        await fetchContentsInto(builtGroups, batch)
      } catch {
        return // DB error — leave remaining lines empty rather than retry-looping
      }
    }
  }

  async function load(lineId: number) {
    loadedForLineId = lineId
    const multiIds = selectedLineIds()
    const isMulti = multiIds != null && multiIds.length > 0
    loadedIdsSignature = idsSignature(lineId, multiIds)
    groups.value = []
    contentRequested.clear()
    loading.value = true
    loadError.value = false
    try {
      // Fire all pre-flight work in parallel:
      // - catalog + commentary metadata (needed by allBooksMap for group building)
      // - connection type ID table (needed by reverse-lookup queries)
      // - the forward commentary query (needs no ID table — pure SQL with a fixed line param)
      // The reverse queries depend on connection type IDs so they start after that resolves,
      // but they run concurrently with each other and with the catalog awaits.
      // Two-phase load for both single-line and section clicks: the links-only,
      // JOIN-free query returns in milliseconds even for thousands of hits (the old
      // content-joining query cost 150ms-1.4s and up to 10MB per click), so group
      // structure renders immediately. Line text is backfilled below.
      const queryLineIds = isMulti ? multiIds : [lineId]
      const lineIdsForReverse = queryLineIds

      const forwardQueryPromise = getCommentaryLinksForSourceLineRange(queryLineIds)

      const [rows, sourceEntries, targumEntries] = await Promise.all([
        forwardQueryPromise,
        Promise.all([
          booksDataStore.ensureLoaded(),
          booksDataStore.ensureCommentaryMetadataLoaded(),
          ensureConnectionTypeNamesLoaded(),
        ]).then(() =>
          fetchSourceEntriesViaReverseQuery(lineIdsForReverse, booksDataStore.allBooksMap),
        ),
        Promise.all([
          booksDataStore.ensureLoaded(),
          booksDataStore.ensureCommentaryMetadataLoaded(),
          ensureConnectionTypeNamesLoaded(),
        ]).then(() =>
          fetchTargumEntriesViaReverseQuery(lineIdsForReverse, booksDataStore.allBooksMap),
        ),
      ])

      if (!rows.length && !sourceEntries.length && !targumEntries.length) return

      const built = await buildCommentaryGroupsFromCombined(
        rows,
        sourceEntries,
        targumEntries,
        booksDataStore.allBooksMap,
      )

      // Fetch the first screenful of text BEFORE revealing the panel — one small
      // query — so the panel appears once, with readable text, instead of showing
      // empty rows that fill in later (the reveal also triggers highlights/notes/
      // toc-path queries, and the first content batch would otherwise queue behind
      // them). The rest backfills in display order after the reveal.
      const pendingLines = collectPendingLines(built)
      if (pendingLines.length) {
        try {
          await fetchContentsInto(built, pendingLines.slice(0, CONTENT_FIRST_BATCH))
        } catch { /* reveal with empty text rather than blocking the panel */ }
      }
      if (loadedForLineId !== lineId) return

      groups.value = built

      // Second phase: fill in the remaining text in display order, fire-and-forget.
      // IMPORTANT: mutate through groups.value (the reactive proxy), not `built` —
      // writes to the raw array would not trigger a re-render of already-built rows.
      if (pendingLines.length > CONTENT_FIRST_BATCH) void backfillLineContents(groups.value, lineId)

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
    } catch {
      // Link/content queries failed (older or mismatched seforim DB, service
      // error). Without this catch the rejection was unhandled and the panel
      // stayed empty forever with no indication anything went wrong.
      if (loadedForLineId === lineId) loadError.value = true
    } finally {
      // Keep the spinner up if the effective ID set has already changed since this
      // load started — the fallback watcher is about to fire another load().
      const refetchImminent =
        loadedForLineId === lineId &&
        idsSignature(lineId, selectedLineIds()) !== loadedIdsSignature
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
        loadedIdsSignature = null
        groups.value = []
        loadError.value = false
        return
      }
      if (selectedLineIds() == null) await nextTick()
      if (selectedLineId() !== id) return
      void load(id)
    },
    { immediate: true },
  )

  // Re-fetch when the effective set of query line IDs changes while the anchor
  // (selectedLineId / commentaryLineId) stays the same. This covers two cases:
  //   1. selectedLineIds arriving after the initial load (lines/TOC took >1 tick).
  //   2. ctrl-click / shift-click extending the manual selection — the anchor is
  //      unchanged so watch(selectedLineId) never fires, yet the ID set differs.
  // Comparing signatures (not a loadUsedSectionRange flag) is what makes case 2
  // work: a plain click on a line inside a TOC section already loads with a
  // section range, so the old !loadUsedSectionRange guard suppressed the reload.
  watch(selectedLineIds, (ids) => {
    const lineId = selectedLineId()
    if (lineId == null || lineId !== loadedForLineId) return
    if (idsSignature(lineId, ids) === loadedIdsSignature) return
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
    loadError,
    staticFilterGroupsLoaded,
    ensureStaticFilterGroupsLoaded,
    requestContentPriority,
  }
}
