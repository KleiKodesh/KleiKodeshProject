/**
 * Commentary panel lifecycle for the book view.
 *
 * Owns commentary panel visibility, scroll position save/restore across panel
 * open/close and layout-mode switches, and the onCommentaryPanelMounted handler
 * that wires all the pieces together when the panel appears.
 *
 * Commentary scroll position is persisted as a flat virtualizer index + offset
 * so it survives the panel being unmounted (v-if) and remounted.
 */
import { ref, watch, nextTick } from 'vue'
import type { PinnedCommentaryGroup } from './bookViewTypes'

type CommentaryGroup = { bookId: number; bookTitle: string; sectionLabel?: string; subSectionLabel?: string }

type CommentaryViewInstance = {
  captureScrollPos?: () => { scrollIndex: number; scrollOffset: number } | null
  restoreCommentaryScrollPos: (index: number, offset: number) => Promise<void>
  claimRestoreIntent?: () => void
  scrollToGroup: (bookId: number) => void
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
  sidePanelMode: import('vue').Ref<string | null>,
  closeSidePanel: () => void,
  ensureStaticFilterGroupsLoaded: () => void,
) {
  const commentaryVisible = ref(false)
  const commentaryScrollIndex = ref<number | null>(null)
  const commentaryScrollOffset = ref<number | null>(null)

  // Tracks the scroll position that was last successfully restored so that
  // onCommentaryPanelMounted does not redundantly restore the same position again
  // when the panel remounts after a layout-mode switch.
  let lastRestoredCommentaryKey: string | null = null

  function onCommentaryScroll(scrollIndex: number, scrollOffset: number) {
    commentaryScrollIndex.value = scrollIndex
    commentaryScrollOffset.value = scrollOffset
  }

  // Preserve commentary scroll position when the tree changes (items toggled or
  // search query changes) so the user does not lose their place.
  async function onCommentaryTreeChanged() {
    const savedPos = commentaryViewRef()?.captureScrollPos?.()
    await nextTick()
    if (savedPos)
      commentaryViewRef()?.restoreCommentaryScrollPos(savedPos.scrollIndex, savedPos.scrollOffset)
  }

  /**
   * Called when CommentaryView mounts (v-if becomes true) or after a layout-mode
   * switch (bottom ↔ side) that causes CommentaryView to remount.
   *
   * Responsibilities:
   * 1. Ensure static filter groups are loaded (may be a no-op if already loaded).
   * 2. Sync commentaryLineId from selectedLineId when the panel first opens after
   *    session restore (commentaryVisible was true in IDB but commentaryLineId is
   *    still null because lines haven't loaded yet).
   * 3. Restore the saved scroll position, or scroll to the pinned group if no
   *    saved position exists.
   */
  function onCommentaryPanelMounted() {
    if (!commentaryVisible.value) return
    void ensureStaticFilterGroupsLoaded()

    // Sync commentaryLineId from selectedLineId when the commentary panel first
    // opens after session restore (commentaryVisible=true but commentaryLineId
    // is still null because lines haven't loaded yet).
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
        // Position already restored — just scroll to pinned group if groups are loaded.
        if (groups.value.length > 0 && pinnedCommentaryGroup.value) {
          nextTick(() => {
            const pinned = pinnedCommentaryGroup.value
            if (pinned) commentaryViewRef()?.scrollToGroup(pinned.bookId)
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
          // (see onLineSelected in useBookView) — the queued restore is stale and
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
      // No saved scroll position — scroll to pinned group (e.g. layout-mode switch).
      nextTick(() => {
        const pinned = pinnedCommentaryGroup.value
        if (pinned) commentaryViewRef()?.scrollToGroup(pinned.bookId)
      })
    }
  }

  // flush: 'post' — runs after Vue has flushed the DOM so the commentary panel is
  // painted before any reactive side-effects (metadata load, commentaryLineId set,
  // scroll restore) begin. Without this the 'pre' flush meant everything ran before
  // the SplitPane bottom slot appeared, causing a visible hang.
  watch(commentaryVisible, (visible) => {
    if (!visible && sidePanelMode.value === 'commentary-tree') closeSidePanel()
    if (!visible) {
      lastRestoredCommentaryKey = null
      return
    }
    setTimeout(() => onCommentaryPanelMounted(), 0)
  }, { flush: 'post' })

  watch(hasCommentaries, (has) => {
    if (!has) {
      commentaryVisible.value = false
      if (sidePanelMode.value === 'commentary-tree') closeSidePanel()
    }
  })

  return {
    commentaryVisible,
    commentaryScrollIndex,
    commentaryScrollOffset,
    onCommentaryScroll,
    onCommentaryTreeChanged,
    onCommentaryPanelMounted,
  }
}
