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
  // If navigating to a specific TOC entry, scroll state is already known — no need to wait for IDB
  const scrollStateReady = ref(openTocLineIndex != null)

  async function restore() {
    if (bookId == null) {
      scrollStateReady.value = true
      return
    }

    const [bookSaved, lastRead] = await Promise.all([
      tabStore.getBookViewState(tabId, bookId),
      tabStore.getLastReadPos(bookId),
    ])

    const restoredLineId = bookSaved?.selectedLineId ?? lastRead?.selectedLineId
    const si = bookSaved?.commentaryScrollIndex ?? lastRead?.commentaryScrollIndex
    const so = bookSaved?.commentaryScrollOffset ?? lastRead?.commentaryScrollOffset

    if (bookSaved?.zoom != null) bookViewStore.setZoom(tabId, bookId, bookSaved.zoom)
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

    scrollStateReady.value = true

    if (restoredLineId != null) {
      selectedLineId.value = restoredLineId
      commentaryLineId.value = restoredLineId
      bottomVisible.value = true
    }

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

  return { initialLineIndex, initialScrollTop, initialScrollOffset, scrollStateReady, restore }
}
