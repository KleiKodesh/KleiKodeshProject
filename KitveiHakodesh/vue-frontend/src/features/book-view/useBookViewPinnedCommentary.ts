/**
 * Manages the pinned commentary group for the split-pane bottom panel.
 *
 * The pin tracks which commentary group (book + section) is currently visible.
 * When the user navigates to a new line, the caller captures the active group
 * synchronously (before any reactive state changes) and passes it in via
 * setPendingPin(). The commentaryLineId watcher then applies it.
 *
 * This avoids all timing races with the virtualizer and scroll events — the
 * capture happens at the point of user interaction, not asynchronously.
 */
import { ref, watch } from 'vue'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import type { CommentaryGroup } from './commentary/useCommentary'
import type { PinnedCommentaryGroup } from './bookViewTypes'

export function usePinnedCommentary(
  bookId: number | undefined,
  commentaryLineId: () => number | null,
  groups: () => CommentaryGroup[],
) {
  const pinnedCommentaryGroup = ref<PinnedCommentaryGroup | null>(null)
  let defaultCommentatorBookIds: number[] = []
  let defaultCommentatorsLoaded = false
  // Set to true when the pin is restored from session so the first commentaryLineId
  // watcher fire doesn't overwrite it before the view has rendered.
  let restoredFromSession = false
  // Captured synchronously by the caller (onLineSelected / onNavigateSection)
  // before any reactive state changes. Applied by the commentaryLineId watcher.
  let pendingPin: PinnedCommentaryGroup | null = null

  async function ensureDefaultCommentatorsLoaded() {
    if (defaultCommentatorsLoaded || bookId == null) return
    defaultCommentatorsLoaded = true
    const rows = await query<{ commentatorBookId: number }>(SQL.GET_DEFAULT_COMMENTATORS, [bookId])
    defaultCommentatorBookIds = rows.map((r) => r.commentatorBookId)
  }

  // Called by onLineSelected / onNavigateSection synchronously before setting
  // selectedLineId/commentaryLineId — captures which book the user was looking at.
  function setPendingPin(group: PinnedCommentaryGroup | null) {
    pendingPin = group
  }

  watch(commentaryLineId, async () => {
    if (restoredFromSession) {
      restoredFromSession = false
      return
    }
    const captured = pendingPin
    pendingPin = null
    await ensureDefaultCommentatorsLoaded()
    if (captured) {
      pinnedCommentaryGroup.value = captured
    } else if (defaultCommentatorBookIds.length > 0) {
      const defaultId = defaultCommentatorBookIds[0]!
      const defaultGroup = groups().find((g) => g.bookId === defaultId)
      pinnedCommentaryGroup.value = defaultGroup
        ? { bookId: defaultId, sectionLabel: defaultGroup.sectionLabel ?? '', subSectionLabel: defaultGroup.subSectionLabel ?? '' }
        : { bookId: defaultId, sectionLabel: '', subSectionLabel: '' }
    }
  })

  // When groups load for a new line:
  // - If the current pin is a default and that default IS present in the new groups,
  //   refresh the pin with the real sectionLabel/subSectionLabel from the loaded group.
  // - If the current pin is a default that has no links for this line, fall back to the
  //   next default that does.
  watch(groups, async (newGroups) => {
    if (!newGroups.length) return
    await ensureDefaultCommentatorsLoaded()
    if (!defaultCommentatorBookIds.length) return
    const currentPin = pinnedCommentaryGroup.value
    if (currentPin == null || !defaultCommentatorBookIds.includes(currentPin.bookId)) return
    const pinnedGroupInNewGroups = newGroups.find((g) => g.bookId === currentPin.bookId)
    if (pinnedGroupInNewGroups) {
      pinnedCommentaryGroup.value = {
        bookId: currentPin.bookId,
        sectionLabel: pinnedGroupInNewGroups.sectionLabel ?? '',
        subSectionLabel: pinnedGroupInNewGroups.subSectionLabel ?? '',
      }
      return
    }
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

  function pinExplicitly(bookId: number) {
    const group = groups().find((g) => g.bookId === bookId)
    pinnedCommentaryGroup.value = group
      ? { bookId, sectionLabel: group.sectionLabel ?? '', subSectionLabel: group.subSectionLabel ?? '' }
      : { bookId, sectionLabel: '', subSectionLabel: '' }
  }

  return { pinnedCommentaryGroup, restorePin, pinExplicitly, setPendingPin }
}
