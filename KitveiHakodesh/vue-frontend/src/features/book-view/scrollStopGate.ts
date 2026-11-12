/**
 * Decides when the reader's scroll has STOPPED, so work that follows the reading
 * position (the auto-selected commentary load) runs once per scroll instead of on
 * every scroll step.
 *
 * A stop is:
 *   - the browser's `scrollend` on the viewport (ended), or
 *   - QUIET_MS with no scroll movement, in case scrollend does not come, or
 *   - the release of the scrollbar (pointerUp) after a drag or track hold.
 * While the scrollbar is held nothing counts as a stop: the app's drawn bar writes
 * scrollTop on every pointer move, and each write can end in its own scrollend.
 */
export const SCROLL_STOP_QUIET_MS = 300

export interface ScrollStopGate {
  /** The reader moved the view; a stop is now owed. */
  moved(): void
  /** The browser's scrollend for the viewport. */
  ended(): void
  /** The scrollbar was pressed (drag or track hold). */
  pointerDown(): void
  /** The scrollbar was released. */
  pointerUp(): void
  /** Drop any owed stop without firing it. */
  cancel(): void
}

export function createScrollStopGate(onStop: () => void, quietMs = SCROLL_STOP_QUIET_MS): ScrollStopGate {
  let owed = false
  let held = false
  let quietTimer: ReturnType<typeof setTimeout> | null = null

  function clearQuietTimer() {
    if (quietTimer) {
      clearTimeout(quietTimer)
      quietTimer = null
    }
  }

  function fire() {
    clearQuietTimer()
    if (!owed) return
    owed = false
    onStop()
  }

  return {
    moved() {
      owed = true
      clearQuietTimer()
      if (!held) quietTimer = setTimeout(fire, quietMs)
    },
    ended() {
      if (!held) fire()
    },
    pointerDown() {
      held = true
      clearQuietTimer()
    },
    pointerUp() {
      if (!held) return
      held = false
      fire()
    },
    cancel() {
      owed = false
      clearQuietTimer()
    },
  }
}
