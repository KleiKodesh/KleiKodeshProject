/**
 * Which commentator a commentary panel is on.
 *
 * The rule, in full:
 *   current = persisted (session restore) ?? the one the panel is on ?? this panel's default
 *
 * - Selecting another line - click, keyboard, auto-select once scrolling settles,
 *   section navigation - KEEPS the current commentator. If the new line has nothing
 *   from it, useGroupsForDisplay puts its placeholder at its place.
 * - The reader moves to another commentator by scrolling the panel to it, or by
 *   picking it (which scrolls the panel to it): when the panel's scroll stops, the
 *   commentator at its top becomes current (setCommentator).
 * - The default is used only while the panel has no commentator at all.
 *
 * The panel's windowed scroller is created AT this commentator's row (its start
 * row is an argument of its creation), so there is nothing to steer afterwards: no
 * per-line re-derivation, no timing guards.
 */
import { computed, ref, watch } from 'vue'
import { getDefaultCommentators } from '@/webview-host/seforimApi'
import type { CommentaryGroup } from './commentary/useCommentary'
import type { PinnedCommentaryGroup } from './bookViewTypes'

// One default-commentators query per book, EVER: every commentary panel creates a
// usePinnedCommentary instance, and without this cache each ran the identical
// query. The PROMISE is cached so the instances racing at mount share one in-flight
// request; a rejection is evicted so a transient service error retries next time.
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
 * A commentator reference for `bookId`, with the disambiguating labels from that
 * book's group in `groups` when it has one (blank otherwise: the panel matches on
 * bookId alone and the placeholder supplies the row).
 */
export function buildPin(bookId: number, groups: CommentaryGroup[], chosen = true): PinnedCommentaryGroup {
  const group = groups.find((g) => g.bookId === bookId)
  return { bookId, sectionLabel: group?.sectionLabel ?? '', subSectionLabel: group?.subSectionLabel ?? '', chosen }
}

/**
 * One instance per commentary panel. `defaultRank` picks this panel's default (bottom:
 * the first, side: the second, left side: the third); `fallBackToFirstDefault` says
 * whether a book with fewer defaults than the rank falls back to the first one, or
 * leaves this panel on no commentator (it then shows the list from the top).
 */
export function usePinnedCommentary(
  bookId: number | undefined,
  _commentaryLineId: () => number | null,
  groups: () => CommentaryGroup[],
  defaultRank = 0,
  fallBackToFirstDefault = true,
) {
  const pinnedCommentaryGroup = ref<PinnedCommentaryGroup | null>(null)
  /** The book's default commentators, once known (null while loading). */
  const defaults = ref<number[] | null>(null)

  if (bookId != null) {
    loadDefaultCommentatorIds(bookId)
      .catch(() => [] as number[])
      .then((ids) => {
        defaults.value = ids
      })
  } else {
    defaults.value = []
  }

  function defaultId(): number | undefined {
    const ids = defaults.value ?? []
    return ids[defaultRank] ?? (fallBackToFirstDefault ? ids[0] : undefined)
  }

  // The default applies only while there is no commentator (not restored, not
  // chosen, not carried from a previous line). It is applied once the defaults are
  // known, and never replaces a commentator the panel already has.
  watch(
    [defaults, () => groups()],
    () => {
      if (pinnedCommentaryGroup.value != null || defaults.value == null) return
      const id = defaultId()
      if (id !== undefined) pinnedCommentaryGroup.value = buildPin(id, groups(), false)
    },
    { immediate: true },
  )

  // Labels only: when the current commentator appears in a newly loaded list, take
  // its real section labels from it (they disambiguate a book listed in several
  // sections). The commentator itself never changes here.
  watch(
    () => groups(),
    (list) => {
      const current = pinnedCommentaryGroup.value
      if (current == null || !list.some((g) => g.bookId === current.bookId)) return
      const next = buildPin(current.bookId, list, current.chosen)
      if (next.sectionLabel !== current.sectionLabel || next.subSectionLabel !== current.subSectionLabel) {
        pinnedCommentaryGroup.value = next
      }
    },
  )

  /**
   * The panel can create its scroller once it knows its commentator - or knows it
   * has none (no default at its rank). Only the defaults load can make it wait.
   */
  const isPinSettled = computed(() => pinnedCommentaryGroup.value != null || defaults.value != null)

  /** Persistence: the saved commentator wins over the default. */
  function restorePin(group: PinnedCommentaryGroup) {
    pinnedCommentaryGroup.value = group
  }

  /** The reader moved this panel to a commentator (scrolled or picked). */
  function setCommentator(group: { bookId: number; sectionLabel: string; subSectionLabel: string }) {
    pinnedCommentaryGroup.value = { ...group, chosen: true }
  }

  /** The reader picked this commentator (header toolbar). */
  function pinExplicitly(id: number) {
    pinnedCommentaryGroup.value = buildPin(id, groups(), true)
  }

  return { pinnedCommentaryGroup, isPinSettled, restorePin, pinExplicitly, setCommentator }
}
