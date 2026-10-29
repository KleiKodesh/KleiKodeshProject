import { computed, nextTick, onScopeDispose, ref, watch } from 'vue'
import { getCommentaryLinkGroupsForSourceLineRange, getLineContents } from '@/webview-host/seforimApi'
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

/** Every book that links to `bookId` (SOURCE / TARGUM / COMMENTARY), cached per book. */
export async function loadStaticFilterGroupsForBook(bookId: number): Promise<CommentaryGroup[]> {
  const booksDataStore = useBooksDataStore()
  await booksDataStore.ensureLoaded()
  await booksDataStore.ensureCommentaryMetadataLoaded()

  let pending = staticFilterGroupsByBook.get(bookId)
  if (!pending) {
    pending = buildStaticCommentaryFilterGroups(bookId, booksDataStore.allBooksMap)
    staticFilterGroupsByBook.set(bookId, pending)
    pending.catch(() => staticFilterGroupsByBook.delete(bookId))
  }
  return pending
}

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

  // Line text is fetched ON DEMAND only: the windowed scroller asks for the lines it is
  // about to show (ensureLineContents). Nothing downloads the panel's text in the
  // background — the panel's search has sources of its own (useCommentarySearch).

  // The fetch each requested line rides on, so ensureLineContents can await a line
  // that another window's fetch already has in flight. Reset on every load().
  const contentFetches = new Map<number, Promise<void>>()
  // Lines whose fetch has returned, with or without text: never waited for again
  // (until trimLineContents releases them).
  const contentSettled = new Set<number>()
  // Every line object carrying a line id, for the CURRENT groups. The same line can
  // sit in two groups (one book linked under two connection types), and each copy
  // gets the text. Built once per load instead of scanning the groups per id.
  let linesById = new Map<number, CommentaryLine[]>()

  function indexLines(builtGroups: CommentaryGroup[]): Map<number, CommentaryLine[]> {
    const byId = new Map<number, CommentaryLine[]>()
    for (const group of builtGroups) {
      for (const line of group.lines) {
        if (line.lineId <= 0) continue
        const list = byId.get(line.lineId)
        if (list) list.push(line)
        else byId.set(line.lineId, [line])
      }
    }
    return byId
  }

  // ── Bounded text cache ──────────────────────────────────────────────────────
  // The groups hold a line object for every linked line (a whole-book range has
  // ~50k), but only the text NEAR the scroller's window is kept: settled fetches are
  // remembered in arrival order, and once more than TEXT_BUDGET lines hold text the
  // oldest are released (content back to '', not settled) unless the panel says it
  // still shows them. A released line comes back through ensureLineContents like
  // any line the scroller has not shown yet.
  const TEXT_BUDGET = 3000
  // The newest settled batches are never released: the landing a goal or restore
  // just fetched is the newest batch, and it may still be a tick from mounting.
  const PROTECTED_NEWEST_BATCHES = 2
  let settledBatches: number[][] = []
  let settledCount = 0
  // What each panel still shows, by panel: the two panels share this instance, so a
  // trim asked by one must not release the rows the other has on screen.
  const keepByPanel = new Map<string, (lineId: number) => boolean>()

  /** Fetches content for one batch of lines and writes it into every line object with that id. */
  function fetchContentsInto(ids: number[]): Promise<void> {
    const index = linesById
    const fetching = (async () => {
      const rows = await getLineContents(ids)
      // A new load replaced the groups while this was in flight: its lines are gone,
      // and marking the ids settled would leave the new load's rows without text.
      if (linesById !== index) return
      const contentById = new Map(rows.map((row) => [row.id, row.content ?? '']))
      for (const id of ids) {
        const content = contentById.get(id)
        if (content === undefined) continue
        for (const line of linesById.get(id) ?? []) if (line.content === '') line.content = content
      }
      for (const id of ids) contentSettled.add(id)
      settledBatches.push(ids)
      settledCount += ids.length
    })()
    for (const id of ids) {
      contentFetches.set(id, fetching)
    }
    void fetching.catch(() => {}).finally(() => {
      for (const id of ids) if (contentFetches.get(id) === fetching) contentFetches.delete(id)
    })
    return fetching
  }

  // One request per this many lines: a select-all copy asks for every line at once.
  const FETCH_CHUNK = 500

  /**
   * Resolves once every one of these lines has its text (or is known to have
   * none): the windowed scroller's row source. It asks for a range BEFORE mounting
   * it, so a row is never on screen without its text. Lines another fetch already
   * has in flight are awaited, not requested again. Rejects when a fetch fails, so
   * the scroller mounts nothing rather than empty rows.
   */
  async function ensureLineContents(lineIds: number[]): Promise<void> {
    if (!linesById.size) return
    const waits: Promise<void>[] = []
    const wanted: number[] = []
    const seen = new Set<number>()
    for (const id of lineIds) {
      if (id <= 0 || contentSettled.has(id) || seen.has(id)) continue
      seen.add(id)
      const inFlight = contentFetches.get(id)
      if (inFlight) waits.push(inFlight)
      else if (linesById.has(id)) wanted.push(id)
    }
    for (let i = 0; i < wanted.length; i += FETCH_CHUNK) {
      waits.push(fetchContentsInto(wanted.slice(i, i + FETCH_CHUNK)))
    }
    if (waits.length) await Promise.all(waits)
  }

  /**
   * Records what `panel` still shows (its mounted and pinned rows; null when the
   * panel unmounts), then releases the text of the lines fetched longest ago until
   * the held text is within budget. A line either panel still shows is never
   * released, whatever its age. Called by a panel after its window moves; a no-op
   * while the held text is within budget.
   */
  function trimLineContents(panel: string, keep: ((lineId: number) => boolean) | null): void {
    if (keep) keepByPanel.set(panel, keep)
    else keepByPanel.delete(panel)
    if (!keep || settledCount <= TEXT_BUDGET) return
    const keeps = [...keepByPanel.values()]
    const shown = (lineId: number) => keeps.some((k) => k(lineId))
    const survivors: number[][] = []
    let count = settledCount
    const lastEvictable = settledBatches.length - PROTECTED_NEWEST_BATCHES
    for (let b = 0; b < settledBatches.length; b++) {
      const batch = settledBatches[b]!
      if (count <= TEXT_BUDGET || b >= lastEvictable) {
        survivors.push(batch)
        continue
      }
      const kept: number[] = []
      for (const id of batch) {
        if (shown(id)) {
          kept.push(id)
          continue
        }
        contentSettled.delete(id)
        for (const line of linesById.get(id) ?? []) line.content = ''
        count--
      }
      if (kept.length) survivors.push(kept)
    }
    settledBatches = survivors
    settledCount = count
  }

  function resetContents() {
    contentFetches.clear()
    contentSettled.clear()
    settledBatches = []
    settledCount = 0
    linesById = new Map()
  }

  async function load(lineId: number) {
    loadedForLineId = lineId
    const multiIds = selectedLineIds()
    const isMulti = multiIds != null && multiIds.length > 0
    loadedIdsSignature = idsSignature(lineId, multiIds)
    groups.value = []
    resetContents()
    loading.value = true
    loadError.value = false
    try {
      // Fire all pre-flight work in parallel:
      // - catalog + commentary metadata (needed by allBooksMap for group building)
      // - connection type ID table (needed by reverse-lookup queries)
      // - the forward commentary query (needs no ID table — pure SQL with a fixed line param)
      // The reverse queries depend on connection type IDs so they start after that resolves,
      // but they run concurrently with each other and with the catalog awaits.
      // Two-phase load for both single-line and section clicks: the links-only
      // query returns ids and line indexes grouped per (book, type) — a whole-book
      // range is a few hundred KB (the per-row shape was ~4 MB, the old
      // content-joining query up to 10 MB per click) — so the group structure
      // renders at once. Line text is fetched for the rows shown.
      const queryLineIds = isMulti ? multiIds : [lineId]
      const lineIdsForReverse = queryLineIds

      const forwardQueryPromise = getCommentaryLinkGroupsForSourceLineRange(queryLineIds)

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

      if (loadedForLineId !== lineId) return

      // The structure (which commentators, which lines) is all the panel needs to
      // open: the windowed scroller asks for exactly the lines it will show, before
      // it shows them. The line index is built from the REACTIVE line objects, so a
      // fetch writes text the rows see.
      groups.value = built
      linesById = indexLines(groups.value)
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
    const nextGroups = await loadStaticFilterGroupsForBook(bookId)
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
        resetContents()
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

  // A load still in flight when this instance's component unmounts (tab switch away)
  // must not publish into a dead panel: load() checks loadedForLineId after its awaits.
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
    ensureLineContents,
    trimLineContents,
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
  /**
   * Whether staticFilterGroups has finished loading. The placeholder's POSITION
   * comes from that ordering, so injecting one before it lands puts the row at
   * index 0 and then moves it when the real order arrives - two reshuffles of
   * every row below it, while a restore is chasing its saved scrollIndex through
   * them. That is the "it shows a placeholder and then jumps to the saved
   * position" flicker. Better to inject nothing for those few ms: a missing row
   * settles into place, a misplaced one drags the whole list twice.
   */
  staticFilterGroupsLoaded: () => boolean = () => true,
) {
  const booksDataStore = useBooksDataStore()

  return computed<CommentaryGroup[]>(() => {
    const pinned = pinnedBookId()
    const currentGroups = groups()
    if (
      !pinned ||
      selectedLineId() == null ||
      loading() ||
      !staticFilterGroupsLoaded() ||
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
