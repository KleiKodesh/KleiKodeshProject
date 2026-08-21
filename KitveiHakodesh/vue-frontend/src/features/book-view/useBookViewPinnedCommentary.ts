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

// One default-commentators query per book, EVER: every commentary panel creates a
// usePinnedCommentary instance, and without this cache each ran the identical
// query. The PROMISE is cached (same pattern as staticFilterGroupsByBook) so the
// instances racing at mount share one in-flight request; a rejection is evicted
// so a transient service error retries next time.
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
 * on: the bottom panel takes the first, the side panel the second, the left side
 * panel the third, so opening several immediately shows different commentators
 * rather than the same one repeated.
 *
 * `fallBackToFirstDefault` decides what happens when the book has fewer defaults
 * than the panel's rank. The bottom and side panels fall back to the first
 * default (one-default books open that commentator in both). The left panel does
 * not: rather than showing a third copy of the same commentator it stays
 * unpinned, so the panel renders from the top and never scrolls anywhere.
 */
export function usePinnedCommentary(
  bookId: number | undefined,
  commentaryLineId: () => number | null,
  groups: () => CommentaryGroup[],
  defaultRank = 0,
  fallBackToFirstDefault = true,
) {
  const pinnedCommentaryGroup = ref<PinnedCommentaryGroup | null>(null)
  let defaultCommentatorBookIds: number[] = []
  // The in-flight (or settled) load, NOT a boolean latch. A flag set before the await let a
  // second caller return immediately with defaultCommentatorBookIds still empty, and the
  // groups watcher then bailed on its `!length` guard — the first line selection in a book
  // could end up with the panel unpinned and unlabelled.
  let defaultCommentatorsLoad: Promise<void> | null = null
  // One generation counter PER watcher (never shared — a groups fire must not cancel an
  // in-flight line-selection callback, which is what decides the default pin). A callback
  // that resumes after its await checks it is still the latest of its own kind before
  // writing the pin, so two rapid line selections cannot let the older callback land last
  // and pin the previous line's commentator.
  let lineGeneration = 0
  let groupsGeneration = 0
  // Set to true when the pin is restored from session so the first commentaryLineId
  // watcher fire doesn't overwrite it before the view has rendered.
  let restoredFromSession = false
  // Captured synchronously by the caller (onLineSelected / onNavigateSection)
  // before any reactive state changes. Applied by the commentaryLineId watcher.
  let pendingPin: PinnedCommentaryGroup | null = null

  function ensureDefaultCommentatorsLoaded(): Promise<void> {
    if (bookId == null) return Promise.resolve()
    defaultCommentatorsLoad ??= loadDefaultCommentatorIds(bookId)
      .catch(() => [])
      .then((ids) => {
        defaultCommentatorBookIds = ids
      })
    return defaultCommentatorsLoad
  }

  /**
   * This panel's default commentator, or undefined when the book has none at this
   * rank and the panel does not fall back (see fallBackToFirstDefault).
   */
  function preferredDefaultId(): number | undefined {
    const own = defaultCommentatorBookIds[defaultRank]
    if (own !== undefined) return own
    return fallBackToFirstDefault ? defaultCommentatorBookIds[0] : undefined
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
    const mine = ++lineGeneration
    await ensureDefaultCommentatorsLoaded()
    if (mine !== lineGeneration) return // a newer line selection owns the pin now
    if (captured) {
      pinnedCommentaryGroup.value = captured
      return
    }
    // No default at this panel's rank (and no fallback): leave the panel unpinned
    // so it renders the list from the top instead of jumping to a commentator
    // another panel is already showing.
    const defaultId = preferredDefaultId()
    if (defaultId === undefined) {
      pinnedCommentaryGroup.value = null
      return
    }
    const defaultGroup = groups().find((g) => g.bookId === defaultId)
    pinnedCommentaryGroup.value = defaultGroup
      ? { bookId: defaultId, sectionLabel: defaultGroup.sectionLabel ?? '', subSectionLabel: defaultGroup.subSectionLabel ?? '' }
      : { bookId: defaultId, sectionLabel: '', subSectionLabel: '' }
  })

  // When groups load for a new line:
  // - If the current pin is a default and that default IS present in the new groups,
  //   refresh the pin with the real sectionLabel/subSectionLabel from the loaded group.
  // - If the current pin is a default that has no links for this line, fall back to the
  //   next default that does.
  watch(groups, async () => {
    const mine = ++groupsGeneration
    await ensureDefaultCommentatorsLoaded()
    if (mine !== groupsGeneration) return // superseded while the defaults loaded
    // Re-read live: the argument captured before the await can be a previous line's groups.
    const newGroups = groups()
    if (!newGroups.length) return
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
    const defaultId = preferredDefaultId()
    if (defaultId === undefined) {
      pinnedCommentaryGroup.value = null
      return
    }
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
