import { computed, nextTick, onScopeDispose, ref, watch } from 'vue'
import { getCommentaryLinksForSourceLineRange, getLineContents } from '@/webview-host/seforimApi'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { isCommentaryItemVisible } from '../bookViewTypes'
import type { CommentaryVisibilityItem } from '../bookViewTypes'
import { isCommentaryBookUnchecked } from './uncheckedCommentaryBooks'
import {
  ensureConnectionTypeNamesLoaded,
  getPrimaryConnectionType,
  isStaticFilterConnectionType,
} from './commentaryConnectionTypes'
import {
  buildCommentaryGroupsFromCombined,
  buildStaticCommentaryFilterGroups,
  fetchSourceEntriesViaReverseQuery,
  commentaryDisplayTitle,
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

/**
 * Identity of a group within a load. A single book yields one group per
 * connection type / category (SOURCE, TARGUM, COMMENTARY·ראשונים …), each with
 * its own line subset — so bookId alone does NOT identify a group, and anything
 * keyed by it silently collapses those groups together.
 */
export function commentaryGroupKey(group: {
  bookId: number
  sectionLabel?: string
  subSectionLabel?: string
}): string {
  return `${group.bookId}::${group.sectionLabel ?? ''}::${group.subSectionLabel ?? ''}`
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

// Static filter groups are a per-book constant — which books EVER comment on /
// translate / source this book — derived from three link-table scans that cost
// seconds on heavily-linked books (a gemara volume has ~75k inbound links).
// Cached at MODULE level so a tab switch back to the same book never re-runs
// the scans. The PROMISE is cached (not just the result) so a remount that
// races a still-in-flight build reuses it instead of re-issuing the queries;
// rejected entries are dropped so a transient service error retries next time.
// The seforim DB is fixed for the page's lifetime (a DB swap reloads the page),
// so there is no invalidation.
const staticFilterGroupsByBook = new Map<number, Promise<CommentaryGroup[]>>()

/**
 * Loads the commentary of the current line. ONE instance per book view, shared by
 * both commentary panels: with a single anchor line the two panels would issue
 * byte-identical queries, and commentary is the app's heaviest payload.
 *
 * Everything that differs per panel - pin, filter, scroll, search - is layered on
 * top of `groups` by `useGroupsForDisplay` / `filterVisibleGroups` below.
 */
export function useCommentary(
  selectedLineId: () => number | null,
  selectedLineIds: () => number[] | null = () => null,
  sourceBookId: () => number | undefined = () => undefined,
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

      const [rows, sourceEntries] = await Promise.all([
        forwardQueryPromise,
        Promise.all([
          booksDataStore.ensureLoaded(),
          booksDataStore.ensureCommentaryMetadataLoaded(),
          ensureConnectionTypeNamesLoaded(),
        ]).then(() =>
          fetchSourceEntriesViaReverseQuery(lineIdsForReverse, booksDataStore.allBooksMap),
        ),
      ])

      if (!rows.length && !sourceEntries.length) return

      const built = await buildCommentaryGroupsFromCombined(
        rows,
        sourceEntries,
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

    let pending = staticFilterGroupsByBook.get(bookId)
    if (!pending) {
      pending = buildStaticCommentaryFilterGroups(bookId, booksDataStore.allBooksMap)
      staticFilterGroupsByBook.set(bookId, pending)
      pending.catch(() => staticFilterGroupsByBook.delete(bookId))
    }
    const nextGroups = await pending
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

  // Stop the display-order content backfill when this instance's component
  // unmounts (tab switch away): backfillLineContents checks loadedForLineId
  // before every batch. Without this it kept fetching large batches for a dead
  // panel, and the NEXT mount's restore queries queued behind that zombie
  // traffic (dev: the browser's 6-connection limit; hosted: the shared bridge).
  onScopeDispose(() => {
    loadedForLineId = null
  })

  // Lazy — called by useBookView when the related-books dropdown or commentary filter
  // panel first opens. Safe to call multiple times; staticFilterGroupsByBook prevents
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

  return {
    groups,
    filterGroups,
    staticFilterGroups,
    loading,
    loadError,
    staticFilterGroupsLoaded,
    ensureStaticFilterGroupsLoaded,
    requestContentPriority,
  }
}

/**
 * One panel's view of the loaded commentary: the shared groups, plus a placeholder
 * for THIS panel's pinned book when the current line has no commentary from it, so
 * the reader still sees the book they were following instead of a silent jump.
 *
 * Per panel rather than per book view: the two commentary panels pin different
 * books (and start on different default commentators), so each needs its own list.
 */
export function useGroupsForDisplay(
  groups: () => CommentaryGroup[],
  pinnedBookId: () => number | null,
  staticFilterGroups: () => CommentaryGroup[],
  loading: () => boolean,
  selectedLineId: () => number | null,
) {
  const booksDataStore = useBooksDataStore()

  return computed<CommentaryGroup[]>(() => {
    const pinned = pinnedBookId()
    const currentGroups = groups()
    if (
      !pinned ||
      selectedLineId() == null ||
      loading() ||
      currentGroups.some((g) => g.bookId === pinned)
    )
      return currentGroups

    const book = booksDataStore.allBooksMap.get(pinned)
    // Must match how commentaryGroupBuilder titles the real group this placeholder stands in for.
    const bookTitle = book?.title ? commentaryDisplayTitle(book.title) : String(pinned)

    // staticFilterGroups is the canonical ordering of every book that ever links
    // to this one, so it decides where the placeholder slots in.
    const staticOrder = staticFilterGroups()
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

    if (pinnedRank === -1 || !staticOrder.length) return [placeholder, ...currentGroups]

    const insertBefore = currentGroups.findIndex((g) => {
      const rank = staticOrder.findIndex((s) => s.bookId === g.bookId)
      return rank !== -1 && rank > pinnedRank
    })
    if (insertBefore === -1) return [...currentGroups, placeholder]

    const result = [...currentGroups]
    result.splice(insertBefore, 0, placeholder)
    return result
  })
}

/**
 * The groups one panel actually renders: its own check-tree exclusions first, then
 * its own filter-tree search result.
 *
 * Shared deliberately between CommentaryView (what it draws) and that panel's
 * in-panel search (what a flat index means), so the two can never disagree about
 * which rows exist - they used to, because search scanned the unfiltered groups.
 */
export function filterVisibleGroups(
  groups: CommentaryGroup[],
  scopeKey: string,
  visibilityList: CommentaryVisibilityItem[],
): CommentaryGroup[] {
  // Unchecked books/categories are excluded unconditionally - this applies even
  // when the filter tree was never opened for this panel, and the section rules
  // cover books that first appear on a later line.
  const base = groups.filter(
    (group) =>
      !isCommentaryBookUnchecked(
        scopeKey,
        group.sectionLabel ?? '',
        group.subSectionLabel ?? '',
        group.bookId,
      ),
  )
  if (!visibilityList.length) return base
  const visibleKeys = new Set(visibilityList.filter(isCommentaryItemVisible).map(commentaryGroupKey))
  return base.filter((group) => visibleKeys.has(commentaryGroupKey(group)))
}
