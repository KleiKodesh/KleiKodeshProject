/**
 * Restores per-book view state from IDB on mount:
 * scroll position, selected line, commentary scroll, zoom, commentary mode,
 * divider fraction, auto-sync setting, and commentary filter.
 *
 * Also exposes the initial scroll refs that BookViewLinesContent needs before
 * the IDB read completes.
 */
import { ref } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { Ref } from 'vue'
import type { CommentaryTreeState } from './bookViewTypes'
import { isCommentaryBookUnchecked } from './commentary/uncheckedCommentaryBooks'

export function useBookViewSessionRestore(
  tabId: string,
  bookId: number | undefined,
  openTocLineIndex: number | undefined,
  commentaryVisible: Ref<boolean>,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  commentaryTreeState: CommentaryTreeState,
  // Seeds the live commentary-scroll refs in useBookViewCommentaryPanel with the
  // persisted position. onCommentaryPanelMounted reads them and owns the actual
  // restore; seeding also makes hasSavedScrollPos true so setupGroupReloadScroll
  // defers to the restore instead of jumping to the pinned group on tab switch.
  seedSavedScrollPos: (index: number, offset: number) => void = () => {},
) {
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()

  const initialLineIndex = ref<number | undefined>(openTocLineIndex)
  const initialScrollTop = ref<number | undefined>()
  const initialScrollOffset = ref<number>(0)
  const scrollStateReady = ref(true)
  const idbResolved = ref(false)

  let _restoredSi: number | null | undefined
  let _restoredSo: number | null | undefined
  let _restoredCommentaryMode: 'off' | 'bottom' | 'side' | undefined
  let _restoredCommentaryFraction: number | undefined
  let _restoredStackedCommentaryFraction: number | undefined
  let _restoredPinnedCommentaryGroup: import('./bookViewTypes').PinnedCommentaryGroup | null | undefined

  const _idbPromise: Promise<void> = bookId == null
    ? Promise.resolve()
    : (() => {
        return Promise.all([
          tabStore.getBookViewState(tabId, bookId),
          tabStore.getLastReadPos(bookId),
        ]).then(([bookSaved, lastRead]) => {
          const result = _applyRestoreData(bookSaved ?? null, lastRead ?? null)
          _restoredSi = result.si
          _restoredSo = result.so
          _restoredCommentaryMode = result.commentaryMode
          _restoredCommentaryFraction = result.commentaryFraction
          _restoredStackedCommentaryFraction = result.stackedCommentaryFraction
          _restoredPinnedCommentaryGroup = result.pinnedCommentaryGroup
        })
      })()

  _idbPromise.then(() => { idbResolved.value = true })

  function _applyRestoreData(
    bookSaved: Awaited<ReturnType<typeof tabStore.getBookViewState>>,
    lastRead: Awaited<ReturnType<typeof tabStore.getLastReadPos>>,
  ) {
    // Per-tab state wins; the per-book lastRead record is always the fallback, so
    // opening a book in a brand-new tab resumes where the user left off.
    const restoredLineId = bookSaved?.selectedLineId ?? lastRead?.selectedLineId
    const si = bookSaved?.commentaryScrollIndex ?? lastRead?.commentaryScrollIndex
    const so = bookSaved?.commentaryScrollOffset ?? lastRead?.commentaryScrollOffset

    if (bookSaved?.zoom != null) bookViewStore.setLinesZoom(tabId, bookId!, bookSaved.zoom)
    if (bookSaved?.commentaryZoom != null) bookViewStore.setCommentaryZoom(tabId, bookId!, bookSaved.commentaryZoom)
    if (bookSaved?.autoSelectTopLine != null) {
      bookViewStore.autoSelectTopLine = bookSaved.autoSelectTopLine
    }

    const savedFilter =
      bookSaved?.commentaryFilterState ?? lastRead?.commentaryFilterState
    if (savedFilter) {
      commentaryTreeState.searchQuery = savedFilter.searchQuery
      commentaryTreeState.tokens = savedFilter.tokens ?? []
      // isChecked is per-tab and session-scoped (uncheckedCommentaryBooks.ts):
      // re-derive it instead of trusting the persisted value, so unchecked books
      // survive tab switches but reset on a fresh app start.
      commentaryTreeState.visibilityList = savedFilter.visibilityList.map((item) => ({
        ...item,
        isChecked: !isCommentaryBookUnchecked(tabId, item.sectionLabel, item.subSectionLabel, item.bookId),
      }))
    }

    if (openTocLineIndex == null) {
      const scrollIndex = bookSaved?.scrollIndex ?? lastRead?.scrollIndex
      const scrollOffset = bookSaved?.scrollOffset ?? lastRead?.scrollOffset
      if (scrollIndex != null) {
        initialScrollTop.value = scrollIndex
        initialScrollOffset.value = scrollOffset ?? 0
      }
    }

    // Derive commentaryMode first so we can use it to guard commentaryVisible below.
    // Prefer explicit saved value, fall back to lastRead,
    // then fall back to old saves that only have commentaryVisible (backward compat).
    const commentaryMode: 'off' | 'bottom' | 'side' | undefined =
      bookSaved?.commentaryMode ??
      lastRead?.commentaryMode ??
      (bookSaved?.commentaryVisible ? 'bottom' : undefined)

    if (restoredLineId != null) {
      selectedLineId.value = restoredLineId
      // Don't set commentaryLineId here — that would trigger a booksDataStore load
      // (GET_ALL_CATEGORIES + GET_ALL_BOOKS) before line chunks have finished loading.
      // commentaryLineId is set by useBookView when commentaryVisible first becomes true.
      // Only open the commentary panel if it was actually open when the user left.
      if (commentaryMode !== 'off') {
        commentaryVisible.value = true
      }
    }

    const commentaryFraction: number | undefined =
      bookSaved?.commentaryFraction ?? lastRead?.commentaryFraction

    const stackedCommentaryFraction: number | undefined =
      bookSaved?.stackedCommentaryFraction ?? lastRead?.stackedCommentaryFraction

    const pinnedCommentaryGroup: import('./bookViewTypes').PinnedCommentaryGroup | null | undefined =
      bookSaved?.pinnedCommentaryGroup ??
      lastRead?.pinnedCommentaryGroup ??
      // Backward compat: old saves only have pinnedCommentaryBookId (bare number)
      (bookSaved?.pinnedCommentaryBookId != null
        ? { bookId: bookSaved.pinnedCommentaryBookId, sectionLabel: '', subSectionLabel: '' }
        : lastRead?.pinnedCommentaryBookId != null
          ? { bookId: lastRead.pinnedCommentaryBookId, sectionLabel: '', subSectionLabel: '' }
          : undefined)

    return { si, so, commentaryMode, commentaryFraction, stackedCommentaryFraction, pinnedCommentaryGroup }
  }

  async function restore(): Promise<{
    commentaryMode?: 'off' | 'bottom' | 'side'
    commentaryFraction?: number
    stackedCommentaryFraction?: number
    pinnedCommentaryGroup?: import('./bookViewTypes').PinnedCommentaryGroup | null
  }> {
    if (bookId == null) return {}

    await _idbPromise

    const si = _restoredSi
    const so = _restoredSo

    if (si != null && so != null) {
      // Publish the persisted position into the live panel refs. This is the ONLY
      // thing session restore does for commentary scroll — the actual restore is
      // owned entirely by onCommentaryPanelMounted (useBookViewCommentaryPanel),
      // which fires when commentaryVisible flips true (set by _applyRestoreData
      // above), reads these seeded refs, claims restore intent, and restores.
      // Seeding also makes hasSavedScrollPos true before the first groups load,
      // so setupGroupReloadScroll defers instead of jumping to the pinned group.
      // (Seeding happens in this microtask, before the panel path's setTimeout(0),
      // so the ordering is guaranteed.)
      seedSavedScrollPos(si, so)
    }

    return {
      commentaryMode: _restoredCommentaryMode,
      commentaryFraction: _restoredCommentaryFraction,
      stackedCommentaryFraction: _restoredStackedCommentaryFraction,
      pinnedCommentaryGroup: _restoredPinnedCommentaryGroup,
    }
  }

  return { initialLineIndex, initialScrollTop, initialScrollOffset, scrollStateReady, idbResolved, restore }
}
