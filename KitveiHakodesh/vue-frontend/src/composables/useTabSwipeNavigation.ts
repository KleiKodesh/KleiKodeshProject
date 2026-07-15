import { useSwipe } from '@vueuse/core'
import { useEventListener } from '@vueuse/core'
import { useTabStore } from '@/stores/tabStore'

// Horizontal travel (in px) that counts as a deliberate swipe. Precision touchpads
// report large per-event deltas (~100+), so this needs to span a few events — too
// small and a mere flick fires. The vertical-dominance guard below keeps ordinary
// page scrolling from reaching it.
const TRACKPAD_DELTA_THRESHOLD = 150
// A single physical swipe arrives as a dense run of wheel events (finger movement +
// momentum tail). A quiet gap longer than this means the finger lifted and the gesture
// ended, so the next swipe may fire. Short enough that back-to-back swipes (each after
// a real finger-lift pause) each register, while a single continuous swipe still fires
// once (its internal gaps never reach this).
const TRACKPAD_GESTURE_END_MS = 150
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
 * ONE tab, and you can do several in a row to cycle.
 *
 * It accumulates horizontal delta and fires once when the running total crosses the
 * threshold, then LOCKS for the rest of the push. It re-arms — so the next swipe can
 * fire — only on an unambiguous gesture boundary:
 *   • a direction reversal (you flicked back the other way — instant, no pause), or
 *   • a real pause (gap > TRACKPAD_GESTURE_END_MS).
 * Reversal is what lets you cycle back-and-forth with no pause. It is deliberately the
 * ONLY magnitude-based signal: a long swipe naturally dips in speed mid-gesture, and
 * treating a dip as "finger lifted" made a single swipe fire twice — so we never do.
 * (Same-direction repeats therefore need a brief pause between them.)
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

  return (event: WheelEvent) => {
    const now = Date.now()
    const gap = now - lastWheelTime
    lastWheelTime = now

    // Normalize line/page wheel modes to pixels so the threshold is unit-independent.
    const unit =
      event.deltaMode === 1 ? WHEEL_LINE_HEIGHT_PX : event.deltaMode === 2 ? window.innerHeight : 1
    const deltaX = event.deltaX * unit
    const deltaY = event.deltaY * unit

    // A real pause means the previous swipe (finger + momentum) ended — re-arm.
    if (gap > TRACKPAD_GESTURE_END_MS) {
      firedThisGesture = false
      accumulatedDeltaX = 0
    }

    // Ignore vertical-dominant scrolling.
    if (Math.abs(deltaX) <= Math.abs(deltaY)) return

    if (firedThisGesture) {
      // Locked for the current push. A firm flick in the OPPOSITE direction to the last
      // switch is a new swipe (cycling back) — re-arm immediately, no pause needed.
      if (lastFiredSign !== 0 && Math.sign(deltaX) === -lastFiredSign) {
        firedThisGesture = false
        accumulatedDeltaX = 0
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
