/**
 * Lifecycle of ONE commentary panel: whether it is open, and the place it is at.
 *
 * The book view runs one of these per CommentarySlot, so nothing here may reach
 * for shared book-view state. Consumers that need to react to a panel closing
 * watch `commentaryVisible` themselves (useCommentaryPanelSlot closes that
 * panel's filter tree that way).
 *
 * The place is a row index + pixel offset into it, kept HERE so it outlives the
 * panel's view (closed and reopened, remounted by a layout change, saved to and
 * read back from the session). This file never puts the panel anywhere: the place
 * is handed to CommentaryView, which creates its scroller AT that row (a windowed
 * scroller takes its start row as an argument of its creation — see
 * useCommentaryScroll). There is no restore step, and nothing here may wait for the
 * view to mount in order to move it.
 */
import { ref, watch } from 'vue'

export function useBookViewCommentaryPanel(
  selectedLineId: import('vue').Ref<number | null>,
  commentaryLineId: import('vue').Ref<number | null>,
  lines: () => { loadedCount: number },
  hasCommentaries: import('vue').Ref<boolean>,
  ensureStaticFilterGroupsLoaded: () => void,
) {
  const commentaryVisible = ref(false)
  const commentaryScrollIndex = ref<number | null>(null)
  const commentaryScrollOffset = ref<number | null>(null)

  /** The view reports where it is on every scroll; this is what a later scroller is created at. */
  function onCommentaryScroll(scrollIndex: number, scrollOffset: number) {
    commentaryScrollIndex.value = scrollIndex
    commentaryScrollOffset.value = scrollOffset
  }

  // A panel that opens while a line is selected but the commentary is not anchored
  // to it yet (session restore: the panel was open, the lines had not loaded) anchors
  // the commentary once the book has text. Decided at the moment the panel opens.
  const isAnchorOwed = ref(false)

  // flush: 'post' - after Vue has flushed the DOM, so the panel is painted before
  // the work it triggers (the filter groups load, the commentary load) begins.
  watch(commentaryVisible, (visible) => {
    if (!visible) {
      isAnchorOwed.value = false
      return
    }
    void ensureStaticFilterGroupsLoaded()
    isAnchorOwed.value = selectedLineId.value != null && commentaryLineId.value == null
  }, { flush: 'post' })

  watch(
    () => isAnchorOwed.value && lines().loadedCount > 0,
    (canAnchor) => {
      if (!canAnchor) return
      isAnchorOwed.value = false
      if (commentaryVisible.value && selectedLineId.value != null && commentaryLineId.value == null)
        commentaryLineId.value = selectedLineId.value
    },
  )

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
