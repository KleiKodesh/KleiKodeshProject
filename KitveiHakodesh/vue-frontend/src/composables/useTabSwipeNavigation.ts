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
// Deep-decay gate on the re-strike: the reference must have decayed to at most this
// before a spike may re-arm. Calibrated from two REAL __tabSwipeTrace captures
// (2026-07-19, user's precision touchpad in the demo app):
//   • real repeat swipes re-struck off DEEPLY decayed tails — ref 3-6px, spikes 9-22×
//     (3→62, 4→89, 6→55);
//   • finger SPEED PULSES inside one continuous swipe rose off still-fat bases — ref
//     17-34px, rises 1.9-3.9× (34→65, 22→73) — and were false-firing extra tab jumps.
// Magnitude alone cannot tell a momentum tail from a smoothly slowing finger (both
// decay at ~0.92-0.98/event at 16ms cadence — measured), so mid-tail rises are treated
// as finger pulses and only deep-tail spikes as new swipes. This gate is what earlier
// pattern-recognition attempts (à la wheel-gestures momentum detection) got wrong on
// real hardware: a slowing finger produces the exact same "smooth decay" signature.
const TAIL_DECAY_MAX_PX = 15
// Runt immunity for the re-strike reference. Real WebView2 trackpad streams contain
// isolated ~1px "runt" events sandwiched between large deltas (seen in a captured
// __tabSwipeTrace: ...72, 71, 1, 72... mid-swipe). If the checks compared against the
// runt alone, the next normal event would look like a huge strike off a dead tail and
// falsely re-arm — one strong swipe then fired twice. So the reference is
// max(prev, RUNT_DECAY × prevPrev): a single runt can't collapse a two-event memory,
// while a GENUINE decay (two+ consecutive small events) still lowers it, keeping
// legitimate deep-tail re-strikes (e.g. tail 4px → push 89px) fully sensitive.
const RUNT_DECAY = 0.8
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
 *   • a deep-tail re-strike: the momentum tail has decayed to nearly nothing
 *     (runt-immune reference ≤ TAIL_DECAY_MAX_PX) and a new event spikes up
 *     (> REARM_SPIKE_RATIO× the reference, ≥ REARM_SPIKE_FLOOR_PX) — a dead tail
 *     never jumps back up, so that can only be a fresh finger strike; re-arm even
 *     if the stream never paused. Both halves are required: real captures showed a
 *     smoothly SLOWING FINGER inside one long swipe is indistinguishable from a
 *     momentum tail by magnitude (both ~0.92-0.98/event), and its re-acceleration
 *     (2-4× off a 17-34px base) false-fired extra tab jumps — while genuine repeat
 *     swipes in practice land on single-digit tails with 9-22× spikes. Hence: spike
 *     alone is not enough, the tail must be DEEP first.
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
  let prevAbsDeltaX = 0 // |deltaX| of the previous HORIZONTAL event, for the re-strike checks
  let prevPrevAbsDeltaX = 0 // one further back — runt immunity for the re-strike reference

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
    // so the ratio tests below always compare consecutive HORIZONTAL events.)
    if (Math.abs(deltaX) <= Math.abs(deltaY)) return

    const absDeltaX = Math.abs(deltaX)
    const prevAbs = prevAbsDeltaX
    // Runt-immune reference (see RUNT_DECAY): a lone ~1px glitch event between large
    // deltas must not collapse what the ratio tests compare against.
    const refAbs = Math.max(prevAbs, prevPrevAbsDeltaX * RUNT_DECAY)
    prevPrevAbsDeltaX = prevAbsDeltaX
    prevAbsDeltaX = absDeltaX

    if (firedThisGesture) {
      // Locked for the current push. Re-arm on any gesture-boundary signal (see doc
      // comment): opposite-direction flick, post-fire quiet gap, or a deep-tail
      // re-strike (tail decayed to ≤ TAIL_DECAY_MAX_PX, then a spike — mid-tail rises
      // are finger speed pulses inside the SAME swipe and must stay locked).
      const reversed = lastFiredSign !== 0 && Math.sign(deltaX) === -lastFiredSign
      const quietGap = gap > REARM_GAP_MS
      const reStrike =
        refAbs <= TAIL_DECAY_MAX_PX &&
        absDeltaX > Math.max(REARM_SPIKE_FLOOR_PX, refAbs * REARM_SPIKE_RATIO)
      if (reversed || quietGap || reStrike) {
        swipeTrace(`rearm ${reversed ? 'reversal' : quietGap ? `gap=${gap}ms` : `restrike ref=${refAbs.toFixed(0)}->${absDeltaX.toFixed(0)}`}`)
        firedThisGesture = false
        accumulatedDeltaX = 0
        // fall through — this event's delta opens the new count below
      } else {
        swipeTrace(`locked dx=${deltaX.toFixed(0)} gap=${gap}ms ref=${refAbs.toFixed(0)}`)
        return
      }
    }

    // A direction reversal starts a fresh count so leftover delta can't cancel it out.
    if (accumulatedDeltaX !== 0 && Math.sign(deltaX) !== Math.sign(accumulatedDeltaX)) {
      accumulatedDeltaX = 0
    }
    accumulatedDeltaX += deltaX
    swipeTrace(`acc dx=${deltaX.toFixed(0)} gap=${gap}ms sum=${accumulatedDeltaX.toFixed(0)}`)

    if (Math.abs(accumulatedDeltaX) >= TRACKPAD_DELTA_THRESHOLD) {
      const direction = accumulatedDeltaX < 0 ? 'next' : 'previous'
      onSwipe(direction)
      swipeTrace(`FIRE ${direction}`)
      firedThisGesture = true
      lastFiredSign = accumulatedDeltaX < 0 ? -1 : 1
      accumulatedDeltaX = 0
    }
  }
}

// ── Diagnostics ────────────────────────────────────────────────────────────────
// Ring buffer of recent handler decisions, dumpable from DevTools with
// window.__tabSwipeTrace(). Synthetic dev-rig streams have repeatedly differed from
// real trackpad/WebView2 streams, so being able to capture the REAL stream (per-event
// delta, gap, lock state, which signal re-armed) on an affected machine is what turns
// tuning from guesswork into data. Strings only, capped — negligible overhead.
const TRACE_MAX = 200
const traceBuf: string[] = []

function swipeTrace(msg: string) {
  if (traceBuf.length >= TRACE_MAX) traceBuf.shift()
  traceBuf.push(`${(performance.now() / 1000).toFixed(3)} ${msg}`)
}

declare global {
  interface Window {
    __tabSwipeTrace?: () => string[]
  }
}
if (typeof window !== 'undefined') {
  window.__tabSwipeTrace = () => [...traceBuf]
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
