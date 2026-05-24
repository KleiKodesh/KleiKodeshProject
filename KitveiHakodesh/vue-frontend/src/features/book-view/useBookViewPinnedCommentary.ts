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
  // Set to true when the pin is restored from session so the first commentaryLineId
  // watcher fire doesn't overwrite it before the view has rendered.
  let restoredFromSession = false

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
    if (restoredFromSession) {
      console.log('[pin] commentaryLineId fired — skipping (restoredFromSession), pin stays:', pinnedCommentaryBookId.value)
      restoredFromSession = false
      return
    }
    const capturedBookId = commentaryViewRef()?.activeBookId ?? null
    console.log('[pin] commentaryLineId fired — capturedBookId:', capturedBookId)
    await ensureDefaultCommentatorsLoaded()
    if (capturedBookId) {
      console.log('[pin] setting pin to capturedBookId:', capturedBookId)
      pinnedCommentaryBookId.value = capturedBookId
    } else if (defaultCommentatorBookIds.length > 0) {
      console.log('[pin] no capturedBookId, falling back to default:', defaultCommentatorBookIds[0])
      pinnedCommentaryBookId.value = defaultCommentatorBookIds[0]!
    }
  })

  // When groups load for a new line, if the current pin is a default that has no
  // links for this line, fall back to the next default that does — but only if
  // there is no default at all in the new groups. If the pinned book is simply
  // absent for this line, keep the pin and let groupsForDisplay show a placeholder.
  watch(groups, async (newGroups) => {
    console.log('[pin] groups watcher fired, length:', newGroups.length, 'currentPin:', pinnedCommentaryBookId.value)
    if (!newGroups.length) return
    await ensureDefaultCommentatorsLoaded()
    if (!defaultCommentatorBookIds.length) return
    const currentPin = pinnedCommentaryBookId.value
    if (currentPin == null || !defaultCommentatorBookIds.includes(currentPin)) return
    const anyDefaultPresent = defaultCommentatorBookIds.some((id) => newGroups.some((g) => g.bookId === id))
    console.log('[pin] anyDefaultPresent:', anyDefaultPresent, 'defaults:', defaultCommentatorBookIds)
    if (anyDefaultPresent) return
    console.log('[pin] no defaults present — falling back to:', defaultCommentatorBookIds[0])
    pinnedCommentaryBookId.value = defaultCommentatorBookIds[0]!
  })

  function restorePin(bookId: number) {
    pinnedCommentaryBookId.value = bookId
    restoredFromSession = true
  }

  return { pinnedCommentaryBookId, restorePin }
}
