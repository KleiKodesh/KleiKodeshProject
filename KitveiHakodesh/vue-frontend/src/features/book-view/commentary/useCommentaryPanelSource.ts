/**
 * Where ONE commentary panel gets its line and its commentary from.
 *
 * Normally that is the book: the line the reader picked there, and the one
 * commentary load every panel shares (`live`). A panel told to stop following the
 * book (a detached panel's toolbar button) switches to an anchor of its own - the
 * line it was on at that moment - with its own commentary load. The book can then
 * move on without it, and the panel's own section buttons move its own anchor.
 *
 * Everything a panel reads about "its line" comes through here, so nothing else
 * has to know which of the two it is on. `generation` changes on every switch:
 * the panel's view is keyed on it, because the list it shows is a different one.
 */
import { computed, effectScope, onScopeDispose, ref, shallowRef, watch, type EffectScope, type Ref } from 'vue'
import { useCommentary, type CommentaryGroup } from './useCommentary'
import type { CommentarySlot } from '../bookViewTypes'

type SectionRange = { fromIndex: number; toIndex: number }

/** The book's own line and the commentary load shared by every panel that follows it. */
export interface LiveCommentarySource {
  groups: Ref<CommentaryGroup[]>
  loading: Ref<boolean>
  loadError: Ref<boolean>
  selectedLineId: Ref<number | null>
  commentaryLineId: Ref<number | null>
  /** The lines the commentary covers when a whole section or a range is picked. */
  sectionLineIds: () => number[] | null
  sectionRange: () => SectionRange | null
  ensureLineContents: (lineIds: number[], readAheadLineIds?: number[]) => Promise<void>
  trimLineContents: (panel: CommentarySlot, keep: ((lineId: number) => boolean) | null) => void
}

/** A panel's own line, held while it does not follow the book. */
export interface OwnCommentaryAnchor {
  selectedLineId: Ref<number | null>
  commentaryLineId: Ref<number | null>
  /** Back to a single line: the anchor moved off the section or range it was taken on. */
  clearSection: () => void
}

interface OwnSource extends OwnCommentaryAnchor {
  scope: EffectScope
  data: ReturnType<typeof useCommentary>
}

export function useCommentaryPanelSource(
  slot: CommentarySlot,
  bookId: number | undefined,
  live: LiveCommentarySource,
  followsBook: Ref<boolean>,
) {
  const own = shallowRef<OwnSource | null>(null)
  const generation = ref(0)

  function createOwnSource(): OwnSource {
    // Its own scope: it lives until the panel follows the book again, not as long as
    // whatever happened to be running when the button was pressed.
    const scope = effectScope(true)
    return scope.run(() => {
      const selectedLineId = ref(live.selectedLineId.value)
      const commentaryLineId = ref(live.commentaryLineId.value)
      const sectionLineIds = shallowRef(live.sectionLineIds())
      const sectionRange = shallowRef(live.sectionRange())
      const data = useCommentary(
        () => commentaryLineId.value,
        () => sectionLineIds.value,
        () => bookId,
        () => sectionRange.value,
      )
      function clearSection() {
        sectionLineIds.value = null
        sectionRange.value = null
      }
      return { scope, selectedLineId, commentaryLineId, clearSection, data }
    })!
  }

  function releaseOwnSource() {
    own.value?.scope.stop()
    own.value = null
  }

  // Sync: the panel's view is re-keyed in the same tick the source changes, so it
  // never renders one source's rows against the other's place.
  watch(followsBook, (follows) => {
    // The text this panel held in the source it is leaving is no longer needed there.
    trimLineContents(slot, null)
    if (follows) releaseOwnSource()
    else own.value = createOwnSource()
    generation.value++
  }, { flush: 'sync' })

  onScopeDispose(releaseOwnSource)

  function ensureLineContents(lineIds: number[], readAheadLineIds?: number[]): Promise<void> {
    return (own.value?.data ?? live).ensureLineContents(lineIds, readAheadLineIds)
  }

  function trimLineContents(panel: CommentarySlot, keep: ((lineId: number) => boolean) | null): void {
    ;(own.value?.data ?? live).trimLineContents(panel, keep)
  }

  return {
    groups: computed(() => (own.value ? own.value.data.groups.value : live.groups.value)),
    loading: computed(() => (own.value ? own.value.data.loading.value : live.loading.value)),
    loadError: computed(() => (own.value ? own.value.data.loadError.value : live.loadError.value)),
    selectedLineId: computed(() => (own.value ? own.value.selectedLineId.value : live.selectedLineId.value)),
    commentaryLineId: computed(() => (own.value ? own.value.commentaryLineId.value : live.commentaryLineId.value)),
    ensureLineContents,
    trimLineContents,
    /** The panel's own anchor while it does not follow the book, else null. */
    ownAnchor: computed<OwnCommentaryAnchor | null>(() => own.value),
    generation,
  }
}

export type CommentaryPanelSource = ReturnType<typeof useCommentaryPanelSource>
