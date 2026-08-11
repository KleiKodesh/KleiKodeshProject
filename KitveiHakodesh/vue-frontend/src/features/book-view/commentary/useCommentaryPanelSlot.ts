/**
 * Everything ONE commentary panel owns.
 *
 * The book view builds one of these per CommentarySlot (see useBookView): a
 * 'bottom' panel stacked under the text, and a 'side' / 'side-left' column on
 * either side of it. All are anchored to the same clicked line and share one
 * `useCommentary` fetch - re-querying would be byte-identical work on the app's
 * heaviest payload - but everything downstream of the fetch is per panel:
 *
 *   - which book is pinned (and which default commentator it opens on)
 *   - the filter tree: whether it is open, its search query, tokens, visibility
 *     list and check-tree scope
 *   - scroll position, and its save/restore across close and remount
 *   - the in-panel search (Ctrl+F) and its render cache
 *   - the divider position
 *
 * That is what makes the two panels genuinely independent views of one line.
 */
import { reactive, ref, computed, watch } from 'vue'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useCommentarySearch } from './useCommentarySearch'
import { useCommentaryRender } from './useCommentaryRender'
import { useGroupsForDisplay, filterVisibleGroups } from './useCommentary'
import { commentaryScopeKey } from './uncheckedCommentaryBooks'
import { usePinnedCommentary } from '../useBookViewPinnedCommentary'
import { useBookViewCommentaryPanel } from '../useBookViewCommentaryPanel'
import type { CommentaryGroup } from './useCommentary'
import type { CommentarySlot, CommentaryTreeState } from '../bookViewTypes'
import type { Highlight } from '../lines/useBookViewHighlights'
import type { Note } from '../lines/useBookViewNotes'
import type { WordLinkAnchor } from '@/webview-host/queries.types'

/**
 * Default divider position per slot: fraction of the pane the panel takes.
 *
 * The two side columns are equal to each other but each slightly narrower than the
 * book column between them, so opening both leaves the text the widest of the three
 * (0.3 / 0.4 / 0.3). The book is what the reader is reading; the commentaries flank it.
 */
const DEFAULT_FRACTION: Record<CommentarySlot, number> = {
  bottom: 0.5,
  side: 0.3,
  'side-left': 0.3,
}

/**
 * Which of the book's default commentators each panel opens on. Opening all three
 * panels on a book with several defaults then shows three different commentators.
 */
const DEFAULT_COMMENTATOR_RANK: Record<CommentarySlot, number> = { bottom: 0, side: 1, 'side-left': 2 }

/**
 * Whether a panel falls back to the FIRST default commentator when the book has
 * no default at its own rank.
 *
 * The two original panels do: a book with one default opens that commentator in
 * both. The left panel does not - a third panel showing a third copy of the same
 * commentator is noise, so with no third default it stays unpinned and simply
 * renders the commentary list from the top without scrolling anywhere.
 */
const FALLS_BACK_TO_FIRST_DEFAULT: Record<CommentarySlot, boolean> = {
  bottom: true,
  side: true,
  'side-left': false,
}

type CommentaryViewInstance = {
  topVisibleFlatIndex: number
  activeBookId: number | null
  activePinnedGroup: { bookId: number; sectionLabel: string; subSectionLabel: string } | null
  getFilterButtonEl?: () => HTMLElement | null
  scrollToGroup: (bookId: number, sectionLabel?: string, subSectionLabel?: string, reason?: string) => void
  scrollToFlatIndex: (index: number, occurrence?: number) => void
  captureScrollPos?: () => { scrollIndex: number; scrollOffset: number } | null
  restoreCommentaryScrollPos: (index: number, offset: number) => Promise<boolean>
  claimRestoreIntent?: () => void
}

/** The fetch layer and line-level annotations both panels read from. */
export interface SharedCommentaryDeps {
  bookId: number | undefined
  groups: import('vue').Ref<CommentaryGroup[]>
  staticFilterGroups: import('vue').Ref<CommentaryGroup[]>
  loading: import('vue').Ref<boolean>
  selectedLineId: import('vue').Ref<number | null>
  commentaryLineId: import('vue').Ref<number | null>
  hasCommentaries: import('vue').Ref<boolean>
  lines: () => { content: string | null }[]
  ensureStaticFilterGroupsLoaded: () => void
  getHighlightsForLine: (lineId: number) => Highlight[]
  getNotesForLine: (lineId: number) => Note[]
  getWordLinkAnchorsForLine: (lineId: number) => WordLinkAnchor[]
}

