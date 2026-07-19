import { useSwipe } from '@vueuse/core'
import { useEventListener } from '@vueuse/core'
import { useTabStore } from '@/stores/tabStore'

// Horizontal travel (in px) that counts as a deliberate swipe. Precision touchpads
// report large per-event deltas (~100+), so this needs to span a few events — too
// small and a mere flick fires. The vertical-dominance guard below keeps ordinary
// page scrolling from reaching it.
const TRACKPAD_DELTA_THRESHOLD = 150
// Quiet gap (no wheel events) that resets the PRE-fire accumulator, so travel from an
// abandoned half-swipe can't combine with a later one. Generous because slow inputs
// (held tilt-wheel auto-repeat) legitimately pace events up to ~100ms apart.
const ACCUM_RESET_GAP_MS = 150
// Quiet gap that re-arms AFTER a fire. One continuous physical swipe (finger movement +
// momentum tail) delivers events every ~8-33ms with no gaps, so this never triggers
// mid-gesture; lifting the finger and re-touching for a second swipe almost always
// leaves a quiet moment at least this long. Deliberately much shorter than
// ACCUM_RESET_GAP_MS: a false re-arm is harmless (switching still needs a full
// TRACKPAD_DELTA_THRESHOLD of fresh travel), so repeat swipes feel instant.
const REARM_GAP_MS = 60
// Re-strike detector (adopted from the `wheel-gestures` library's momentum-cancel rule).
// A momentum tail decays MONOTONICALLY — each event is a fraction of the one before — so
// an event whose magnitude JUMPS to more than REARM_SPIKE_RATIO× the previous event's can
// only be a fresh finger strike interrupting the tail. This re-arms instantly even when the
// event stream never paused. It is RELATIVE (a ratio, not an absolute px count) so it adapts
// to swipe strength: a slow, gentle re-swipe off a small tail (~3px→18px) and a firm one off
// a big tail (~20px→60px) are both caught, where a fixed px floor would swallow the gentle
// one. Ratio 2 is the value wheel-gestures uses; it clears the tail's <1 ratios and even a
// mid-swipe speed-up (which tops out well under 2×), so a single wavy swipe still fires once.
const REARM_SPIKE_RATIO = 2
// Absolute floor beneath the ratio test: in a nearly-dead tail (prev delta ~3px) a few px of
// jitter would clear 2× and re-arm needlessly. Requiring the event also exceed this keeps a
// dead tail quiet. Small enough that any real finger strike sails past it. (A false re-arm is
// harmless anyway — switching still needs a full TRACKPAD_DELTA_THRESHOLD of travel — this is
// just tidiness.)
const REARM_SPIKE_FLOOR_PX = 12
// Wheel deltas are usually pixels (deltaMode 0), but mouse wheels report lines (1) or
// pages (2). Approximate one line as this many px so a line-mode swipe still accumulates
// toward the pixel threshold instead of never firing.
const WHEEL_LINE_HEIGHT_PX = 16
const TOUCH_THRESHOLD_PX = 60

// Custom event name used by iframe relays (PdfViewPage, HtmlViewPage, etc.) to
// signal a completed swipe gesture that originated inside an iframe where native
// touch/wheel events cannot bubble to the parent document.
export const TAB_SWIPE_EVENT = 'tab-swipe-gesture'

export interface TabSwipeGestureEventDetail {
  direction: 'next' | 'previous'
}

/**
 * Builds a `wheel` event handler that switches tabs on a horizontal trackpad swipe,
 * behaving like a Ctrl+Tab / Ctrl+Shift+Tab press: ONE physical swipe advances exactly
 * ONE tab, and you can do several in a row to cycle — with no forced pause between them.
 *
 * It accumulates horizontal delta and fires once when the running total crosses the
 * threshold, then LOCKS for the rest of the push. The lock protects a single
 * slightly-too-long swipe from jumping two tabs; the hard part is re-arming fast
 * enough that deliberate back-to-back swipes don't feel throttled. Key safety
 * property: a false re-arm costs nothing, because switching still requires a full
 * TRACKPAD_DELTA_THRESHOLD of fresh travel — only a real second push provides that.
 * So the lock re-arms on the EARLIEST of three gesture-boundary signals:
 *   • direction reversal (you flicked back the other way — instant), or
 *   • a short quiet gap (> REARM_GAP_MS): one continuous swipe streams events with no
 *     gaps, so any gap means the finger lifted, or
 *   • a re-strike spike: one physical swipe is a single velocity hump whose momentum
 *     tail decays MONOTONICALLY (each event a fraction of the one before). So an event
 *     that jumps to more than REARM_SPIKE_RATIO× the previous event's magnitude can only
 *     be a fresh finger strike interrupting the tail — re-arm even if the stream never
 *     paused. Using a RATIO (not a fixed px jump) is what the `wheel-gestures` library
 *     does for this same problem; it adapts to swipe strength and, because a mid-swipe
 *     speed-up tops out well under 2×, a single variable-speed swipe still fires once.
 *     (An earlier attempt treated any speed dip as "finger lifted" and double-fired on
 *     long swipes — a monotonic tail never spikes UP, so the up-spike is the reliable
 *     boundary, not the dip.)
 *
 * Direction (RTL — the tab list runs right-to-left, so "next" sits to the LEFT):
 *   scroll left  (deltaX < 0) → next tab
 *   scroll right (deltaX > 0) → previous tab
 *
 * Shared by the parent document and the iframe relays so the threshold and direction
 * convention live in exactly one place.
 */
