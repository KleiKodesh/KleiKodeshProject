/**
 * Lifecycle of ONE commentary panel: visibility, and scroll position save/restore
 * across the panel being closed, reopened, or remounted by a layout change.
 *
 * The book view runs two of these - one per CommentarySlot - so nothing here may
 * reach for shared book-view state. Where the old single-panel version closed the
 * side filter panel directly, it now calls `onHidden` and lets the caller decide
 * whether that filter panel belonged to this slot.
 *
 * Scroll position is kept as a flat virtualizer index + offset so it survives the
 * panel being unmounted (v-if) and remounted.
 */
import { ref, watch, nextTick } from 'vue'
import type { PinnedCommentaryGroup } from './bookViewTypes'

type CommentaryGroup = { bookId: number; bookTitle: string; sectionLabel?: string; subSectionLabel?: string }

type CommentaryViewInstance = {
  captureScrollPos?: () => { scrollIndex: number; scrollOffset: number } | null
  restoreCommentaryScrollPos: (index: number, offset: number) => Promise<void>
  claimRestoreIntent?: () => void
  scrollToGroup: (bookId: number, sectionLabel?: string, subSectionLabel?: string, reason?: string) => void
}

export function useBookViewCommentaryPanel(
  commentaryViewRef: () => CommentaryViewInstance | null,
  groups: import('vue').Ref<CommentaryGroup[]>,
  commentaryLoading: import('vue').Ref<boolean>,
  pinnedCommentaryGroup: import('vue').Ref<PinnedCommentaryGroup | null>,
  selectedLineId: import('vue').Ref<number | null>,
  commentaryLineId: import('vue').Ref<number | null>,
  lines: () => { content: string | null }[],
  hasCommentaries: import('vue').Ref<boolean>,
  ensureStaticFilterGroupsLoaded: () => void,
  /** Called whenever this panel goes from shown to hidden, for any reason. */
  onHidden: () => void = () => {},
) {
  const commentaryVisible = ref(false)
  const commentaryScrollIndex = ref<number | null>(null)
  const commentaryScrollOffset = ref<number | null>(null)

  // Tracks the scroll position that was last successfully restored so that
  // onCommentaryPanelMounted does not redundantly restore the same position again
  // when the panel remounts after a layout change.
  let lastRestoredCommentaryKey: string | null = null

  function onCommentaryScroll(scrollIndex: number, scrollOffset: number) {
    commentaryScrollIndex.value = scrollIndex
    commentaryScrollOffset.value = scrollOffset
  }

  /**
   * Called when this panel's CommentaryView mounts (v-if becomes true) or remounts
   * after a layout change.
   *
   * Responsibilities:
   * 1. Ensure static filter groups are loaded (may be a no-op if already loaded).
   * 2. Sync commentaryLineId from selectedLineId when the panel first opens after
   *    session restore (the panel was visible in IDB but commentaryLineId is
   *    still null because lines haven't loaded yet).
   * 3. Restore the saved scroll position, or scroll to the pinned group if no
   *    saved position exists.
   */
  function onCommentaryPanelMounted() {
    if (!commentaryVisible.value) return
    void ensureStaticFilterGroupsLoaded()

    // Sync commentaryLineId from selectedLineId when a commentary panel first
    // opens after session restore (visible=true but commentaryLineId is still
    // null because lines haven't loaded yet).
    if (selectedLineId.value != null && commentaryLineId.value == null) {
      let stop: (() => void) | undefined
      stop = watch(
        () => lines().some((l) => l.content !== null),
        (hasContent) => {
          if (!hasContent) return
          stop?.()
          if (commentaryVisible.value && selectedLineId.value != null && commentaryLineId.value == null)
            commentaryLineId.value = selectedLineId.value
        },
        { immediate: true },
      )
    }

    if (commentaryScrollIndex.value != null && commentaryScrollOffset.value != null) {
      const savedScrollIndex = commentaryScrollIndex.value
      const savedScrollOffset = commentaryScrollOffset.value
      const restoreKey = `${savedScrollIndex}:${savedScrollOffset}`

      if (restoreKey === lastRestoredCommentaryKey) {
        // Position already restored - just scroll to pinned group if groups are loaded.
        if (groups.value.length > 0 && pinnedCommentaryGroup.value) {
          nextTick(() => {
            const pinned = pinnedCommentaryGroup.value
            if (pinned) commentaryViewRef()?.scrollToGroup(pinned.bookId, undefined, undefined, 'already-restored')
          })
        }
        return
      }

      let stopLoading: (() => void) | undefined
      let stopViewRef: (() => void) | undefined
      const cancelRestore = () => { stopLoading?.(); stopViewRef?.() }
      const stopVisibleGuard = watch(commentaryVisible, (visible) => {
        if (!visible) { cancelRestore(); lastRestoredCommentaryKey = null; stopVisibleGuard() }
      })

      stopLoading = watch(
        () => !commentaryLoading.value && groups.value.length > 0,
        (ready) => {
          if (!ready) return
          // A line click while commentary was loading clears the saved position
          // (see onLineSelected in useBookView) - the queued restore is stale and
          // must yield to the pinned-group jump for the newly clicked line.
          if (commentaryScrollIndex.value == null) { cancelRestore(); return }
          stopLoading?.()
          const viewRef = commentaryViewRef()
          if (viewRef) {
            // Claim restore intent SYNCHRONOUSLY, before the nextTick below. This
            // reload also wakes setupGroupReloadScroll's watcher; without the
            // synchronous claim the two race and the panel can land on the pinned
            // group instead of the saved position.
            viewRef.claimRestoreIntent?.()
            nextTick(async () => {
              await viewRef.restoreCommentaryScrollPos(savedScrollIndex, savedScrollOffset)
              lastRestoredCommentaryKey = restoreKey
            })
          } else {
            stopViewRef = watch(
              () => commentaryViewRef(),
              (newRef) => {
                if (!newRef) return
                stopViewRef?.()
                newRef.claimRestoreIntent?.()
                nextTick(async () => {
                  await newRef.restoreCommentaryScrollPos(savedScrollIndex, savedScrollOffset)
                  lastRestoredCommentaryKey = restoreKey
                })
              },
            )
          }
        },
        { flush: 'post', immediate: true },
      )
    } else if (groups.value.length > 0 && pinnedCommentaryGroup.value) {
      // No saved scroll position - scroll to pinned group (e.g. after a layout change).
      nextTick(() => {
        const pinned = pinnedCommentaryGroup.value
        if (pinned) commentaryViewRef()?.scrollToGroup(pinned.bookId, undefined, undefined, 'panel-mounted')
      })
    }
  }

  // flush: 'post' - runs after Vue has flushed the DOM so the commentary panel is
  // painted before any reactive side-effects (metadata load, commentaryLineId set,
  // scroll restore) begin. Without this the 'pre' flush meant everything ran before
  // the panel's slot appeared, causing a visible hang.
  watch(commentaryVisible, (visible) => {
    if (!visible) {
      lastRestoredCommentaryKey = null
      onHidden()
      return
    }
    setTimeout(() => onCommentaryPanelMounted(), 0)
  }, { flush: 'post' })

  watch(hasCommentaries, (has) => {
    if (!has) commentaryVisible.value = false
  })

  return {
    commentaryVisible,
    commentaryScrollIndex,
    commentaryScrollOffset,
    onCommentaryScroll,
  }
}