export function useCommentaryPanelSlot(
  slot: CommentarySlot,
  tabId: string,
  viewRef: () => CommentaryViewInstance | null,
  shared: SharedCommentaryDeps,
) {
  // Scopes this panel's virtual check-tree. Both slots of a tab share the prefix
  // so tab teardown clears them together (see uncheckedCommentaryBooks).
  const scopeKey = commentaryScopeKey(tabId, slot)

  const bookViewStore = useBookViewStore()
  // Per panel: each panel zooms independently (Ctrl+scroll inside its scroller).
  const getCommentaryZoom = () =>
    shared.bookId != null ? bookViewStore.getCommentaryZoom(tabId, shared.bookId, slot) : 100

  const treeState = reactive<CommentaryTreeState>({ searchQuery: '', tokens: [], visibilityList: [] })
  const fraction = ref(DEFAULT_FRACTION[slot])

  const { pinnedCommentaryGroup, restorePin, setPendingPin, pinExplicitly } = usePinnedCommentary(
    shared.bookId,
    () => shared.commentaryLineId.value,
    () => shared.groups.value,
    DEFAULT_COMMENTATOR_RANK[slot],
    FALLS_BACK_TO_FIRST_DEFAULT[slot],
  )

  const groupsForDisplay = useGroupsForDisplay(
    () => shared.groups.value,
    () => pinnedCommentaryGroup.value?.bookId ?? null,
    () => shared.staticFilterGroups.value,
    () => shared.loading.value,
    () => shared.selectedLineId.value,
  )

  // Exactly the rows this panel renders. The panel's search scans this same list,
  // so a flat index means the same row in both.
  const visibleGroups = computed(() =>
    filterVisibleGroups(groupsForDisplay.value, scopeKey, treeState.visibilityList),
  )

  const panel = useBookViewCommentaryPanel(
    viewRef,
    shared.groups,
    shared.loading,
    pinnedCommentaryGroup,
    shared.selectedLineId,
    shared.commentaryLineId,
    shared.lines,
    shared.hasCommentaries,
    shared.ensureStaticFilterGroupsLoaded,
  )

  // ── This panel's filter tree ────────────────────────────────────────────────
  // Every panel owns its own tree, so all of them can be open at once and none
  // can re-target another's. Where the trees used to share the book view's side
  // panel (and so also fought the TOC for it), each is now a dropdown belonging to
  // one panel: clipped to its own column for the side slots, full body height for
  // 'bottom' (which is why BookViewPage renders that one - see CommentaryPanelHost).
  const filterOpen = ref(false)

  function toggleFilter() {
    // A closed panel has no tree to filter. Same guard the shared side panel had.
    if (!panel.commentaryVisible.value) return
    filterOpen.value = !filterOpen.value
    // The full book list is only worth fetching once a tree actually opens.
    if (filterOpen.value) shared.ensureStaticFilterGroupsLoaded()
  }

  function closeFilter() {
    filterOpen.value = false
  }

  // Closing the panel takes its tree with it, whatever closed it: the header's
  // close button, a line with no commentary at all (hasCommentaries), or a pane
  // narrowing past the side columns' minimum width. All three funnel through
  // commentaryVisible, so watching it covers every path - an orphaned tree with
  // no panel beside it was the failure mode worth designing out.
  watch(panel.commentaryVisible, (visible) => {
    if (!visible) filterOpen.value = false
  })

  // Per panel: the render cache is keyed partly by the active search query, and
  // the two panels search independently - one shared cache would thrash.
  const { commentaryFontPx, renderContent, setCurrentMark } = useCommentaryRender(
    () => groupsForDisplay.value,
    getCommentaryZoom,
    shared.getHighlightsForLine,
    shared.getNotesForLine,
    shared.getWordLinkAnchorsForLine,
  )

  const search = useCommentarySearch(
    () => visibleGroups.value,
    () => viewRef()?.topVisibleFlatIndex ?? 0,
  )

  return {
    slot,
    scopeKey,
    treeState,
    filterOpen,
    toggleFilter,
    closeFilter,
    fraction,
    pinnedCommentaryGroup,
    restorePin,
    setPendingPin,
    pinExplicitly,
    groupsForDisplay,
    visibleGroups,
    commentaryFontPx,
    renderContent,
    setCurrentMark,
    search,
    visible: panel.commentaryVisible,
    scrollIndex: panel.commentaryScrollIndex,
    scrollOffset: panel.commentaryScrollOffset,
    onScroll: panel.onCommentaryScroll,
  }
}

export type CommentaryPanel = ReturnType<typeof useCommentaryPanelSlot>
