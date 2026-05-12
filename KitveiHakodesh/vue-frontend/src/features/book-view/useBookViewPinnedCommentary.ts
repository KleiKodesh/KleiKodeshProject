/**
 * Manages the pinned commentary book for the split-pane bottom panel.
 *
 * The pin tracks which commentary book is currently visible. When the user
 * navigates to a new line, the pin falls back to the first default commentator
 * that has links for that line. When the user explicitly scrolls to a book in
 * the commentary view, the pin follows their selection.
 */
import { ref, watch } from 'vue'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import type { CommentaryGroup } from './useCommentary'

interface CommentaryViewRef {
  activeBookId: number | null
}

export function usePinnedCommentary(
  bookId: number | undefined,
  commentaryLineId: () => number | null,
  groups: () => CommentaryGroup[],
  commentaryViewRef: () => CommentaryViewRef | null,
) {
  const pinnedCommentaryBookId = ref<number | null>(null)
  let defaultCommentatorBookIds: number[] = []
  let defaultCommentatorsLoaded = false

  async function ensureDefaultCommentatorsLoaded() {
    if (defaultCommentatorsLoaded || bookId == null) return
    defaultCommentatorsLoaded = true
    const rows = await query<{ commentatorBookId: number }>(SQL.GET_DEFAULT_COMMENTATORS, [bookId])
    defaultCommentatorBookIds = rows.map((r) => r.commentatorBookId)
  }

  // When the selected line changes, update the pin to follow the user's active book
  // or fall back to the first default commentator.
  // activeBookId must be captured synchronously before any await — by the time
  // ensureDefaultCommentatorsLoaded() resolves, useCommentary has already cleared
  // groups (groups.value = []) and CommentaryView has re-rendered with no items,
  // making activeBookId return 0 or null. Capturing it first ensures we always
  // get the book the user was actually looking at before the line change.
  watch(commentaryLineId, async () => {
    const capturedBookId = commentaryViewRef()?.activeBookId ?? null
    await ensureDefaultCommentatorsLoaded()
    if (capturedBookId) {
      pinnedCommentaryBookId.value = capturedBookId
    } else if (defaultCommentatorBookIds.length > 0) {
      pinnedCommentaryBookId.value = defaultCommentatorBookIds[0]!
    }
  })

  // When groups load for a new line, if the current pin is a default that has no
  // links for this line, fall back to the next default that does.
  watch(groups, async (newGroups) => {
    if (!newGroups.length) return
    await ensureDefaultCommentatorsLoaded()
    if (!defaultCommentatorBookIds.length) return
    const currentPin = pinnedCommentaryBookId.value
    if (currentPin == null || !defaultCommentatorBookIds.includes(currentPin)) return
    const available = defaultCommentatorBookIds.find((id) => newGroups.some((g) => g.bookId === id))
    if (available != null) pinnedCommentaryBookId.value = available
  })

  return { pinnedCommentaryBookId }
}
