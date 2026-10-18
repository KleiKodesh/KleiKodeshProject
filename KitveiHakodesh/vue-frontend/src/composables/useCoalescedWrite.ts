import { useEventListener, useTimeoutFn } from '@vueuse/core'

/**
 * A persistence write that is coalesced off the hot path: `schedule()` arms (or
 * re-arms) a trailing timer, so a burst of state changes — a scroll moving the
 * breadcrumb on every tick, a drag reordering tabs — costs ONE write when the burst
 * ends. The write is flushed early when the page is hidden, closed or reloaded, so
 * the last state is never lost to the delay.
 *
 * Call from inside a component or store setup: the lifecycle listeners are scoped
 * to it. A wipe that is about to reload seals its storage (persistence.ts
 * sealDatabase / sealLocalStorageKeys) so the unload flush cannot resurrect what
 * was deleted — the coalescer itself knows nothing about why a write happens.
 */
export function useCoalescedWrite(write: () => void, delayMs = 300) {
  const timer = useTimeoutFn(write, delayMs, { immediate: false })

  function flush() {
    if (!timer.isPending.value) return
    timer.stop()
    write()
  }

  useEventListener(document, 'visibilitychange', () => {
    if (document.visibilityState === 'hidden') flush()
  })
  useEventListener(window, 'pagehide', flush)
  useEventListener(window, 'beforeunload', flush)

  return { schedule: () => timer.start(), flush }
}
