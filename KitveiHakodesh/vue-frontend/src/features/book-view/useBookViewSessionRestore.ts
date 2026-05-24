/**
 * Restores per-book view state from IDB on mount:
 * scroll position, selected line, commentary scroll, zoom, commentary mode,
 * divider fraction, auto-sync setting, and commentary filter.
 *
 * Also exposes the initial scroll refs that BookViewLinesContent needs before
 * the IDB read completes.
 */
import { ref, watch, nextTick } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import type { Ref } from 'vue'
import type { CommentaryTreeState } from './bookViewTypes'

interface CommentaryViewRef {
  restoreCommentaryScrollPos: (index: number, offset: number) => void
}

export function useBookViewSessionRestore(
  tabId: string,
  bookId: number | undefined,
  openTocLineIndex: number | undefined,
  commentaryVisible: Ref<boolean>,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  commentaryTreeState: CommentaryTreeState,
  commentaryLoading: Ref<boolean>,
  commentaryViewRef: () => CommentaryViewRef | null,
  commentaryGroups: () => { length: number },
) {
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()
  const settingsStore = useSettingsStore()

  const initialLineIndex = ref<number | undefined>(openTocLineIndex)
  const initialScrollTop = ref<number | undefined>()
  const initialScrollOffset = ref<number>(0)
  const scrollStateReady = ref(true)
  const idbResolved = ref(false)

  let _restoredSi: number | null | undefined
  let _restoredSo: number | null | undefined
  let _restoredCommentaryMode: 'off' | 'bottom' | 'side' | undefined
  let _restoredCommentaryFraction: number | undefined
  let _restoredPinnedCommentaryBookId: number | null | undefined

  const _idbPromise: Promise<void> = bookId == null
    ? Promise.resolve()
    : Promise.all([
        tabStore.getBookViewState(tabId, bookId),
        tabStore.getLastReadPos(bookId),
      ]).then(([bookSaved, lastRead]) => {
        const result = _applyRestoreData(bookSaved ?? null, lastRead ?? null)
        _restoredSi = result.si
        _restoredSo = result.so
        _restoredCommentaryMode = result.commentaryMode
        _restoredCommentaryFraction = result.commentaryFraction
        _restoredPinnedCommentaryBookId = result.pinnedCommentaryBookId
      })

  _idbPromise.then(() => { idbResolved.value = true })

  function _applyRestoreData(
    bookSaved: Awaited<ReturnType<typeof tabStore.getBookViewState>>,
    lastRead: Awaited<ReturnType<typeof tabStore.getLastReadPos>>,
  ) {
    // When resumeLastRead is off, only use lastRead as a fallback if there is
    // already a per-tab bookSaved entry — i.e. the user has visited this book
    // in this tab before. Opening the same book in a brand-new tab should start
    // from scratch when the setting is disabled.
    const useLastRead = settingsStore.resumeLastRead || bookSaved != null
    const restoredLineId = bookSaved?.selectedLineId ?? (useLastRead ? lastRead?.selectedLineId : undefined)
    const si = bookSaved?.commentaryScrollIndex ?? (useLastRead ? lastRead?.commentaryScrollIndex : undefined)
    const so = bookSaved?.commentaryScrollOffset ?? (useLastRead ? lastRead?.commentaryScrollOffset : undefined)

    if (bookSaved?.zoom != null) bookViewStore.setZoom(tabId, bookId!, bookSaved.zoom)
    if (bookSaved?.autoSelectTopLine != null) {
      bookViewStore.autoSelectTopLine = bookSaved.autoSelectTopLine
    }

    const savedFilter =
      bookSaved?.commentaryFilterState ??
      (settingsStore.resumeLastRead ? lastRead?.commentaryFilterState : undefined)
    if (savedFilter) {
      commentaryTreeState.searchQuery = savedFilter.searchQuery
      commentaryTreeState.tokens = savedFilter.tokens ?? []
      commentaryTreeState.visibilityList = savedFilter.visibilityList
    }

    if (openTocLineIndex == null) {
      const scrollIndex = bookSaved?.scrollIndex ?? (useLastRead ? lastRead?.scrollIndex : undefined)
      const scrollOffset = bookSaved?.scrollOffset ?? (useLastRead ? lastRead?.scrollOffset : undefined)
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
      (settingsStore.resumeLastRead ? lastRead?.commentaryMode : undefined) ??
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
      bookSaved?.commentaryFraction ??
      (settingsStore.resumeLastRead ? lastRead?.commentaryFraction : undefined)

    const pinnedCommentaryBookId: number | null | undefined =
      bookSaved?.pinnedCommentaryBookId ??
      (settingsStore.resumeLastRead ? lastRead?.pinnedCommentaryBookId : undefined)

    return { si, so, commentaryMode, commentaryFraction, pinnedCommentaryBookId }
  }

  async function restore(): Promise<{
    commentaryMode?: 'off' | 'bottom' | 'side'
    commentaryFraction?: number
    pinnedCommentaryBookId?: number | null
  }> {
    if (bookId == null) return {}

    await _idbPromise

    const si = _restoredSi
    const so = _restoredSo

    if (si != null && so != null) {
      const stop = watch(
        () => !commentaryLoading.value && commentaryGroups().length > 0,
        async (ready) => {
          if (!ready) return
          stop()
          await nextTick()
          const viewRef = commentaryViewRef()
          if (viewRef) {
            viewRef.restoreCommentaryScrollPos(si, so)
          } else {
            const stopRef = watch(
              () => commentaryViewRef(),
              (newRef) => {
                if (!newRef) return
                stopRef()
                newRef.restoreCommentaryScrollPos(si, so)
              },
            )
          }
        },
        { flush: 'sync' },
      )
    }

    return {
      commentaryMode: _restoredCommentaryMode,
      commentaryFraction: _restoredCommentaryFraction,
      pinnedCommentaryBookId: _restoredPinnedCommentaryBookId,
    }
  }

  return { initialLineIndex, initialScrollTop, initialScrollOffset, scrollStateReady, idbResolved, restore }
}
