/**
 * Lifecycle of ONE commentary panel: visibility, and scroll position save/restore
 * across the panel being closed, reopened, or remounted by a layout change.
 *
 * The book view runs one of these per CommentarySlot, so nothing here may reach
 * for shared book-view state. Consumers that need to react to a panel closing
 * watch `commentaryVisible` themselves (useCommentaryPanelSlot closes that
 * panel's filter tree that way).
 *
 * Scroll position is kept as a flat virtualizer index + offset so it survives the
 * panel being unmounted (v-if) and remounted.
 */
import { ref, watch, nextTick, onScopeDispose } from 'vue'
import type { PinnedCommentaryGroup } from './bookViewTypes'

type CommentaryGroup = { bookId: number; bookTitle: string; sectionLabel?: string; subSectionLabel?: string }

type CommentaryViewInstance = {
  captureScrollPos?: () => { scrollIndex: number; scrollOffset: number } | null
  restoreCommentaryScrollPos: (index: number, offset: number) => Promise<boolean>
  claimRestoreIntent?: () => void
  scrollToGroup: (bookId: number, sectionLabel?: string, subSectionLabel?: string, reason?: string) => void
}

export function useBookViewCommentaryPanel(
  commentaryViewRef: () => CommentaryViewInstance | null,
  groups: import('vue').Ref<CommentaryGroup[]>,
  commentaryLoading: import('vue').Ref<boolean>,
  pinnedCommentaryGroup: import('vue').Ref<PinnedCommentaryGroup | null>,
  selectedLineId: import('vue').Ref<number | null>,
  commentaryLineId: import('vue').Ref<number | null>,
  lines: () => { content: string | null }[],
  hasCommentaries: import('vue').Ref<boolean>,
  ensureStaticFilterGroupsLoaded: () => void,
) {
  const commentaryVisible = ref(false)
  const commentaryScrollIndex = ref<number | null>(null)
  const commentaryScrollOffset = ref<number | null>(null)

  // onCommentaryPanelMounted runs from a setTimeout, so Vue's active effect scope is gone
  // by then and every watch() created inside it is DETACHED — unmounting disposes none of
  // them. They self-stop on success, but a panel that closes (or a tab that dies) before
  // that condition lands would leak the watcher, its closure, and everything the closure
  // holds: `groups`, the whole `lines` array, and a dead CommentaryView instance. Since
  // the panel re-runs that function on every toggle and every layout remount, the leak
  // accumulates per open, not per tab. Track the handles and dispose them with the scope.
  const detachedWatchStops = new Set<() => void>()
  // The dispatch below is a setTimeout, so the scope can dispose inside that 0ms window —
  // after which onCommentaryPanelMounted would register watches into an already-swept
  // registry and nothing would ever sweep it again. The flag stops it running at all.
  let panelDisposed = false
  let pendingMountTimer: ReturnType<typeof setTimeout> | null = null
  /** Register a detached watch's stop handle; auto-unregisters when the watch self-stops. */
  function trackDetachedWatch(stop: () => void): () => void {
    detachedWatchStops.add(stop)
    return () => {
      detachedWatchStops.delete(stop)
      stop()
    }
  }
  onScopeDispose(() => {
    panelDisposed = true
    if (pendingMountTimer != null) { clearTimeout(pendingMountTimer); pendingMountTimer = null }
    for (const stop of detachedWatchStops) stop()
    detachedWatchStops.clear()
  })

  // Tracks the scroll position that was last successfully restored so that
  // onCommentaryPanelMounted does not redundantly restore the same position again
  // when the panel remounts after a layout change.
  let lastRestoredCommentaryKey: string | null = null

  function onCommentaryScroll(scrollIndex: number, scrollOffset: number) {
    commentaryScrollIndex.value = scrollIndex
    commentaryScrollOffset.value = scrollOffset
  }

  /**
   * Called when this panel's CommentaryView mounts (v-if becomes true) or remounts
   * after a layout change.
   *
   * Responsibilities:
   * 1. Ensure static filter groups are loaded (may be a no-op if already loaded).
   * 2. Sync commentaryLineId from selectedLineId when the panel first opens after
   *    session restore (the panel was visible in IDB but commentaryLineId is
   *    still null because lines haven't loaded yet).
   * 3. Restore the saved scroll position, or scroll to the pinned group if no
   *    saved position exists.
   */
  function onCommentaryPanelMounted() {
    if (panelDisposed) return // scope died inside the dispatch window
    if (!commentaryVisible.value) return
    void ensureStaticFilterGroupsLoaded()

    // Sync commentaryLineId from selectedLineId when a commentary panel first
    // opens after session restore (visible=true but commentaryLineId is still
    // null because lines haven't loaded yet).
    if (selectedLineId.value != null && commentaryLineId.value == null) {
      let stop: (() => void) | undefined
      stop = trackDetachedWatch(
        watch(
          () => lines().some((l) => l.content !== null),
          (hasContent) => {
            if (!hasContent) return
            stop?.()
            if (commentaryVisible.value && selectedLineId.value != null && commentaryLineId.value == null)
              commentaryLineId.value = selectedLineId.value
          },
          { immediate: true },
        ),
      )
    }

    if (commentaryScrollIndex.value != null && commentaryScrollOffset.value != null) {
      const savedScrollIndex = commentaryScrollIndex.value
      const savedScrollOffset = commentaryScrollOffset.value
      const restoreKey = `${savedScrollIndex}:${savedScrollOffset}`

      if (restoreKey === lastRestoredCommentaryKey) {
        // Position already restored - just scroll to pinned group if groups are loaded.
        if (groups.value.length > 0 && pinnedCommentaryGroup.value) {
          nextTick(() => {
            const pinned = pinnedCommentaryGroup.value
            if (pinned) commentaryViewRef()?.scrollToGroup(pinned.bookId, undefined, undefined, 'already-restored')
          })
        }
        return
      }

      let stopLoading: (() => void) | undefined
      let stopViewRef: (() => void) | undefined
      const cancelRestore = () => { stopLoading?.(); stopViewRef?.() }
      let stopVisibleGuard: (() => void) | undefined
      stopVisibleGuard = trackDetachedWatch(
        watch(commentaryVisible, (visible) => {
          if (!visible) { cancelRestore(); lastRestoredCommentaryKey = null; stopVisibleGuard?.() }
        }),
      )

      stopLoading = trackDetachedWatch(watch(
        () => !commentaryLoading.value && groups.value.length > 0,
        (ready) => {
          if (!ready) return
          // A line click while commentary was loading clears the saved position
          // (see onLineSelected in useBookView) - the queued restore is stale and
          // must yield to the pinned-group jump for the newly clicked line.
          if (commentaryScrollIndex.value == null) { cancelRestore(); return }
          stopLoading?.()
          const viewRef = commentaryViewRef()
          if (viewRef) {
            // Claim restore intent SYNCHRONOUSLY, before the nextTick below. This
            // reload also wakes setupGroupReloadScroll's watcher; without the
            // synchronous claim the two race and the panel can land on the pinned
            // group instead of the saved position.
            viewRef.claimRestoreIntent?.()
            nextTick(async () => {
              // Only record the restore as done when it actually applied. A restore
              // that died unapplied (panel closed mid-flight) used to still stamp the
              // key, so the next reopen deduped against it and skipped restoring -
              // the reader's place was lost.
              const applied = await viewRef.restoreCommentaryScrollPos(savedScrollIndex, savedScrollOffset)
              if (applied) lastRestoredCommentaryKey = restoreKey
            })
          } else {
            stopViewRef = trackDetachedWatch(watch(
              () => commentaryViewRef(),
              (newRef) => {
                if (!newRef) return
                stopViewRef?.()
                newRef.claimRestoreIntent?.()
                nextTick(async () => {
                  const applied = await newRef.restoreCommentaryScrollPos(savedScrollIndex, savedScrollOffset)
                  if (applied) lastRestoredCommentaryKey = restoreKey
                })
              },
            ))
          }
        },
        { flush: 'post', immediate: true },
      ))
    } else if (groups.value.length > 0 && pinnedCommentaryGroup.value) {
      // No saved scroll position - scroll to pinned group (e.g. after a layout change).
      nextTick(() => {
        const pinned = pinnedCommentaryGroup.value
        if (pinned) commentaryViewRef()?.scrollToGroup(pinned.bookId, undefined, undefined, 'panel-mounted')
      })
    }
  }

  // flush: 'post' - runs after Vue has flushed the DOM so the commentary panel is
  // painted before any reactive side-effects (metadata load, commentaryLineId set,
  // scroll restore) begin. Without this the 'pre' flush meant everything ran before
  // the panel's slot appeared, causing a visible hang.
  watch(commentaryVisible, (visible) => {
    if (!visible) {
      lastRestoredCommentaryKey = null
      // A close cancels a mount that has not run yet — it would no-op on the visibility
      // check anyway, but leaving it pending means the handle we hold is stale.
      if (pendingMountTimer != null) { clearTimeout(pendingMountTimer); pendingMountTimer = null }
      return
    }
    // Replace rather than stack: only ever one mount in flight, and the handle we keep is
    // always the live one, so onScopeDispose can actually cancel it.
    if (pendingMountTimer != null) clearTimeout(pendingMountTimer)
    pendingMountTimer = setTimeout(() => {
      pendingMountTimer = null
      onCommentaryPanelMounted()
    }, 0)
  }, { flush: 'post' })

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
