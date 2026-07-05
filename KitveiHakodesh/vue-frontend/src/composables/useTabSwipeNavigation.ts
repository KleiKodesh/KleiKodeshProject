import { useSwipe } from '@vueuse/core'
import { useEventListener } from '@vueuse/core'
import { useTabStore } from '@/stores/tabStore'

const TRACKPAD_DELTA_THRESHOLD = 150
const TRACKPAD_COOLDOWN_MS = 400
const TOUCH_THRESHOLD_PX = 60

// Custom event name used by iframe relays (PdfViewPage, HtmlViewPage, etc.) to
// signal a completed swipe gesture that originated inside an iframe where native
// touch/wheel events cannot bubble to the parent document.
export const TAB_SWIPE_EVENT = 'tab-swipe-gesture'

export interface TabSwipeGestureEventDetail {
  direction: 'next' | 'previous'
}

/**
 * Wires up tab switching via horizontal swipe (touch) and trackpad horizontal scroll.
 *
 * Direction convention (matches browser tab bar behaviour):
 *   swipe left  (finger moves left)  → next tab (advance forward in tab list)
 *   swipe right (finger moves right) → previous tab (go back in tab list)
 *
 * Touch: uses VueUse useSwipe on the document body.
 * Trackpad: listens to wheel events and accumulates deltaX until a threshold is crossed.
 * A cooldown prevents multiple switches from a single long swipe gesture.
 *
 * Iframe relay: iframes (PDF viewer, HTML viewer) cannot bubble touch/wheel events
 * to the parent document. Those pages track gestures internally and fire a
 * TAB_SWIPE_EVENT CustomEvent on the parent window when a swipe is detected.
 * This composable listens for that event and handles it identically to native gestures.
 */
export function useTabSwipeNavigation() {
  const tabStore = useTabStore()

  function switchToAdjacentTab(direction: 'next' | 'previous') {
    const tabs = tabStore.tabs
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
      if (direction === 'left') switchToAdjacentTab('next')
      else if (direction === 'right') switchToAdjacentTab('previous')
    },
  })

  // ── Trackpad horizontal scroll (native — parent document) ───────────────────

  let accumulatedDeltaX = 0
  let lastSwitchTime = 0

  useEventListener(
    document,
    'wheel',
    (event: WheelEvent) => {
      // Ignore purely vertical scrolls — only act when horizontal delta dominates
      if (Math.abs(event.deltaX) <= Math.abs(event.deltaY)) return

      const now = Date.now()
      if (now - lastSwitchTime < TRACKPAD_COOLDOWN_MS) {
        // Still in cooldown — reset accumulator so the next gesture starts fresh
        accumulatedDeltaX = 0
        return
      }

      accumulatedDeltaX += event.deltaX

      if (Math.abs(accumulatedDeltaX) >= TRACKPAD_DELTA_THRESHOLD) {
        const direction = accumulatedDeltaX > 0 ? 'next' : 'previous'
        accumulatedDeltaX = 0
        lastSwitchTime = now
        switchToAdjacentTab(direction)
      }
    },
    { passive: true },
  )

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
