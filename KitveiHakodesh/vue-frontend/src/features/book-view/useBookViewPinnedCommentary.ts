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
import { getDefaultCommentators } from '@/webview-host/seforimApi'
import type { CommentaryGroup } from './commentary/useCommentary'
import type { PinnedCommentaryGroup } from './bookViewTypes'

// One default-commentators query per book, EVER: both commentary panels create a
// usePinnedCommentary instance, and without this cache each ran the identical
// query. The PROMISE is cached (same pattern as staticFilterGroupsByBook) so the
// two instances racing at mount share one in-flight request; a rejection is
// evicted so a transient service error retries next time.
const defaultCommentatorsByBook = new Map<number, Promise<number[]>>()

function loadDefaultCommentatorIds(bookId: number): Promise<number[]> {
  let pending = defaultCommentatorsByBook.get(bookId)
  if (!pending) {
    pending = getDefaultCommentators(bookId).then((rows) => rows.map((r) => r.commentatorBookId))
    defaultCommentatorsByBook.set(bookId, pending)
    pending.catch(() => defaultCommentatorsByBook.delete(bookId))
  }
  return pending
}

/**
 * One instance per commentary panel.
 *
 * `defaultRank` picks which of the book's default commentators this panel opens
 * on: the bottom panel takes the first, the side panel the second, so opening
 * both immediately shows two different commentators rather than the same one
 * twice. Books with only one default fall back to it for both panels.
 */
export function usePinnedCommentary(
  bookId: number | undefined,
  commentaryLineId: () => number | null,
  groups: () => CommentaryGroup[],
  defaultRank = 0,
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
    defaultCommentatorBookIds = await loadDefaultCommentatorIds(bookId).catch(() => [])
  }

  /** This panel's default commentator, falling back to the first one. */
  function preferredDefaultId(): number | undefined {
    return defaultCommentatorBookIds[defaultRank] ?? defaultCommentatorBookIds[0]
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
      const defaultId = preferredDefaultId()!
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
    const defaultId = preferredDefaultId()!
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
