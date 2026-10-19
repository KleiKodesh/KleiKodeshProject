import { tryOnScopeDispose, useTimeoutFn } from '@vueuse/core'
import { registerPendingWriter } from '@/webview-host/persistenceFlush'

/**
 * A persistence write that is coalesced off the hot path: `schedule()` arms (or
 * re-arms) a trailing timer, so a burst of state changes — a scroll moving the
 * breadcrumb on every tick, a drag reordering tabs — costs ONE write when the burst
 * ends. The write is flushed early when the page is hidden, closed or reloaded, so
 * the last state is never lost to the delay - by webview-host/persistenceFlush.ts,
 * which each instance registers with: it runs the position captures first and the
 * writers after, so a write scheduled BY a capture (the recents list, once the
 * closing position is folded in) still goes out. The WebView2 hosts fire none of
 * those events when they close their window; they ask for every pending write
 * through `window.__khsFlushPersistence`, served from the same registry.
 *
 * `write` may return a promise (an IndexedDB put); `flush` hands it back so a host
 * waiting for the write to land can await it. The timer path ignores it.
 *
 * Call from inside a component or store setup: the lifecycle listeners are scoped
 * to it. A wipe that is about to reload seals its storage (persistence.ts
 * sealDatabase / sealLocalStorageKeys) so the unload flush cannot resurrect what
 * was deleted — the coalescer itself knows nothing about why a write happens.
 */
export function useCoalescedWrite(write: () => void | Promise<unknown>, delayMs = 300) {
  const timer = useTimeoutFn(() => { void write() }, delayMs, { immediate: false })

  function flush(): void | Promise<unknown> {
    if (!timer.isPending.value) return
    timer.stop()
    return write()
  }

  tryOnScopeDispose(registerPendingWriter(flush))

  return { schedule: () => timer.start(), flush }
}
