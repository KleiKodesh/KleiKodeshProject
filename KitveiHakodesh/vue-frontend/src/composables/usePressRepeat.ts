import { onBeforeUnmount } from 'vue'

/** First repeat after this long held; a normal tap stays one step. */
export const PRESS_REPEAT_DELAY_MS = 400
/** Then one step every this long while held. */
export const PRESS_REPEAT_INTERVAL_MS = 80

/**
 * Hold-to-repeat for a toolbar button (the book view's continuous zoom): `start(step)` runs the
 * step at once on pointerdown, then after PRESS_REPEAT_DELAY_MS repeats it every
 * PRESS_REPEAT_INTERVAL_MS until `stop` (pointerup / pointerleave / pointercancel). Stops on
 * unmount too - none of those events fire when the button disappears under the pointer.
 */
export function usePressRepeat() {
  let interval: ReturnType<typeof setInterval> | null = null
  let delay: ReturnType<typeof setTimeout> | null = null

  function start(step: () => void) {
    if (interval !== null || delay !== null) return
    step()
    delay = setTimeout(() => {
      delay = null
      interval = setInterval(step, PRESS_REPEAT_INTERVAL_MS)
    }, PRESS_REPEAT_DELAY_MS)
  }

  function stop() {
    if (delay !== null) {
      clearTimeout(delay)
      delay = null
    }
    if (interval !== null) {
      clearInterval(interval)
      interval = null
    }
  }

  onBeforeUnmount(stop)
  return { start, stop }
}