export function createWheelSwipeHandler(onSwipe: (direction: 'next' | 'previous') => void) {
  let accumulatedDeltaX = 0
  let lastWheelTime = 0
  let firedThisGesture = false
  let lastFiredSign = 0 // direction of the last switch (+1 previous / -1 next), for reversal detection
  let prevAbsDeltaX = 0 // |deltaX| of the previous HORIZONTAL event, for the re-strike ratio test

  return (event: WheelEvent) => {
    const now = Date.now()
    const gap = now - lastWheelTime
    lastWheelTime = now

    // Normalize line/page wheel modes to pixels so the threshold is unit-independent.
    const unit =
      event.deltaMode === 1 ? WHEEL_LINE_HEIGHT_PX : event.deltaMode === 2 ? window.innerHeight : 1
    const deltaX = event.deltaX * unit
    const deltaY = event.deltaY * unit

    // A long pause invalidates whatever came before — re-arm and drop stale travel.
    if (gap > ACCUM_RESET_GAP_MS) {
      firedThisGesture = false
      accumulatedDeltaX = 0
    }

    // Ignore vertical-dominant scrolling. (Returning early leaves prevAbsDeltaX untouched,
    // so the ratio test below always compares consecutive HORIZONTAL events.)
    if (Math.abs(deltaX) <= Math.abs(deltaY)) return

    const absDeltaX = Math.abs(deltaX)
    const prevAbs = prevAbsDeltaX
    prevAbsDeltaX = absDeltaX

    if (firedThisGesture) {
      // Locked for the current push. Re-arm on any gesture-boundary signal (see doc
      // comment): opposite-direction flick, post-fire quiet gap, or a re-strike spike
      // (this event jumps to > REARM_SPIKE_RATIO× the previous — a monotonic momentum
      // tail never does that, so it can only be a new finger push).
      const reversed = lastFiredSign !== 0 && Math.sign(deltaX) === -lastFiredSign
      const quietGap = gap > REARM_GAP_MS
      const reStrike = absDeltaX > Math.max(REARM_SPIKE_FLOOR_PX, prevAbs * REARM_SPIKE_RATIO)
      if (reversed || quietGap || reStrike) {
        firedThisGesture = false
        accumulatedDeltaX = 0
        // fall through — this event's delta opens the new count below
      } else {
        return
      }
    }

    // A direction reversal starts a fresh count so leftover delta can't cancel it out.
    if (accumulatedDeltaX !== 0 && Math.sign(deltaX) !== Math.sign(accumulatedDeltaX)) {
      accumulatedDeltaX = 0
    }
    accumulatedDeltaX += deltaX

    if (Math.abs(accumulatedDeltaX) >= TRACKPAD_DELTA_THRESHOLD) {
      const direction = accumulatedDeltaX < 0 ? 'next' : 'previous'
      onSwipe(direction)
      firedThisGesture = true
      lastFiredSign = accumulatedDeltaX < 0 ? -1 : 1
      accumulatedDeltaX = 0
    }
  }
}

/**
 * Wires up tab switching via horizontal swipe (touch) and trackpad horizontal scroll.
 *
 * Direction convention (RTL — "next" advances forward through the tab array, which
 * is laid out right-to-left, so the next tab is visually to the left):
 *   swipe right (finger moves right) → next tab
 *   swipe left  (finger moves left)  → previous tab
 *
 * Touch: uses VueUse useSwipe on the document body.
 * Trackpad: a shared wheel handler (createWheelSwipeHandler) accumulates deltaX and
 * fires once per gesture.
 *
 * Iframe relay: iframes (PDF viewer, HTML viewer) cannot bubble touch/wheel events
 * to the parent document. Those pages track gestures internally and fire a
 * TAB_SWIPE_EVENT CustomEvent on the parent window when a swipe is detected.
 * This composable listens for that event and handles it identically to native gestures.
 */
export function useTabSwipeNavigation() {
  const tabStore = useTabStore()

  function switchToAdjacentTab(direction: 'next' | 'previous') {
    const tabs = tabStore.pane1Tabs
    if (tabs.length < 2) return
    const currentIndex = tabs.findIndex((tab) => tab.id === tabStore.activeTabId)
    if (currentIndex === -1) return

    const targetIndex =
      direction === 'next'
        ? (currentIndex + 1) % tabs.length
        : (currentIndex - 1 + tabs.length) % tabs.length

    tabStore.switchTab(tabs[targetIndex]!.id)
  }

  // ── Touch swipe (native — document body) ────────────────────────────────────

  useSwipe(document.body, {
    threshold: TOUCH_THRESHOLD_PX,
    onSwipeEnd(_event, direction) {
      if (direction === 'right') switchToAdjacentTab('next')
      else if (direction === 'left') switchToAdjacentTab('previous')
    },
  })

  // ── Trackpad horizontal scroll (native — parent document) ───────────────────
  // Capture phase so the gesture is seen even if a child scroll container stops wheel
  // propagation; passive since we no longer preventDefault (overscroll is handled by
  // CSS overscroll-behavior on the root). The C# host also forwards WM_MOUSEHWHEEL as
  // a synthetic wheel event here when the web content lacks OS focus (AppViewerFocus.cs).
  useEventListener(document, 'wheel', createWheelSwipeHandler(switchToAdjacentTab), {
    passive: true,
    capture: true,
  })

  // ── Iframe relay (PDF viewer, HTML viewer, etc.) ─────────────────────────────
  // Iframes capture touch and wheel events — they never reach this document.
  // Pages that embed iframes track gestures internally and dispatch TAB_SWIPE_EVENT
  // on the parent window when a swipe completes. We handle it here exactly like
  // a native swipe so the tab-switching logic stays in one place.

  useEventListener(window, TAB_SWIPE_EVENT, (event: Event) => {
    const { direction } = (event as CustomEvent<TabSwipeGestureEventDetail>).detail
    switchToAdjacentTab(direction)
  })
}
