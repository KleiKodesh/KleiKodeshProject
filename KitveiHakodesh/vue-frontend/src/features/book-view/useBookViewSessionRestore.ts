/**
 * Restores per-book view state from IDB on mount:
 * scroll position, selected line, commentary scroll, zoom, bottom panel visibility,
 * auto-sync setting, and commentary filter.
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
  bottomVisible: Ref<boolean>,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  commentaryTreeState: CommentaryTreeState,
  commentaryLoading: Ref<boolean>,
  commentaryViewRef: () => CommentaryViewRef | null,
) {
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()
  const settingsStore = useSettingsStore()

  const initialLineIndex = ref<number | undefined>(openTocLineIndex)
  const initialScrollTop = ref<number | undefined>()
  const initialScrollOffset = ref<number>(0)
  // Mount immediately — don't wait for IDB. The initial scroll watcher in
  // BookViewLinesContent watches initialScrollTop reactively and will apply
  // the saved position when IDB resolves, even if that's after mount.
  const scrollStateReady = ref(true)
  // Becomes true once the IDB read has settled (with or without a saved position).
  // BookViewLinesContent uses this to know it is safe to give up waiting for a
  // restore target — before this is true, the absence of initialScrollTop does not
  // mean there is no saved position; it just means IDB hasn't responded yet.
  const idbResolved = ref(false)

  // Start IDB reads immediately at composable setup — not deferred to onMounted —
  // so they run in parallel with the DB queries rather than racing mid-stream.
  // Store the resolved values so restore() can use them without a second read.
  let _restoredSi: number | null | undefined
  let _restoredSo: number | null | undefined

  const _idbPromise: Promise<void> = bookId == null
    ? Promise.resolve()
    : Promise.all([
        tabStore.getBookViewState(tabId, bookId),
        tabStore.getLastReadPos(bookId),
      ]).then(([bookSaved, lastRead]) => {
        const result = _applyRestoreData(bookSaved ?? null, lastRead ?? null)
        _restoredSi = result.si
        _restoredSo = result.so
      })

  _idbPromise.then(() => { idbResolved.value = true })

  function _applyRestoreData(
    bookSaved: Awaited<ReturnType<typeof tabStore.getBookViewState>>,
    lastRead: Awaited<ReturnType<typeof tabStore.getLastReadPos>>,
  ) {
    const restoredLineId = bookSaved?.selectedLineId ?? lastRead?.selectedLineId
    const si = bookSaved?.commentaryScrollIndex ?? lastRead?.commentaryScrollIndex
    const so = bookSaved?.commentaryScrollOffset ?? lastRead?.commentaryScrollOffset

    if (bookSaved?.zoom != null) bookViewStore.setZoom(tabId, bookId!, bookSaved.zoom)
    if (bookSaved?.bottomVisible != null) bottomVisible.value = bookSaved.bottomVisible
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
      const scrollIndex = bookSaved?.scrollIndex ?? lastRead?.scrollIndex
      const scrollOffset = bookSaved?.scrollOffset ?? lastRead?.scrollOffset
      if (scrollIndex != null) {
        initialScrollTop.value = scrollIndex
        initialScrollOffset.value = scrollOffset ?? 0
      }
    }

    if (restoredLineId != null) {
      selectedLineId.value = restoredLineId
      // Don't set commentaryLineId here — that would trigger a booksDataStore load
      // (GET_ALL_CATEGORIES + GET_ALL_BOOKS) before line chunks have finished loading.
      // commentaryLineId is set by useBookView when bottomVisible first becomes true.
      bottomVisible.value = true
    }

    return { si, so }
  }

  async function restore() {
    if (bookId == null) return

    await _idbPromise

    const si = _restoredSi
    const so = _restoredSo

    if (si != null && so != null) {
      const stop = watch(
        commentaryLoading,
        async (loading) => {
          if (loading) return
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
  }

  return { initialLineIndex, initialScrollTop, initialScrollOffset, scrollStateReady, idbResolved, restore }
}
