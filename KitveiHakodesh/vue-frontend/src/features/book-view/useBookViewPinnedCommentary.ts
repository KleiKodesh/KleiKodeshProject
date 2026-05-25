/**
 * Manages the pinned commentary group for the split-pane bottom panel.
 *
 * The pin tracks which commentary group (book + section) is currently visible.
 * When the user navigates to a new line, the pin falls back to the first default
 * commentator that has links for that line. When the user explicitly scrolls to
 * a group in the commentary view, the pin follows their selection.
 */
import { ref, watch } from 'vue'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import type { CommentaryGroup } from './commentary/useCommentary'
import type { PinnedCommentaryGroup } from './bookViewTypes'

interface CommentaryViewRef {
  activePinnedGroup: PinnedCommentaryGroup | null
}

export function usePinnedCommentary(
  bookId: number | undefined,
  commentaryLineId: () => number | null,
  groups: () => CommentaryGroup[],
  commentaryViewRef: () => CommentaryViewRef | null,
) {
  const pinnedCommentaryGroup = ref<PinnedCommentaryGroup | null>(null)
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

  // When the selected line changes, update the pin to follow the user's active group
  // or fall back to the first default commentator.
  // activePinnedGroup must be captured synchronously before any await — by the time
  // ensureDefaultCommentatorsLoaded() resolves, useCommentary has already cleared
  // groups (groups.value = []) and CommentaryView has re-rendered with no items,
  // making activePinnedGroup return null. Capturing it first ensures we always
  // get the group the user was actually looking at before the line change.
  watch(commentaryLineId, async () => {
    if (restoredFromSession) {
      restoredFromSession = false
      return
    }
    const capturedGroup = commentaryViewRef()?.activePinnedGroup ?? null
    await ensureDefaultCommentatorsLoaded()
    if (capturedGroup) {
      pinnedCommentaryGroup.value = capturedGroup
    } else if (defaultCommentatorBookIds.length > 0) {
      // Fall back to the first default commentator — use its first group in the
      // current groups list to get the correct sectionLabel/subSectionLabel.
      const defaultId = defaultCommentatorBookIds[0]!
      const defaultGroup = groups().find((g) => g.bookId === defaultId)
      pinnedCommentaryGroup.value = defaultGroup
        ? { bookId: defaultId, sectionLabel: defaultGroup.sectionLabel ?? '', subSectionLabel: defaultGroup.subSectionLabel ?? '' }
        : { bookId: defaultId, sectionLabel: '', subSectionLabel: '' }
    }
  })

  // When groups load for a new line, if the current pin is a default that has no
  // links for this line, fall back to the next default that does — but only if
  // there is no default at all in the new groups. If the pinned book is simply
  // absent for this line, keep the pin and let groupsForDisplay show a placeholder.
  watch(groups, async (newGroups) => {
    if (!newGroups.length) return
    await ensureDefaultCommentatorsLoaded()
    if (!defaultCommentatorBookIds.length) return
    const currentPin = pinnedCommentaryGroup.value
    if (currentPin == null || !defaultCommentatorBookIds.includes(currentPin.bookId)) return
    const anyDefaultPresent = defaultCommentatorBookIds.some((id) => newGroups.some((g) => g.bookId === id))
    if (anyDefaultPresent) return
    const defaultId = defaultCommentatorBookIds[0]!
    const defaultGroup = newGroups.find((g) => g.bookId === defaultId)
    pinnedCommentaryGroup.value = defaultGroup
      ? { bookId: defaultId, sectionLabel: defaultGroup.sectionLabel ?? '', subSectionLabel: defaultGroup.subSectionLabel ?? '' }
      : { bookId: defaultId, sectionLabel: '', subSectionLabel: '' }
  })

  function restorePin(group: PinnedCommentaryGroup) {
    pinnedCommentaryGroup.value = group
    restoredFromSession = true
  }

  return { pinnedCommentaryGroup, restorePin }
}
