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
 * Build a pin for `bookId`, taking the disambiguating labels from that book's group
 * in `groups` when it has one and falling back to blank labels when it does not
 * (the book has no commentary on this line - scrollToGroup matches on bookId alone
 * in that case, and the placeholder supplies the row).
 *
 * The single place a pin is constructed. There are several ways to end up on a
 * commentator - scrolling to it, picking it from the header toolbar, section nav,
 * the default on a blank slate - and each used to build this object inline, with
 * its own copy of the `?? ''` fallbacks and its own decision about `chosen`. That
 * is how the two halves of the placeholder bug got out of step in the first place;
 * a new caller that forgets `chosen` reintroduces it silently.
 *
 * `chosen` says the READER put the panel here rather than it being derived for
 * them - see PinnedCommentaryGroup. Callers pass it rather than it being inferred,
 * because only the caller knows whether a person asked for this.
 */
export function buildPin(
  bookId: number,
  groups: CommentaryGroup[],
  chosen: boolean,
): PinnedCommentaryGroup {
  const group = groups.find((g) => g.bookId === bookId)
  return {
    bookId,
    sectionLabel: group?.sectionLabel ?? '',
    subSectionLabel: group?.subSectionLabel ?? '',
    chosen,
  }
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
    // Derived for the reader, not asked for: chosen = false, so it may still give
    // way to another default on a line it has no text for.
    pinnedCommentaryGroup.value = buildPin(defaultId, groups(), false)
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
    if (newGroups.some((g) => g.bookId === currentPin.bookId)) {
      // Same book, real labels now available - carry `chosen` across so a refresh
      // never downgrades the reader's choice to a derived default.
      pinnedCommentaryGroup.value = buildPin(currentPin.bookId, newGroups, !!currentPin.chosen)
      return
    }
    // The pinned book has no commentary on this line. If the READER chose it, that
    // is precisely the case the injected placeholder exists for: keep the pin so
    // useGroupsForDisplay can build "no text for this line" at the book's own
    // position and the reader stays on their commentator. Only a DERIVED default
    // may give way to the next default below.
    //
    // This branch was the placeholder bug. It fires only when the pin happens to
    // be one of the book's default commentators (the guard above), so it reassigned
    // the pin out from under the placeholder for some commentators and not others -
    // and useGroupsForDisplay keys the placeholder off this very ref, so it never
    // got the chance to build one.
    if (currentPin.chosen) return
    const defaultId = preferredDefaultId()
    if (defaultId === undefined) {
      pinnedCommentaryGroup.value = null
      return
    }
    pinnedCommentaryGroup.value = buildPin(defaultId, newGroups, false)
  })

  function restorePin(group: PinnedCommentaryGroup) {
    pinnedCommentaryGroup.value = group
    restoredFromSession = true
  }

  // Named for what it is: the reader put the panel on this book. Always chosen,
  // so it holds through lines the book has no commentary on (placeholder).
  function pinExplicitly(bookId: number) {
    pinnedCommentaryGroup.value = buildPin(bookId, groups(), true)
  }

  return { pinnedCommentaryGroup, restorePin, pinExplicitly, setPendingPin }
}
