/**
 * Makes the full-book lines backfill yield to commentary loading.
 *
 * useLines queues the WHOLE book as ~2000-line backfill chunks (needed for
 * in-book search and instant scrolling) — for a large book that is tens of MB
 * arriving as a constant three-deep stream of heavy queries. Any commentary
 * load that runs concurrently (the session-restored panel on tab return, or a
 * plain line click during the initial fill) queues behind those chunks and
 * takes seconds instead of milliseconds. This gate pauses the backfill while
 * commentary is loading and resumes it afterwards; the visible lines are
 * unaffected because the first chunk and prefetch()/prioritise() fetches
 * bypass the queue.
 *
 * Hold/release timeline:
 * - held from setup, so the tab-return flood cannot start before the restored
 *   commentary panel (if any) gets on the wire
 * - released when session restore finishes with no commentary panel to reopen,
 *   or when the panel closes
 * - re-held whenever a commentary load starts; released a short grace after it
 *   settles, so the panel's viewport content-priority fetch also gets through
 * - every hold arms a safety timeout — in-book search must never be starved of
 *   the full book by a stuck load
 */
import { watch, onScopeDispose } from 'vue'
import type { Ref } from 'vue'

// Grace between a commentary load settling and the backfill resuming: long
// enough for CommentaryView's virtual-items watcher to fire its
// requestContentPriority fetch for the now-visible lines, short enough to be
// unnoticeable in the full-book load time.
const RELEASE_GRACE_MS = 300

// Hard ceiling on how long a single hold can last.
const HOLD_SAFETY_TIMEOUT_MS = 5000

export function useBookViewLinesBackfillGate(
  holdBackfill: () => void,
  releaseBackfill: () => void,
  commentaryVisible: Ref<boolean>,
  commentaryLoading: Ref<boolean>,
) {
  let graceTimer: ReturnType<typeof setTimeout> | null = null
  let safetyTimer: ReturnType<typeof setTimeout> | null = null

  function cancelTimers() {
    if (graceTimer != null) { clearTimeout(graceTimer); graceTimer = null }
    if (safetyTimer != null) { clearTimeout(safetyTimer); safetyTimer = null }
  }

  function hold() {
    if (graceTimer != null) { clearTimeout(graceTimer); graceTimer = null }
    if (safetyTimer == null) safetyTimer = setTimeout(release, HOLD_SAFETY_TIMEOUT_MS)
    holdBackfill()
  }

  function release() {
    cancelTimers()
    releaseBackfill()
  }

  // Runs during synchronous component setup, before useLines' load() resumes
  // from its first await — guaranteed to win the race against spawnWorkers().
  hold()

  watch(commentaryLoading, (loading) => {
    if (loading) hold()
    // useCommentary clears the flag in a finally, so errors release too.
    else if (graceTimer == null) graceTimer = setTimeout(release, RELEASE_GRACE_MS)
  })

  // Panel closed — no commentary load can start until it reopens (line clicks
  // are inert while it is closed), so there is nothing to yield to.
  watch(commentaryVisible, (visible) => {
    if (!visible) release()
  })

  onScopeDispose(cancelTimers)

  /**
   * Called by useBookView right after restoreSession() resolves: at that point
   * commentaryVisible is final for the restore, so if no commentary panel is
   * reopening there is nothing to wait for and the backfill starts immediately.
   */
  function onSessionRestoreSettled() {
    if (!commentaryVisible.value) release()
  }

  return { onSessionRestoreSettled }
}
